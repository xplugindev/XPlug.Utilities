using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Configuration;
using Screen2VMS.Engine;

namespace Screen2VMS.App.ViewModels;

/// <summary>
/// Drives the main window (spec 28, 63).
/// </summary>
/// <remarks>
/// Presentation only. Owns the collection of configured cameras and the
/// "add a camera" flow; everything about running one camera lives in its own
/// <see cref="CameraTileViewModel"/> (spec override, see CLAUDE.md
/// "multi-camera").
/// </remarks>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ICameraSourceService cameraService;
    private readonly IConfigurationService configuration;
    private readonly CameraRuntimeManager runtimeManager;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MainViewModel> logger;
    private readonly DispatcherTimer statusTimer;
    private readonly Dispatcher dispatcher;

    private CameraDevice? deviceToAdd;
    /// <summary>How long a one-off message outlives the once-a-second status refresh.</summary>
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(10);

    private string statusMessage = "Add a camera to get started.";
    private string? notice;
    private DateTime noticeExpiresUtc;
    private bool disposed;

    public MainViewModel(
        ICameraSourceService cameraService,
        IConfigurationService configuration,
        CameraRuntimeManager runtimeManager,
        Dispatcher dispatcher,
        ILoggerFactory loggerFactory)
    {
        this.cameraService = cameraService;
        this.configuration = configuration;
        this.runtimeManager = runtimeManager;
        this.dispatcher = dispatcher;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<MainViewModel>();

        AddCameraCommand = new RelayCommand(AddCamera, () => DeviceToAdd is not null);
        RefreshCommand = new RelayCommand(RefreshDevices);
        StartAllCommand = new RelayCommand(StartAll, () => Cameras.Any(c => !c.IsRunning && !c.DeviceMissing));
        StopAllCommand = new RelayCommand(StopAll, () => Cameras.Any(c => c.IsRunning));
        OpenLogsCommand = new RelayCommand(OpenLogs);
        CreateFirewallRulesCommand = new RelayCommand(CreateFirewallRules);

        statusTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        statusTimer.Tick += (_, _) => RefreshStatistics();
        statusTimer.Start();

        Cameras.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoCamerasConfigured));

        LoadConfiguredCameras();
        RefreshDevices();
    }

    public ObservableCollection<CameraTileViewModel> Cameras { get; } = new();

    /// <summary>Whether the "add a camera to get started" placeholder should show.</summary>
    public bool NoCamerasConfigured => Cameras.Count == 0;

    public ObservableCollection<CameraDevice> AvailableDevices { get; } = new();

    public RelayCommand AddCameraCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand StartAllCommand { get; }

    public RelayCommand StopAllCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand CreateFirewallRulesCommand { get; }

    public CameraDevice? DeviceToAdd
    {
        get => deviceToAdd;
        set
        {
            if (SetProperty(ref deviceToAdd, value))
            {
                AddCameraCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        statusTimer.Stop();

        foreach (var camera in Cameras)
        {
            camera.Dispose();
        }
    }

    /// <summary>Builds one tile per profile already in config.json. None are started automatically.</summary>
    private void LoadConfiguredCameras()
    {
        var devices = cameraService.EnumerateDevices();

        foreach (var profile in configuration.Current.Cameras)
        {
            var device = devices.FirstOrDefault(d => d.Id == profile.Camera.DeviceId);
            Cameras.Add(CreateTile(profile, device));
        }

        UpdateStatusMessage();
    }

    private void AddCamera()
    {
        var device = DeviceToAdd;
        if (device is null)
        {
            return;
        }

        var (rtspPort, onvifPort) = CameraProfileDefaults.NextPorts(configuration.Current.Cameras);

        var profile = new CameraProfile
        {
            Device = new DeviceConfiguration
            {
                SerialNumber = DeviceIdentity.NewSerialNumber(),
                MacAddress = DeviceIdentity.NewMacAddress(),
            },
            Camera = new CameraConfiguration { DeviceId = device.Id, Name = device.Name },
            Rtsp = new RtspConfiguration { Port = rtspPort },
            Onvif = new OnvifConfiguration { Port = onvifPort },
        };

        configuration.Update(config => config with { Cameras = [.. config.Cameras, profile] });

        Cameras.Add(CreateTile(profile, device));
        RefreshDevices();
        UpdateStatusMessage();
    }

    private CameraTileViewModel CreateTile(CameraProfile profile, CameraDevice? device)
    {
        var runtime = runtimeManager.GetOrCreate(profile.Id);
        var tile = new CameraTileViewModel(
            profile,
            device,
            configuration,
            runtime,
            dispatcher,
            loggerFactory.CreateLogger<CameraTileViewModel>(),
            UpdateProfile,
            RemoveCamera);

        tile.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(CameraTileViewModel.IsRunning) or nameof(CameraTileViewModel.DeviceMissing))
            {
                RaiseCommandStates();
            }
        };

        return tile;
    }

    private void RemoveCamera(CameraTileViewModel tile)
    {
        Cameras.Remove(tile);
        tile.Dispose();
        runtimeManager.Remove(tile.Id);
        configuration.Update(config => config with { Cameras = config.Cameras.Where(p => p.Id != tile.Id).ToList() });

        RefreshDevices();

        // Closing its ports needs elevation, and a UAC prompt on every Remove
        // would be worse than a hint: the next Firewall Rules sync removes them.
        ShowNotice($"Removed {tile.Name}. If you created firewall rules, press Firewall Rules to close its ports.");
        RaiseCommandStates();
    }

    private void UpdateProfile(string profileId, Func<CameraProfile, CameraProfile> mutate) =>
        configuration.Update(config => config with
        {
            Cameras = config.Cameras.Select(p => p.Id == profileId ? mutate(p) : p).ToList(),
        });

    private void RefreshDevices()
    {
        var devices = cameraService.EnumerateDevices();

        // Each tile is matched to the physical device it was configured for,
        // independent of whether that device is still attached - a camera
        // that is unplugged should show as missing, not disappear.
        var configuredDeviceIds = configuration.Current.Cameras
            .Select(p => p.Camera.DeviceId)
            .Where(id => id is not null)
            .ToHashSet();

        foreach (var camera in Cameras)
        {
            var configuredId = configuration.Current.Cameras.FirstOrDefault(p => p.Id == camera.Id)?.Camera.DeviceId;
            camera.UpdateDevice(devices.FirstOrDefault(d => d.Id == configuredId));
        }

        AvailableDevices.Clear();
        foreach (var device in devices.Where(d => !configuredDeviceIds.Contains(d.Id)))
        {
            AvailableDevices.Add(device);
        }

        DeviceToAdd = AvailableDevices.FirstOrDefault();
        UpdateStatusMessage();
        RaiseCommandStates();
    }

    /// <summary>
    /// Shows a message that the once-a-second refresh leaves alone for a while.
    /// Without this, the result of an action was overwritten within a second.
    /// </summary>
    private void ShowNotice(string message)
    {
        notice = message;
        noticeExpiresUtc = DateTime.UtcNow + NoticeDuration;
        UpdateStatusMessage();
    }

    private void UpdateStatusMessage()
    {
        if (notice is not null)
        {
            if (DateTime.UtcNow < noticeExpiresUtc)
            {
                StatusMessage = notice;
                return;
            }

            notice = null;
        }

        if (Cameras.Count == 0)
        {
            StatusMessage = AvailableDevices.Count == 0
                ? "No camera found. Connect a webcam and press Refresh."
                : "Add a camera to get started.";
            return;
        }

        var running = Cameras.Count(c => c.IsRunning);
        StatusMessage = $"{Cameras.Count} camera(s) configured, {running} running.";
    }

    private void StartAll()
    {
        foreach (var camera in Cameras.Where(c => !c.IsRunning && !c.DeviceMissing))
        {
            camera.StartCommand.Execute(null);
        }

        UpdateStatusMessage();
    }

    private void StopAll()
    {
        foreach (var camera in Cameras.Where(c => c.IsRunning))
        {
            camera.StopCommand.Execute(null);
        }

        UpdateStatusMessage();
    }

    private void RefreshStatistics()
    {
        foreach (var camera in Cameras)
        {
            camera.RefreshStatistics();
        }

        UpdateStatusMessage();
    }

    private void CreateFirewallRules()
    {
        // A full sync: rules for removed cameras and from older versions go too.
        var rules = FirewallRules.For(configuration.Current);
        var cameraCount = configuration.Current.Cameras.Count;

        ShowNotice(FirewallRules.TrySync(rules, logger)
            ? cameraCount == 0
                ? "Firewall rules removed - no cameras are configured."
                : $"Firewall rules updated for {cameraCount} camera(s)."
            : "Firewall rules were not changed. Administrator approval is required.");
    }

    private void OpenLogs()
    {
        AppPaths.EnsureCreated();

        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.LogDirectory,
            UseShellExecute = true,
        });
    }

    private void RaiseCommandStates()
    {
        StartAllCommand.RaiseCanExecuteChanged();
        StopAllCommand.RaiseCanExecuteChanged();
        AddCameraCommand.RaiseCanExecuteChanged();
    }
}

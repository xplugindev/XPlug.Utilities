using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Screen2VMS.App.Views;
using Screen2VMS.Configuration;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Configuration;
using Screen2VMS.Core.Diagnostics;
using Screen2VMS.Engine;

namespace Screen2VMS.App.ViewModels;

/// <summary>
/// Drives the main window (spec 28, 63).
/// </summary>
/// <remarks>
/// Presentation only. Everything it shows comes from
/// <see cref="Screen2VmsRuntime"/>, which knows nothing about WPF.
/// </remarks>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ICameraSourceService cameraService;
    private readonly IConfigurationService configuration;
    private readonly Screen2VmsRuntime runtime;
    private readonly ILogger<MainViewModel> logger;
    private readonly PreviewSink preview;
    private readonly DispatcherTimer statusTimer;
    private readonly Dispatcher dispatcher;

    private CameraDevice? selectedDevice;
    private Resolution? selectedResolution;
    private double selectedFrameRate = CameraSettings.DefaultFrameRate;
    private int bitrateKbps = 4000;
    private string statusMessage = "Select a camera and press Start.";
    private string onvifPassword = string.Empty;
    private bool disposed;

    public MainViewModel(
        ICameraSourceService cameraService,
        IConfigurationService configuration,
        Screen2VmsRuntime runtime,
        Dispatcher dispatcher,
        ILogger<MainViewModel> logger)
    {
        this.cameraService = cameraService;
        this.configuration = configuration;
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        this.logger = logger;

        preview = new PreviewSink(dispatcher);
        preview.ImageChanged += (_, _) => OnPropertyChanged(nameof(PreviewImage));

        StartCommand = new RelayCommand(Start, () => SelectedDevice is not null && !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        RefreshCommand = new RelayCommand(RefreshDevices, () => !IsRunning);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        CopyStreamUriCommand = new RelayCommand(CopyStreamUri, () => !string.IsNullOrEmpty(StreamUri));
        CopyOnvifUriCommand = new RelayCommand(CopyOnvifUri, () => !string.IsNullOrEmpty(OnvifUri));
        CopyPasswordCommand = new RelayCommand(CopyPassword, () => !string.IsNullOrEmpty(OnvifPassword));
        CreateFirewallRulesCommand = new RelayCommand(CreateFirewallRules);

        runtime.Health.Changed += OnHealthChanged;

        statusTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        statusTimer.Tick += (_, _) => RefreshStatistics();
        statusTimer.Start();

        LoadCredentials();
        RefreshDevices();
    }

    public ObservableCollection<CameraDevice> Devices { get; } = new();

    public ObservableCollection<Resolution> Resolutions { get; } = new();

    public ObservableCollection<double> FrameRates { get; } = new();

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand CopyStreamUriCommand { get; }

    public RelayCommand CopyOnvifUriCommand { get; }

    public RelayCommand CopyPasswordCommand { get; }

    public RelayCommand CreateFirewallRulesCommand { get; }

    public ImageSource? PreviewImage => preview.Image;

    public bool IsRunning => runtime.IsRunning;

    public CameraDevice? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (SetProperty(ref selectedDevice, value))
            {
                RebuildModeLists();
                RaiseCommandStates();
            }
        }
    }

    public Resolution? SelectedResolution
    {
        get => selectedResolution;
        set
        {
            if (SetProperty(ref selectedResolution, value))
            {
                RebuildFrameRates();
            }
        }
    }

    public double SelectedFrameRate
    {
        get => selectedFrameRate;
        set => SetProperty(ref selectedFrameRate, value);
    }

    public int BitrateKbps
    {
        get => bitrateKbps;
        set => SetProperty(ref bitrateKbps, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string OnvifUserName => configuration.Current.Onvif.Username;

    public string OnvifPassword
    {
        get => onvifPassword;
        private set => SetProperty(ref onvifPassword, value);
    }

    public string? StreamUri => runtime.StreamUri;

    public string? OnvifUri => runtime.OnvifUri;

    // --- Component status (spec 36, 63) ---

    public ComponentState CameraState => StateOf(ComponentNames.Camera);

    public ComponentState EncoderState => StateOf(ComponentNames.Encoder);

    public ComponentState RtspState => StateOf(ComponentNames.Rtsp);

    public ComponentState OnvifState => StateOf(ComponentNames.Onvif);

    public ComponentState DiscoveryState => StateOf(ComponentNames.Discovery);

    public string CameraDetail => DetailOf(ComponentNames.Camera);

    public string EncoderDetail => DetailOf(ComponentNames.Encoder);

    public string RtspDetail => DetailOf(ComponentNames.Rtsp);

    public string OnvifDetail => DetailOf(ComponentNames.Onvif);

    public string DiscoveryDetail => DetailOf(ComponentNames.Discovery);

    public string ClientCountText => runtime.StreamManager.RtspServer.ClientCount.ToString();

    public string MeasuredFpsText
    {
        get
        {
            var camera = runtime.StreamManager.Camera;
            return camera is null ? "-" : $"{camera.Statistics.MeasuredFrameRate:0.0} fps";
        }
    }

    public string MeasuredBitrateText
    {
        get
        {
            var encoder = runtime.StreamManager.Encoder;
            return encoder is null ? "-" : $"{encoder.Statistics.MeasuredBitrateKbps:0} kbps";
        }
    }

    public string FramesText
    {
        get
        {
            var encoder = runtime.StreamManager.Encoder;
            if (encoder is null)
            {
                return "-";
            }

            var statistics = encoder.Statistics;
            return $"{statistics.FramesEncoded:N0} encoded, {statistics.FramesDropped:N0} dropped";
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        statusTimer.Stop();
        runtime.Health.Changed -= OnHealthChanged;
        Stop();
        preview.Dispose();
    }

    /// <summary>
    /// Loads the ONVIF password, generating one on first run.
    /// </summary>
    /// <remarks>
    /// Spec 24 forbids a universal default password, so each installation gets
    /// its own. It is shown in the window because the operator has to type it
    /// into the VMS.
    /// </remarks>
    private void LoadCredentials()
    {
        var stored = PasswordProtector.Unprotect(configuration.Current.Onvif.ProtectedPassword);

        if (stored is null)
        {
            stored = PasswordProtector.GeneratePassword();
            configuration.Update(config => config with
            {
                Onvif = config.Onvif with { ProtectedPassword = PasswordProtector.Protect(stored) },
            });

            logger.LogInformation("Generated a new ONVIF password for this installation.");
        }

        OnvifPassword = stored;
        BitrateKbps = configuration.Current.Encoder.BitrateKbps;
    }

    private void RefreshDevices()
    {
        var previousId = SelectedDevice?.Id ?? configuration.Current.Camera.DeviceId;

        Devices.Clear();
        foreach (var device in cameraService.EnumerateDevices())
        {
            Devices.Add(device);
        }

        if (Devices.Count == 0)
        {
            SelectedDevice = null;
            StatusMessage = "No camera found. Connect a webcam and press Refresh.";
            return;
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == previousId) ?? Devices[0];
        StatusMessage = $"{Devices.Count} camera(s) found.";
    }

    private void RebuildModeLists()
    {
        Resolutions.Clear();

        if (SelectedDevice is null)
        {
            SelectedResolution = null;
            return;
        }

        if (SelectedDevice.Modes.Count == 0)
        {
            StatusMessage =
                $"'{SelectedDevice.Name}' could not be opened to read its modes. " +
                "It may be in use by another application.";
            SelectedResolution = null;
            return;
        }

        var resolutions = SelectedDevice.Modes
            .Select(m => new Resolution(m.Width, m.Height))
            .Distinct()
            .OrderByDescending(r => r.PixelCount)
            .ToList();

        foreach (var resolution in resolutions)
        {
            Resolutions.Add(resolution);
        }

        var preferred = configuration.Current.Camera;

        SelectedResolution =
            resolutions.FirstOrDefault(r => r.Width == preferred.Width && r.Height == preferred.Height)
            ?? resolutions.FirstOrDefault(r => r.Width == CameraSettings.DefaultWidth && r.Height == CameraSettings.DefaultHeight)
            ?? resolutions.FirstOrDefault(r => r.Width == CameraSettings.FallbackWidth && r.Height == CameraSettings.FallbackHeight)
            ?? resolutions[0];
    }

    private void RebuildFrameRates()
    {
        FrameRates.Clear();

        if (SelectedDevice is null || SelectedResolution is null)
        {
            return;
        }

        var rates = SelectedDevice.Modes
            .Where(m => m.Width == SelectedResolution.Width && m.Height == SelectedResolution.Height)
            .SelectMany(m => new[] { m.MinFrameRate, m.MaxFrameRate })
            .Where(r => r > 0)
            .Select(r => Math.Round(r, 2))
            .Distinct()
            .OrderByDescending(r => r)
            .ToList();

        if (rates.Count == 0)
        {
            rates.Add(CameraSettings.DefaultFrameRate);
        }

        foreach (var rate in rates)
        {
            FrameRates.Add(rate);
        }

        var configured = configuration.Current.Camera.Fps;
        SelectedFrameRate = rates.Contains(configured)
            ? configured
            : rates.FirstOrDefault(r => Math.Abs(r - CameraSettings.DefaultFrameRate) < 0.01, rates[0]);
    }

    private void Start()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var cameraSettings = new CameraSettings
        {
            Width = SelectedResolution?.Width ?? CameraSettings.DefaultWidth,
            Height = SelectedResolution?.Height ?? CameraSettings.DefaultHeight,
            FrameRate = SelectedFrameRate,
        };

        try
        {
            StatusMessage = $"Starting {SelectedDevice.Name}...";
            PersistSelection(cameraSettings);

            runtime.Start(configuration.Current, SelectedDevice.Id, cameraSettings, OnvifPassword);
            runtime.StreamManager.Camera?.AddSink(preview);

            StatusMessage = runtime.OnvifUri is null
                ? "Streaming. ONVIF did not start; check the log."
                : "Streaming. The camera is discoverable on the network.";
        }
        catch (CameraBusyException ex)
        {
            StatusMessage = ex.Message;
            logger.LogWarning(ex, "Camera {Camera} is unavailable.", SelectedDevice.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.LogError(ex, "Could not start Screen2VMS.");
        }
        finally
        {
            RaiseAllStatus();
        }
    }

    private void Stop()
    {
        runtime.StreamManager.Camera?.RemoveSink(preview);
        runtime.Stop();

        StatusMessage = "Stopped.";
        RaiseAllStatus();
    }

    private void PersistSelection(CameraSettings settings)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        configuration.Update(config => config with
        {
            Camera = config.Camera with
            {
                DeviceId = device.Id,
                Name = device.Name,
                Width = settings.Width,
                Height = settings.Height,
                Fps = settings.FrameRate,
            },
            Encoder = config.Encoder with { BitrateKbps = BitrateKbps },
        });
    }

    private void OnHealthChanged(object? sender, HealthSnapshot snapshot) =>
        dispatcher.BeginInvoke(new Action(RaiseAllStatus));

    private void RefreshStatistics()
    {
        if (!IsRunning)
        {
            return;
        }

        OnPropertyChanged(nameof(MeasuredFpsText));
        OnPropertyChanged(nameof(MeasuredBitrateText));
        OnPropertyChanged(nameof(FramesText));
        OnPropertyChanged(nameof(ClientCountText));
    }

    private void RaiseAllStatus()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StreamUri));
        OnPropertyChanged(nameof(OnvifUri));
        OnPropertyChanged(nameof(CameraState));
        OnPropertyChanged(nameof(EncoderState));
        OnPropertyChanged(nameof(RtspState));
        OnPropertyChanged(nameof(OnvifState));
        OnPropertyChanged(nameof(DiscoveryState));
        OnPropertyChanged(nameof(CameraDetail));
        OnPropertyChanged(nameof(EncoderDetail));
        OnPropertyChanged(nameof(RtspDetail));
        OnPropertyChanged(nameof(OnvifDetail));
        OnPropertyChanged(nameof(DiscoveryDetail));
        RefreshStatistics();
        RaiseCommandStates();
    }

    private ComponentState StateOf(string component) =>
        runtime.Health.Current.Components.TryGetValue(component, out var health)
            ? health.State
            : ComponentState.Stopped;

    private string DetailOf(string component) =>
        runtime.Health.Current.Components.TryGetValue(component, out var health)
            ? health.Detail ?? health.State.ToString()
            : "Stopped";

    private void CopyStreamUri() => CopyToClipboard(StreamUri, "RTSP address copied.");

    private void CopyOnvifUri() => CopyToClipboard(OnvifUri, "ONVIF address copied.");

    private void CopyPassword() => CopyToClipboard(OnvifPassword, "Password copied.");

    private void CopyToClipboard(string? text, string confirmation)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            StatusMessage = confirmation;
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; not worth an error.
            logger.LogDebug(ex, "Could not write to the clipboard.");
        }
    }

    private void CreateFirewallRules()
    {
        var config = configuration.Current;

        StatusMessage = FirewallRules.TryCreate(
            config.Rtsp.Port,
            config.Onvif.Port,
            config.Discovery.Port,
            logger)
            ? "Firewall rules created."
            : "Firewall rules were not created. Administrator approval is required.";
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
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        CopyStreamUriCommand.RaiseCanExecuteChanged();
        CopyOnvifUriCommand.RaiseCanExecuteChanged();
        CopyPasswordCommand.RaiseCanExecuteChanged();
    }

    /// <summary>A resolution offered in the dropdown.</summary>
    public sealed record Resolution(int Width, int Height)
    {
        public long PixelCount => (long)Width * Height;

        public override string ToString() => $"{Width} x {Height}";
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Screen2VMS.App.Views;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Configuration;

namespace Screen2VMS.App.ViewModels;

/// <summary>
/// Drives the Phase 1 window: pick a camera, start it, watch it run.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ICameraSourceService cameraService;
    private readonly IConfigurationService configuration;
    private readonly ILogger<MainViewModel> logger;
    private readonly PreviewSink preview;
    private readonly DispatcherTimer statisticsTimer;

    private ICameraSource? camera;
    private CameraDevice? selectedDevice;
    private Resolution? selectedResolution;
    private double selectedFrameRate = CameraSettings.DefaultFrameRate;
    private CameraState state = CameraState.Stopped;
    private string statusMessage = "Select a camera and press Start.";
    private string activeModeText = "-";
    private string measuredFpsText = "-";
    private string framesText = "-";
    private bool disposed;

    public MainViewModel(
        ICameraSourceService cameraService,
        IConfigurationService configuration,
        Dispatcher dispatcher,
        ILogger<MainViewModel> logger)
    {
        this.cameraService = cameraService;
        this.configuration = configuration;
        this.logger = logger;

        preview = new PreviewSink(dispatcher);
        preview.ImageChanged += (_, _) => OnPropertyChanged(nameof(PreviewImage));

        StartCommand = new RelayCommand(Start, () => SelectedDevice is not null && !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        RefreshCommand = new RelayCommand(RefreshDevices, () => !IsRunning);
        OpenLogsCommand = new RelayCommand(OpenLogs);

        statisticsTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        statisticsTimer.Tick += (_, _) => RefreshStatistics();
        statisticsTimer.Start();

        RefreshDevices();
    }

    public ObservableCollection<CameraDevice> Devices { get; } = new();

    public ObservableCollection<Resolution> Resolutions { get; } = new();

    public ObservableCollection<double> FrameRates { get; } = new();

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public ImageSource? PreviewImage => preview.Image;

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

    public CameraState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StateText));
                RaiseCommandStates();
            }
        }
    }

    public bool IsRunning => State == CameraState.Running;

    public string StateText => State switch
    {
        CameraState.Running => "Running",
        CameraState.Starting => "Starting",
        CameraState.Stopped => "Stopped",
        CameraState.Busy => "Unavailable",
        CameraState.Disconnected => "Disconnected",
        _ => "Error",
    };

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string ActiveModeText
    {
        get => activeModeText;
        private set => SetProperty(ref activeModeText, value);
    }

    public string MeasuredFpsText
    {
        get => measuredFpsText;
        private set => SetProperty(ref measuredFpsText, value);
    }

    public string FramesText
    {
        get => framesText;
        private set => SetProperty(ref framesText, value);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        statisticsTimer.Stop();
        Stop();
        preview.Dispose();
    }

    private void RefreshDevices()
    {
        var previousSelectionId = SelectedDevice?.Id ?? configuration.Current.Camera.DeviceId;

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

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == previousSelectionId) ?? Devices[0];
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

        // Fall back the way spec 9 asks: the configured size, then 1080p, then
        // 720p, then whatever the camera does offer.
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

        var settings = new CameraSettings
        {
            Width = SelectedResolution?.Width ?? CameraSettings.DefaultWidth,
            Height = SelectedResolution?.Height ?? CameraSettings.DefaultHeight,
            FrameRate = SelectedFrameRate,
        };

        try
        {
            State = CameraState.Starting;
            StatusMessage = $"Starting {SelectedDevice.Name}...";

            camera = cameraService.CreateSource(SelectedDevice.Id);
            camera.StateChanged += OnCameraStateChanged;
            camera.AddSink(preview);
            camera.Start(settings);

            PersistSelection(settings);

            State = camera.State;
            ActiveModeText = camera.ActiveMode?.ToString() ?? "-";
            StatusMessage = $"Streaming from {SelectedDevice.Name}.";
        }
        catch (CameraBusyException ex)
        {
            TearDownCamera();
            State = CameraState.Busy;
            StatusMessage = ex.Message;
            logger.LogWarning(ex, "Camera {Camera} is unavailable.", SelectedDevice.Name);
        }
        catch (CameraException ex)
        {
            TearDownCamera();
            State = CameraState.Error;
            StatusMessage = ex.Message;
            logger.LogError(ex, "Could not start camera {Camera}.", SelectedDevice.Name);
        }
    }

    private void Stop()
    {
        if (camera is null)
        {
            return;
        }

        TearDownCamera();

        State = CameraState.Stopped;
        ActiveModeText = "-";
        MeasuredFpsText = "-";
        FramesText = "-";
        StatusMessage = "Stopped.";
    }

    private void TearDownCamera()
    {
        if (camera is null)
        {
            return;
        }

        camera.StateChanged -= OnCameraStateChanged;
        camera.RemoveSink(preview);
        camera.Dispose();
        camera = null;
    }

    private void OnCameraStateChanged(object? sender, CameraState newState)
    {
        // Raised from the capture thread, so bounce it onto the UI thread.
        statisticsTimer.Dispatcher.BeginInvoke(new Action(() =>
        {
            State = newState;

            if (newState == CameraState.Disconnected)
            {
                StatusMessage = "Camera disconnected.";
            }
        }));
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
        });
    }

    private void RefreshStatistics()
    {
        if (camera is null || !IsRunning)
        {
            return;
        }

        var statistics = camera.Statistics;
        MeasuredFpsText = $"{statistics.MeasuredFrameRate:0.0} fps";
        FramesText = statistics.FramesDropped == 0
            ? $"{statistics.FramesCaptured:N0} captured"
            : $"{statistics.FramesCaptured:N0} captured, {statistics.FramesDropped:N0} dropped";
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
    }

    /// <summary>A resolution offered in the dropdown.</summary>
    public sealed record Resolution(int Width, int Height)
    {
        public long PixelCount => (long)Width * Height;

        public override string ToString() => $"{Width} x {Height}";
    }
}

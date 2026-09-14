using System.Collections.ObjectModel;
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
/// Drives one tile in the multi-camera grid: one physical camera, published
/// as its own independent ONVIF device and RTSP stream (spec override, see
/// CLAUDE.md "multi-camera").
/// </summary>
/// <remarks>
/// Mirrors what <c>MainViewModel</c> used to do for the single camera it
/// drove, just scoped to one <see cref="Screen2VmsRuntime"/> and one
/// <see cref="PreviewSink"/> per tile instead of one each for the whole
/// window.
/// </remarks>
public sealed class CameraTileViewModel : ObservableObject, IDisposable
{
    private readonly IConfigurationService configuration;
    private readonly Screen2VmsRuntime runtime;
    private readonly Dispatcher dispatcher;
    private readonly ILogger logger;
    private readonly PreviewSink preview;
    private readonly Action<string, Func<CameraProfile, CameraProfile>> persistProfile;
    private readonly Action<CameraTileViewModel> requestRemove;

    private CameraProfile profile;
    private CameraDevice? device;
    private Resolution? selectedResolution;
    private double selectedFrameRate;
    private string statusMessage = "Press Start.";
    private bool rebuilding;
    private bool disposed;

    public CameraTileViewModel(
        CameraProfile profile,
        CameraDevice? device,
        IConfigurationService configuration,
        Screen2VmsRuntime runtime,
        Dispatcher dispatcher,
        ILogger logger,
        Action<string, Func<CameraProfile, CameraProfile>> persistProfile,
        Action<CameraTileViewModel> requestRemove)
    {
        this.profile = profile;
        this.device = device;
        this.configuration = configuration;
        this.runtime = runtime;
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.persistProfile = persistProfile;
        this.requestRemove = requestRemove;

        preview = new PreviewSink(dispatcher);
        preview.ImageChanged += (_, _) => OnPropertyChanged(nameof(PreviewImage));

        StartCommand = new RelayCommand(Start, () => Device is not null && !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        RemoveCommand = new RelayCommand(() => requestRemove(this), () => !IsRunning);
        CopyStreamUriCommand = new RelayCommand(CopyStreamUri, () => !string.IsNullOrEmpty(StreamUri));
        CopyOnvifUriCommand = new RelayCommand(CopyOnvifUri, () => !string.IsNullOrEmpty(OnvifUri));
        CopyPasswordCommand = new RelayCommand(CopyPassword, () => !string.IsNullOrEmpty(OnvifPassword));

        runtime.Health.Changed += OnHealthChanged;

        RebuildModeLists();
        LoadPersistedSettings();
    }

    public string Id => profile.Id;

    public ObservableCollection<Resolution> Resolutions { get; } = new();

    public ObservableCollection<double> FrameRates { get; } = new();

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand CopyStreamUriCommand { get; }

    public RelayCommand CopyOnvifUriCommand { get; }

    public RelayCommand CopyPasswordCommand { get; }

    public ImageSource? PreviewImage => preview.Image;

    public bool IsRunning => runtime.IsRunning;

    public string Name => device?.Name ?? profile.Camera.Name ?? "Unknown camera";

    public bool DeviceMissing => device is null;

    public CameraDevice? Device
    {
        get => device;
        set
        {
            if (SetProperty(ref device, value))
            {
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DeviceMissing));
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
            if (SetProperty(ref selectedResolution, value) && !rebuilding)
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

    public int RtspPort => profile.Rtsp.Port;

    public int OnvifPort => profile.Onvif.Port;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string OnvifUserName => profile.Onvif.Username;

    public string OnvifPassword { get; private set; } = string.Empty;

    public string? StreamUri => runtime.StreamUri;

    public string? OnvifUri => runtime.OnvifUri;

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

    /// <summary>Called by <c>MainViewModel</c> after a device refresh, so a re-plugged camera is picked up.</summary>
    public void UpdateDevice(CameraDevice? refreshed) => Device = refreshed;

    public void RefreshStatistics()
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtime.Health.Changed -= OnHealthChanged;
        Stop();
        preview.Dispose();
    }

    private void LoadPersistedSettings()
    {
        var stored = PasswordProtector.Unprotect(profile.Onvif.ProtectedPassword);

        if (stored is null)
        {
            stored = PasswordProtector.GeneratePassword();
            var protectedPassword = PasswordProtector.Protect(stored);

            persistProfile(Id, p => p with { Onvif = p.Onvif with { ProtectedPassword = protectedPassword } });
            profile = profile with { Onvif = profile.Onvif with { ProtectedPassword = protectedPassword } };

            logger.LogInformation("Generated a new ONVIF password for camera profile {ProfileId}.", Id);
        }

        OnvifPassword = stored;
    }

    private void RebuildModeLists()
    {
        rebuilding = true;

        try
        {
            Resolutions.Clear();

            if (device is null)
            {
                selectedResolution = null;
                StatusMessage = $"'{Name}' was not found. It may be unplugged.";
                return;
            }

            if (device.Modes.Count == 0)
            {
                StatusMessage = $"'{device.Name}' could not be opened to read its modes. It may be in use by another application.";
                selectedResolution = null;
                return;
            }

            var resolutions = device.Modes
                .Select(m => new Resolution(m.Width, m.Height))
                .Distinct()
                .OrderByDescending(r => r.PixelCount)
                .ToList();

            foreach (var resolution in resolutions)
            {
                Resolutions.Add(resolution);
            }

            selectedResolution =
                resolutions.FirstOrDefault(r => r.Width == profile.Camera.Width && r.Height == profile.Camera.Height)
                ?? resolutions.FirstOrDefault(r => r.Width == CameraSettings.DefaultWidth && r.Height == CameraSettings.DefaultHeight)
                ?? resolutions.FirstOrDefault(r => r.Width == CameraSettings.FallbackWidth && r.Height == CameraSettings.FallbackHeight)
                ?? resolutions[0];

            StatusMessage = "Press Start.";
        }
        finally
        {
            rebuilding = false;
            OnPropertyChanged(nameof(SelectedResolution));
            RebuildFrameRates();
        }
    }

    private void RebuildFrameRates()
    {
        rebuilding = true;

        try
        {
            FrameRates.Clear();

            if (device is null || SelectedResolution is null)
            {
                return;
            }

            var rates = device.Modes
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

            selectedFrameRate = rates.Contains(profile.Camera.Fps)
                ? profile.Camera.Fps
                : rates.FirstOrDefault(r => Math.Abs(r - CameraSettings.DefaultFrameRate) < 0.01, rates[0]);
        }
        finally
        {
            rebuilding = false;
            OnPropertyChanged(nameof(SelectedFrameRate));
        }
    }

    private void Start()
    {
        if (device is null)
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
            StatusMessage = $"Starting {device.Name}...";
            PersistSelection(cameraSettings);

            var runtimeConfiguration = BuildRuntimeConfiguration(cameraSettings);
            runtime.Start(runtimeConfiguration, device.Id, cameraSettings, OnvifPassword);
            runtime.StreamManager.Camera?.AddSink(preview);

            StatusMessage = runtime.OnvifUri is null
                ? "Streaming. ONVIF did not start; check the log."
                : "Streaming. The camera is discoverable on the network.";
        }
        catch (CameraBusyException ex)
        {
            StatusMessage = ex.Message;
            logger.LogWarning(ex, "Camera {Camera} is unavailable.", device.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            logger.LogError(ex, "Could not start camera profile {ProfileId}.", Id);
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

    /// <summary>
    /// Builds the single-camera <see cref="AppConfiguration"/> shape
    /// <see cref="Screen2VmsRuntime"/> expects, from this tile's profile.
    /// </summary>
    private AppConfiguration BuildRuntimeConfiguration(CameraSettings settings) => new()
    {
        Device = profile.Device,
        Camera = profile.Camera with
        {
            DeviceId = device?.Id,
            Name = device?.Name,
            Width = settings.Width,
            Height = settings.Height,
            Fps = settings.FrameRate,
        },
        Encoder = configuration.Current.Encoder,
        Rtsp = profile.Rtsp,
        Onvif = profile.Onvif,
        Discovery = configuration.Current.Discovery,
    };

    private void PersistSelection(CameraSettings settings)
    {
        if (device is null)
        {
            return;
        }

        var deviceId = device.Id;
        var deviceName = device.Name;

        persistProfile(Id, p => p with
        {
            Camera = p.Camera with
            {
                DeviceId = deviceId,
                Name = deviceName,
                Width = settings.Width,
                Height = settings.Height,
                Fps = settings.FrameRate,
            },
        });

        profile = profile with
        {
            Camera = profile.Camera with
            {
                DeviceId = deviceId,
                Name = deviceName,
                Width = settings.Width,
                Height = settings.Height,
                Fps = settings.FrameRate,
            },
        };
    }

    private void OnHealthChanged(object? sender, HealthSnapshot snapshot) =>
        dispatcher.BeginInvoke(new Action(RaiseAllStatus));

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
            logger.LogDebug(ex, "Could not write to the clipboard.");
        }
    }

    private void RaiseCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
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

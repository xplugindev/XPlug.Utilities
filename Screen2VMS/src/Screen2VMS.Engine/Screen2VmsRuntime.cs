using System.Net;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Configuration;
using Screen2VMS.Core.Diagnostics;
using Screen2VMS.Core.Encoding;
using Screen2VMS.Core.Networking;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Core.Streaming;
using Screen2VMS.Onvif;

namespace Screen2VMS.Engine;

/// <summary>
/// The whole Screen2VMS runtime: capture, encode, RTSP, ONVIF and discovery.
/// </summary>
/// <remarks>
/// Everything above this is presentation. The WPF application drives this
/// class and reads its status; a Windows service would host the same object
/// with no user interface at all (spec 58, 59).
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class Screen2VmsRuntime : IDisposable
{
    private readonly ICameraSourceService cameraService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger logger;

    private OnvifServiceHost? onvifHost;
    private StreamManagerOnvifContext? onvifContext;
    private OnvifHostOptions? onvifOptions;
    private bool disposed;

    public Screen2VmsRuntime(ICameraSourceService cameraService, ILoggerFactory? loggerFactory = null)
    {
        this.cameraService = cameraService;
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<Screen2VmsRuntime>();

        Health = new HealthMonitor();
        StreamManager = new StreamManager(cameraService, Health, this.loggerFactory);
    }

    public IHealthMonitor Health { get; }

    public IStreamManager StreamManager { get; }

    public IOnvifServiceHost? OnvifHost => onvifHost;

    public bool IsRunning => StreamManager.State == ComponentState.Running;

    /// <summary>The RTSP URI as a VMS on the LAN would see it.</summary>
    public string? StreamUri => onvifContext?.GetStreamUri(NetworkAddressResolver.GetPreferredLocalAddress());

    /// <summary>The ONVIF device service address to hand an operator.</summary>
    public string? OnvifUri => onvifHost?.IsRunning == true
        ? $"http://{NetworkAddressResolver.GetPreferredLocalAddress()}:{onvifHost.Port}/onvif/device_service"
        : null;

    /// <summary>
    /// Brings the whole device up.
    /// </summary>
    /// <param name="configuration">Persisted settings, including device identity.</param>
    /// <param name="deviceId">Camera to open.</param>
    /// <param name="cameraSettings">Requested capture mode.</param>
    /// <param name="onvifPassword">Decrypted ONVIF password.</param>
    public void Start(
        AppConfiguration configuration,
        string deviceId,
        CameraSettings cameraSettings,
        string onvifPassword)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ArgumentException.ThrowIfNullOrEmpty(onvifPassword);
        ObjectDisposedException.ThrowIf(disposed, this);

        var pipeline = new StreamPipelineSettings
        {
            DeviceId = deviceId,
            Camera = cameraSettings,
            Encoder = new EncoderSettings
            {
                Width = cameraSettings.Width,
                Height = cameraSettings.Height,
                FrameRate = cameraSettings.FrameRate,
                BitrateKbps = configuration.Encoder.BitrateKbps,
                GopLength = configuration.Encoder.Gop,
                PreferHardware = configuration.Encoder.HardwareAcceleration,
            },
            RtspPort = configuration.Rtsp.Port,
            RtspPath = configuration.Rtsp.Path,

            // The same credentials as ONVIF. A VMS authenticates to ONVIF, gets
            // a stream URI back and reuses those credentials for RTSP, which is
            // how a real camera behaves; leaving the stream anonymous while
            // protecting the ONVIF password would protect the wrong thing.
            RtspUserName = configuration.Rtsp.RequireAuthentication ? configuration.Onvif.Username : null,
            RtspPassword = configuration.Rtsp.RequireAuthentication ? onvifPassword : null,
        };

        StreamManager.Start(pipeline);

        if (configuration.Onvif.Enabled)
        {
            StartOnvif(configuration, onvifPassword);
        }
    }

    public void Stop()
    {
        StopOnvif();
        StreamManager.Stop();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
        StreamManager.Dispose();
    }

    private void StartOnvif(AppConfiguration configuration, string password)
    {
        var deviceInfo = new OnvifDeviceInfo
        {
            SerialNumber = configuration.Device.SerialNumber
                ?? throw new InvalidOperationException("The device serial number has not been generated."),
            MacAddress = configuration.Device.MacAddress
                ?? throw new InvalidOperationException("The device MAC address has not been generated."),
        };

        onvifOptions = new OnvifHostOptions
        {
            Port = configuration.Onvif.Port,
            UserName = configuration.Onvif.Username,
            Password = password,
            FallbackAddress = NetworkAddressResolver.GetPreferredLocalAddress().ToString(),
            RtspPort = configuration.Rtsp.Port,
            RtspPath = configuration.Rtsp.Path,
        };

        onvifContext = new StreamManagerOnvifContext(
            StreamManager,
            deviceInfo,
            () => onvifOptions.Port,
            loggerFactory.CreateLogger<StreamManagerOnvifContext>());

        onvifHost = new OnvifServiceHost(onvifContext, onvifOptions, loggerFactory);

        try
        {
            Health.Report(ComponentNames.Onvif, ComponentState.Starting);
            onvifHost.Start(configuration.Onvif.Port);

            Health.Report(ComponentNames.Onvif, ComponentState.Running, $"port {configuration.Onvif.Port}");
            Health.Report(
                ComponentNames.Discovery,
                configuration.Discovery.Enabled ? ComponentState.Running : ComponentState.Stopped,
                configuration.Discovery.Enabled ? $"UDP {configuration.Discovery.Port}" : null);
        }
        catch (Exception ex)
        {
            // The stream is still usable by RTSP URL even if ONVIF cannot bind,
            // so this degrades rather than tearing the pipeline down.
            logger.LogError(ex, "The ONVIF service could not be started.");
            Health.Report(ComponentNames.Onvif, ComponentState.Error, ex.Message);
            Health.Report(ComponentNames.Discovery, ComponentState.Error);

            onvifHost.Dispose();
            onvifHost = null;
        }
    }

    private void StopOnvif()
    {
        if (onvifHost is not null)
        {
            onvifHost.Dispose();
            onvifHost = null;
            Health.Report(ComponentNames.Onvif, ComponentState.Stopped);
            Health.Report(ComponentNames.Discovery, ComponentState.Stopped);
        }

        onvifContext = null;
        onvifOptions = null;
    }

    /// <summary>The stream URI a specific client would be given, for diagnostics.</summary>
    public string? GetStreamUriFor(IPAddress client) => onvifContext?.GetStreamUri(client);
}

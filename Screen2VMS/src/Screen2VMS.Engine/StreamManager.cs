using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Diagnostics;
using Screen2VMS.Core.Encoding;
using Screen2VMS.Core.Streaming;
using Screen2VMS.Encoding;
using Screen2VMS.Rtsp;

namespace Screen2VMS.Engine;

/// <summary>
/// Runs the capture, encode and stream pipeline as one unit (spec 42).
/// </summary>
/// <remarks>
/// <para>
/// The camera feeds exactly one encoder, whose output the RTSP server fans out
/// to every client (spec 33). Nothing here references WPF, so the same engine
/// can be hosted by a Windows service later (spec 58).
/// </para>
/// <para>
/// The manager also owns recovery: a camera that disappears is retried until it
/// returns, and the pipeline is rebuilt after the machine wakes from sleep
/// (spec 31, 54).
/// </para>
/// </remarks>
public sealed class StreamManager : IStreamManager
{
    /// <summary>How often a vanished camera is retried (spec 31).</summary>
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Settling time after the machine resumes, before touching the camera.
    /// Devices are commonly still re-enumerating for a second or two.
    /// </summary>
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(3);

    private readonly ICameraSourceService cameraService;
    private readonly IHealthMonitor health;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger logger;
    private readonly SharpRtspVideoServer rtspServer;
    private readonly object gate = new();

    private ICameraSource? camera;
    private MediaFoundationH264Encoder? encoder;
    private Timer? recoveryTimer;
    private ComponentState state = ComponentState.Stopped;
    private bool suspended;
    private bool disposed;

    public StreamManager(
        ICameraSourceService cameraService,
        IHealthMonitor health,
        ILoggerFactory? loggerFactory = null)
    {
        this.cameraService = cameraService;
        this.health = health;
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<StreamManager>();

        rtspServer = new SharpRtspVideoServer(this.loggerFactory);
        rtspServer.ClientConnected += OnRtspClientConnected;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler<ComponentState>? StateChanged;

    public ComponentState State => state;

    public ICameraSource? Camera => camera;

    public IVideoEncoder? Encoder => encoder;

    public IRtspServer RtspServer => rtspServer;

    public StreamPipelineSettings? Settings { get; private set; }

    public void Start(StreamPipelineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (gate)
        {
            if (state is ComponentState.Running or ComponentState.Starting)
            {
                return;
            }

            Settings = settings;
            StartCore(settings);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            StopRecoveryTimer();
            StopCore();
            SetState(ComponentState.Stopped);
        }
    }

    public void Restart()
    {
        lock (gate)
        {
            var settings = Settings;
            if (settings is null)
            {
                return;
            }

            StopCore();
            StartCore(settings);
        }
    }

    public void UpdateSettings(StreamPipelineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (gate)
        {
            Settings = settings;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        rtspServer.ClientConnected -= OnRtspClientConnected;

        Stop();
        rtspServer.Dispose();
    }

    private void StartCore(StreamPipelineSettings settings)
    {
        SetState(ComponentState.Starting);

        try
        {
            StartCamera(settings);
            StartEncoder(settings);
            StartRtsp(settings);

            SetState(ComponentState.Running);

            // Report what the pipeline negotiated, not what was requested:
            // the camera often cannot do exactly what was asked for.
            logger.LogInformation(
                "StreamStarted: {Width}x{Height} @ {Fps:0.##} fps, {Bitrate} kbps, GOP {Gop}",
                encoder!.Width,
                encoder.Height,
                encoder.FrameRate,
                encoder.BitrateKbps,
                settings.Encoder.GopLength);
        }
        catch (CameraBusyException ex)
        {
            // Recoverable: the device exists, someone else has it. Keep
            // retrying rather than making the user press Start again.
            logger.LogWarning(ex, "Camera unavailable; will keep retrying.");
            StopCore();
            health.Report(ComponentNames.Camera, ComponentState.Degraded, ex.Message);
            SetState(ComponentState.Degraded);
            StartRecoveryTimer();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start the pipeline.");
            StopCore();
            health.Report(ComponentNames.Camera, ComponentState.Error, ex.Message);
            SetState(ComponentState.Error);
            throw;
        }
    }

    private void StartCamera(StreamPipelineSettings settings)
    {
        health.Report(ComponentNames.Camera, ComponentState.Starting);

        camera = cameraService.CreateSource(settings.DeviceId);
        camera.StateChanged += OnCameraStateChanged;
        camera.Start(settings.Camera);

        health.Report(ComponentNames.Camera, ComponentState.Running, camera.ActiveMode?.ToString());
    }

    private void StartEncoder(StreamPipelineSettings settings)
    {
        health.Report(ComponentNames.Encoder, ComponentState.Starting);

        // Encode at whatever the camera actually negotiated, not what was
        // asked for: a mismatch here produces a stream whose dimensions
        // disagree with the ONVIF profile, which a VMS rejects.
        var mode = camera!.ActiveMode;
        var encoderSettings = settings.Encoder with
        {
            Width = mode?.Width ?? settings.Encoder.Width,
            Height = mode?.Height ?? settings.Encoder.Height,
            FrameRate = mode is { MaxFrameRate: > 0 } ? mode.MaxFrameRate : settings.Encoder.FrameRate,
        };

        encoder = new MediaFoundationH264Encoder(loggerFactory.CreateLogger<MediaFoundationH264Encoder>());
        encoder.FrameEncoded += OnFrameEncoded;
        encoder.Start(encoderSettings);

        camera.AddSink(encoder);

        health.Report(
            ComponentNames.Encoder,
            ComponentState.Running,
            $"{encoder.EncoderName} ({(encoder.IsHardwareAccelerated ? "hardware" : "software")})");
    }

    private void StartRtsp(StreamPipelineSettings settings)
    {
        health.Report(ComponentNames.Rtsp, ComponentState.Starting);

        rtspServer.UserName = settings.RtspUserName;
        rtspServer.Password = settings.RtspPassword;
        rtspServer.Start(settings.RtspPort, settings.RtspPath);

        // The encoder reads its parameter sets out of the negotiated output
        // type, so they are usually known before the first frame and the SDP
        // is complete from the very first DESCRIBE.
        PublishParameterSets();

        health.Report(ComponentNames.Rtsp, ComponentState.Running, $"port {settings.RtspPort}{settings.RtspPath}");
    }

    private void PublishParameterSets()
    {
        var sps = encoder?.SequenceParameterSet;
        var pps = encoder?.PictureParameterSet;

        if (sps is not null && pps is not null)
        {
            rtspServer.SetParameterSets(sps, pps);
        }
    }

    private void StopCore()
    {
        // Stop is reached from the button, from Dispose and from application
        // exit, so it has to be idempotent - and silent when there was nothing
        // left to stop, or the log fills with StreamStopped on every close.
        var stoppedAnything = encoder is not null || camera is not null || rtspServer.IsRunning;

        if (encoder is not null)
        {
            camera?.RemoveSink(encoder);
            encoder.FrameEncoded -= OnFrameEncoded;
            encoder.Dispose();
            encoder = null;
            health.Report(ComponentNames.Encoder, ComponentState.Stopped);
        }

        if (camera is not null)
        {
            camera.StateChanged -= OnCameraStateChanged;
            camera.Dispose();
            camera = null;
            health.Report(ComponentNames.Camera, ComponentState.Stopped);
        }

        if (rtspServer.IsRunning)
        {
            rtspServer.Stop();
            health.Report(ComponentNames.Rtsp, ComponentState.Stopped);
        }

        if (stoppedAnything)
        {
            logger.LogInformation("StreamStopped");
        }
    }

    private void OnFrameEncoded(object? sender, EncodedFrame frame)
    {
        // The parameter sets can arrive with the first key frame on encoders
        // that do not publish them on the output type.
        if (frame.IsKeyFrame)
        {
            PublishParameterSets();
        }

        rtspServer.PushFrame(frame);
    }

    /// <summary>
    /// Forces an IDR when a client joins so it can start decoding at once
    /// instead of waiting for the next GOP boundary.
    /// </summary>
    private void OnRtspClientConnected(object? sender, EventArgs e) => encoder?.RequestKeyFrame();

    private void OnCameraStateChanged(object? sender, CameraState cameraState)
    {
        switch (cameraState)
        {
            case CameraState.Disconnected:
            case CameraState.Error:
                logger.LogWarning("CameraDisconnected: pipeline degraded, starting recovery.");
                health.Report(ComponentNames.Camera, ComponentState.Degraded, "Camera disconnected");
                SetState(ComponentState.Degraded);
                StartRecoveryTimer();
                break;

            case CameraState.Running:
                health.Report(ComponentNames.Camera, ComponentState.Running);
                break;
        }
    }

    /// <summary>
    /// Retries the pipeline every few seconds until the camera comes back
    /// (spec 31). Runs on a timer thread, so it takes the same lock as Start.
    /// </summary>
    private void StartRecoveryTimer()
    {
        if (recoveryTimer is not null || disposed)
        {
            return;
        }

        recoveryTimer = new Timer(_ => TryRecover(), state: null, RecoveryInterval, RecoveryInterval);
    }

    private void StopRecoveryTimer()
    {
        recoveryTimer?.Dispose();
        recoveryTimer = null;
    }

    private void TryRecover()
    {
        if (disposed || suspended)
        {
            return;
        }

        if (!Monitor.TryEnter(gate))
        {
            // Start or Stop is already running; the next tick will do.
            return;
        }

        try
        {
            var settings = Settings;
            if (settings is null || state is ComponentState.Running or ComponentState.Stopped)
            {
                StopRecoveryTimer();
                return;
            }

            // The device has to be back in the enumeration before it is worth
            // trying to open it.
            var available = cameraService.EnumerateDevices()
                .Any(d => string.Equals(d.Id, settings.DeviceId, StringComparison.OrdinalIgnoreCase));

            if (!available)
            {
                return;
            }

            logger.LogInformation("CameraReconnected: rebuilding the pipeline.");
            StopCore();
            StartCore(settings);

            if (state == ComponentState.Running)
            {
                StopRecoveryTimer();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Recovery attempt failed; will retry.");
        }
        finally
        {
            Monitor.Exit(gate);
        }
    }

    /// <summary>
    /// Releases the camera before the machine sleeps and rebuilds the pipeline
    /// after it wakes (spec 54).
    /// </summary>
    /// <remarks>
    /// Holding a capture device across suspend leaves it in a state Media
    /// Foundation cannot recover from without a restart, so it is dropped
    /// deliberately and re-acquired.
    /// </remarks>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                logger.LogInformation("System suspending: releasing the camera.");
                suspended = true;

                lock (gate)
                {
                    StopCore();
                    SetState(ComponentState.Degraded);
                }

                break;

            case PowerModes.Resume:
                logger.LogInformation("System resumed: restoring the pipeline.");
                suspended = false;

                // Devices are still re-enumerating immediately after resume, so
                // recovery is left to the retry timer rather than attempted now.
                Task.Delay(ResumeDelay).ContinueWith(
                    _ =>
                    {
                        lock (gate)
                        {
                            if (Settings is not null && state != ComponentState.Stopped)
                            {
                                StartRecoveryTimer();
                            }
                        }
                    },
                    TaskScheduler.Default);

                break;
        }
    }

    private void SetState(ComponentState next)
    {
        if (state == next)
        {
            return;
        }

        state = next;
        StateChanged?.Invoke(this, next);
    }
}

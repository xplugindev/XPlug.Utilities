namespace Screen2VMS.Core.Cameras;

/// <summary>
/// A single opened camera, producing frames in <see cref="VideoPixelFormat.Nv12"/>.
/// </summary>
/// <remarks>
/// NV12 is fixed as the pipeline's currency because it is what the Media
/// Foundation H.264 encoder consumes natively (spec 10). The backend asks the
/// driver to convert; the rest of the application never sees MJPEG or YUY2.
/// </remarks>
public interface ICameraSource : IDisposable
{
    string Id { get; }

    string Name { get; }

    string? Manufacturer { get; }

    /// <summary>Modes the device advertises. Populated before <see cref="Start"/>.</summary>
    IReadOnlyList<CameraMode> Capabilities { get; }

    CameraState State { get; }

    bool IsRunning => State == CameraState.Running;

    /// <summary>The mode actually negotiated, once running. Null while stopped.</summary>
    CameraMode? ActiveMode { get; }

    CameraStatistics Statistics { get; }

    /// <summary>Raised whenever <see cref="State"/> changes, on an arbitrary thread.</summary>
    event EventHandler<CameraState>? StateChanged;

    /// <summary>
    /// Opens the device and begins capture, negotiating the closest supported
    /// mode to <paramref name="settings"/>.
    /// </summary>
    /// <exception cref="CameraBusyException">Another application owns the device.</exception>
    /// <exception cref="CameraException">The device could not be started.</exception>
    void Start(CameraSettings settings);

    /// <summary>Stops capture and releases the device. Safe to call when already stopped.</summary>
    void Stop();

    /// <summary>Registers a consumer. Sinks may be added and removed while running.</summary>
    void AddSink(IVideoFrameSink sink);

    void RemoveSink(IVideoFrameSink sink);

    /// <summary>
    /// A copy of the most recent frame, or null if none has arrived yet.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="IVideoFrameSink"/> this always copies, so it is for
    /// occasional callers only - the ONVIF snapshot endpoint, diagnostics. Do
    /// not poll it to drive the pipeline.
    /// </remarks>
    CameraFrameSnapshot? GetLatestFrame();
}

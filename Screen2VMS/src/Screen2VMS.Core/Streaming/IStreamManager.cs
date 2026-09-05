using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Diagnostics;

namespace Screen2VMS.Core.Streaming;

/// <summary>
/// Owns the camera, the encoder and the RTSP server as one unit, and is the
/// only thing that starts or stops them (spec 42).
/// </summary>
/// <remarks>
/// Nothing above this interface knows how capture reaches the wire, and nothing
/// below it knows a VMS exists.
/// </remarks>
public interface IStreamManager : IDisposable
{
    ComponentState State { get; }

    /// <summary>The running camera, or null while stopped.</summary>
    ICameraSource? Camera { get; }

    event EventHandler<ComponentState>? StateChanged;

    void Start(string deviceId, CameraSettings settings);

    void Stop();

    /// <summary>Stops and starts the pipeline, keeping the current selection.</summary>
    void Restart();
}

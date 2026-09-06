using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Diagnostics;
using Screen2VMS.Core.Encoding;

namespace Screen2VMS.Core.Streaming;

/// <summary>
/// Owns the camera, the encoder and the RTSP server as one unit, and is the
/// only thing that starts or stops them (spec 42).
/// </summary>
/// <remarks>
/// Nothing above this interface knows how capture reaches the wire, and nothing
/// below it knows a VMS exists. Keeping the pipeline here rather than in the
/// user interface is what lets the same engine run headless as a Windows
/// service later (spec 58, 59).
/// </remarks>
public interface IStreamManager : IDisposable
{
    ComponentState State { get; }

    /// <summary>The running camera, or null while stopped.</summary>
    ICameraSource? Camera { get; }

    /// <summary>The running encoder, or null while stopped.</summary>
    IVideoEncoder? Encoder { get; }

    /// <summary>The RTSP server, which stays available for its port and client count.</summary>
    IRtspServer RtspServer { get; }

    /// <summary>What the pipeline was last asked to run, or null if never started.</summary>
    StreamPipelineSettings? Settings { get; }

    event EventHandler<ComponentState>? StateChanged;

    void Start(StreamPipelineSettings settings);

    void Stop();

    /// <summary>Stops and restarts the pipeline with the settings already in force.</summary>
    void Restart();

    /// <summary>
    /// Replaces the settings used by the next start, without disturbing a
    /// running pipeline.
    /// </summary>
    /// <remarks>
    /// A VMS writes encoder settings while it is adding the unit. Restarting
    /// the stream at that moment would abort the add, so the change is stored
    /// and takes effect on the next restart.
    /// </remarks>
    void UpdateSettings(StreamPipelineSettings settings);
}

/// <summary>Everything needed to bring the pipeline up.</summary>
public sealed record StreamPipelineSettings
{
    public required string DeviceId { get; init; }

    public CameraSettings Camera { get; init; } = CameraSettings.Default;

    public EncoderSettings Encoder { get; init; } = new();

    public int RtspPort { get; init; } = 8554;

    public string RtspPath { get; init; } = "/live";

    /// <summary>RTSP credentials, or null to serve anonymously.</summary>
    public string? RtspUserName { get; init; }

    public string? RtspPassword { get; init; }
}

using System.Net;

namespace Screen2VMS.Core.Onvif;

/// <summary>
/// Everything the ONVIF services need to describe this device.
/// </summary>
/// <remarks>
/// This is the seam that keeps spec 44 honest: the ONVIF layer asks for
/// metadata and URIs and never learns that a webcam is involved.
/// </remarks>
public interface IOnvifDeviceContext
{
    OnvifDeviceInfo DeviceInfo { get; }

    /// <summary>The encoder settings currently in force, reported as the media profile.</summary>
    OnvifVideoConfiguration Video { get; }

    /// <summary>True while the pipeline is actually streaming.</summary>
    bool IsStreaming { get; }

    /// <summary>
    /// The RTSP URI reachable from <paramref name="client"/>.
    /// </summary>
    /// <remarks>
    /// Resolved per caller rather than fixed at start-up, because the right
    /// answer depends on which interface routes to that particular VMS
    /// (spec 23, 26).
    /// </remarks>
    string GetStreamUri(IPAddress client);

    /// <summary>The JPEG snapshot URI reachable from <paramref name="client"/>.</summary>
    string GetSnapshotUri(IPAddress client);

    /// <summary>
    /// A JPEG of the current picture, or null when nothing is streaming.
    /// </summary>
    /// <remarks>
    /// Both target VMS platforms fetch snapshots for thumbnails, and a camera
    /// that cannot produce one looks broken in the management client even when
    /// its RTSP stream is perfect.
    /// </remarks>
    byte[]? CaptureSnapshotJpeg();

    /// <summary>
    /// Applies encoder settings a VMS asked for.
    /// </summary>
    /// <remarks>
    /// XProtect writes resolution, frame rate and bitrate when a unit is added.
    /// Values outside what the camera supports are clamped rather than
    /// rejected: a SOAP fault here aborts the whole add.
    /// </remarks>
    void ApplyVideoConfiguration(OnvifVideoConfiguration configuration);
}

/// <summary>Identity reported by GetDeviceInformation and GetNetworkInterfaces (spec 19).</summary>
public sealed record OnvifDeviceInfo
{
    public string Manufacturer { get; init; } = "Screen2VMS";

    public string Model { get; init; } = "Virtual Camera";

    public string FirmwareVersion { get; init; } = "0.1.0";

    /// <summary>Persistent across restarts. Genetec keys the unit on this.</summary>
    public required string SerialNumber { get; init; }

    public string HardwareId { get; init; } = "Screen2VMS-VirtualCamera";

    /// <summary>Twelve hex digits. Milestone keys the hardware on this.</summary>
    public required string MacAddress { get; init; }

    /// <summary>Shown in discovery results and the VMS device list.</summary>
    public string Name { get; init; } = "Screen2VMS Virtual Camera";
}

/// <summary>The video encoder settings exposed as an ONVIF media profile.</summary>
public sealed record OnvifVideoConfiguration
{
    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public double FrameRate { get; init; } = 30;

    public int BitrateKbps { get; init; } = 4000;

    public int GopLength { get; init; } = 30;

    /// <summary>66 Baseline, 77 Main, 100 High.</summary>
    public int Profile { get; init; } = 77;
}

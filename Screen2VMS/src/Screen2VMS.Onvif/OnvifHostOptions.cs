namespace Screen2VMS.Onvif;

/// <summary>How the ONVIF endpoints are exposed (spec 17, 18, 24).</summary>
public sealed class OnvifHostOptions
{
    /// <summary>Path of the device management service.</summary>
    public const string DeviceServicePath = "/onvif/device_service";

    /// <summary>Path of the media service.</summary>
    public const string MediaServicePath = "/onvif/media_service";

    /// <summary>Path of the JPEG snapshot endpoint.</summary>
    public const string SnapshotPath = "/onvif/snapshot";

    public int Port { get; set; } = 8000;

    /// <summary>ONVIF account name (spec 24).</summary>
    public string UserName { get; set; } = "admin";

    /// <summary>
    /// ONVIF account password.
    /// </summary>
    /// <remarks>
    /// There is deliberately no default. Shipping a universal password would
    /// put the same credentials on every install, so the host refuses to start
    /// until one is supplied.
    /// </remarks>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Address used in URIs when the request carries no usable host, such as in
    /// discovery announcements made before any client has connected.
    /// </summary>
    public string FallbackAddress { get; set; } = "127.0.0.1";

    /// <summary>Optional location scope advertised in discovery.</summary>
    public string? Location { get; set; }

    /// <summary>RTSP port, needed to build the stream URI handed to a VMS.</summary>
    public int RtspPort { get; set; } = 8554;

    public string RtspPath { get; set; } = "/live";
}

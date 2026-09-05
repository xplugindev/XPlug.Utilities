namespace Screen2VMS.Core.Configuration;

/// <summary>Everything Screen2VMS persists between runs (spec 56).</summary>
public sealed record AppConfiguration
{
    public DeviceConfiguration Device { get; init; } = new();

    public CameraConfiguration Camera { get; init; } = new();

    public EncoderConfiguration Encoder { get; init; } = new();

    public RtspConfiguration Rtsp { get; init; } = new();

    public OnvifConfiguration Onvif { get; init; } = new();

    public DiscoveryConfiguration Discovery { get; init; } = new();

    public LoggingConfiguration Logging { get; init; } = new();
}

/// <summary>
/// The device's stable identity as seen by a VMS.
/// </summary>
/// <remarks>
/// Both values are generated once and never change. Genetec keys on the serial
/// number (spec 19); Milestone's ONVIF driver keys the hardware on the MAC it
/// reads back from GetNetworkInterfaces. If either moves between restarts the
/// VMS treats us as a different camera and the unit has to be re-added.
/// </remarks>
public sealed record DeviceConfiguration
{
    /// <summary>Persistent serial number reported by ONVIF GetDeviceInformation.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Synthetic MAC reported by ONVIF GetNetworkInterfaces, as 12 hex digits.</summary>
    public string? MacAddress { get; init; }
}

public sealed record CameraConfiguration
{
    /// <summary>Device id last selected by the user. Null means "first available" (spec 9).</summary>
    public string? DeviceId { get; init; }

    /// <summary>Friendly name, kept only so the UI can say which camera went missing.</summary>
    public string? Name { get; init; }

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public double Fps { get; init; } = 30;
}

public sealed record EncoderConfiguration
{
    public string Codec { get; init; } = "H264";

    public int BitrateKbps { get; init; } = 4000;

    public int Gop { get; init; } = 30;

    public bool HardwareAcceleration { get; init; } = true;
}

public sealed record RtspConfiguration
{
    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = 8554;

    public string Path { get; init; } = "/live";
}

public sealed record OnvifConfiguration
{
    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = 8000;

    public string Username { get; init; } = "admin";

    /// <summary>
    /// DPAPI-protected password blob, base64. Never the plaintext (spec 56).
    /// Populated in Phase 4 when ONVIF authentication lands.
    /// </summary>
    public string? ProtectedPassword { get; init; }
}

public sealed record DiscoveryConfiguration
{
    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = 3702;
}

public sealed record LoggingConfiguration
{
    /// <summary>Normal, Debug or Trace (spec 49). Trace writes protocol messages.</summary>
    public string Level { get; init; } = "Normal";
}

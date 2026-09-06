using Screen2VMS.Core.Onvif;

namespace Screen2VMS.Discovery;

/// <summary>
/// Builds the WS-Discovery identity a VMS uses to classify this device.
/// </summary>
/// <remarks>
/// Scopes are not decoration. Without the <c>Profile/Streaming</c> scope and
/// the <c>NetworkVideoTransmitter</c> type, Genetec and XProtect will see the
/// probe response and still refuse to list the device as a camera. This is the
/// single most common reason a home-grown ONVIF device is discovered but
/// cannot be added.
/// </remarks>
public static class OnvifDiscoveryScopes
{
    /// <summary>Device type every ONVIF camera must advertise.</summary>
    public const string NetworkVideoTransmitterName = "NetworkVideoTransmitter";

    /// <summary>Namespace the device type lives in.</summary>
    public const string NetworkVideoTransmitterNamespace = "http://www.onvif.org/ver10/network/wsdl";

    /// <summary>Marks the device as conforming to Profile S.</summary>
    public const string ProfileStreamingScope = "onvif://www.onvif.org/Profile/Streaming";

    /// <summary>Marks the device as a video encoder, not a door controller or NVR.</summary>
    public const string VideoEncoderTypeScope = "onvif://www.onvif.org/type/video_encoder";

    /// <summary>Legacy type scope some older clients still look for.</summary>
    public const string NetworkVideoTransmitterScope = "onvif://www.onvif.org/type/NetworkVideoTransmitter";

    private const string NameScopePrefix = "onvif://www.onvif.org/name/";
    private const string HardwareScopePrefix = "onvif://www.onvif.org/hardware/";
    private const string LocationScopePrefix = "onvif://www.onvif.org/location/";

    /// <summary>
    /// The full scope list for a device.
    /// </summary>
    /// <remarks>
    /// Scope values are URIs, so spaces in a device or hardware name have to be
    /// escaped or the whole scope is silently ignored by strict clients.
    /// </remarks>
    public static IReadOnlyList<string> Build(OnvifDeviceInfo device, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        var scopes = new List<string>
        {
            ProfileStreamingScope,
            VideoEncoderTypeScope,
            NetworkVideoTransmitterScope,
            NameScopePrefix + Uri.EscapeDataString(device.Name),
            HardwareScopePrefix + Uri.EscapeDataString(device.Model),
        };

        if (!string.IsNullOrWhiteSpace(location))
        {
            scopes.Add(LocationScopePrefix + Uri.EscapeDataString(location));
        }

        return scopes;
    }
}

using System.Security.Cryptography;

namespace Screen2VMS.Core.Configuration;

/// <summary>
/// Generates the identity values a VMS uses to recognise this camera.
/// </summary>
/// <remarks>
/// Both are created once and then persisted (spec 19). If either changes
/// between restarts the VMS treats Screen2VMS as a new, unknown camera and the
/// operator has to add the unit again.
/// </remarks>
public static class DeviceIdentity
{
    /// <summary>Prefix on generated serial numbers, so they are recognisable in a VMS device list.</summary>
    public const string SerialNumberPrefix = "S2V";

    /// <summary>A new persistent serial number, e.g. S2V-3F9A1C4B7E20.</summary>
    public static string NewSerialNumber() =>
        $"{SerialNumberPrefix}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";

    /// <summary>
    /// A new synthetic MAC address as 12 hex digits.
    /// </summary>
    /// <remarks>
    /// The first octet is forced to a locally administered unicast value: bit 1
    /// set marks it as locally assigned, bit 0 clear keeps it unicast. Without
    /// that a generated address could collide with a real vendor's OUI.
    /// </remarks>
    public static string NewMacAddress()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        bytes[0] = (byte)((bytes[0] | 0x02) & 0xFE);
        return Convert.ToHexString(bytes);
    }

    /// <summary>Formats a stored 12-hex-digit MAC as AA:BB:CC:DD:EE:FF for ONVIF and the UI.</summary>
    public static string FormatMacAddress(string macAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(macAddress);

        if (macAddress.Length != 12)
        {
            return macAddress;
        }

        return string.Join(':', Enumerable.Range(0, 6).Select(i => macAddress.Substring(i * 2, 2)));
    }
}

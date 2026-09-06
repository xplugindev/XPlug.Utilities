using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Screen2VMS.Core.Networking;

/// <summary>
/// Works out which local address a VMS should be told to connect to.
/// </summary>
/// <remarks>
/// Returning the wrong address is the most common reason a video unit adds
/// cleanly and then shows no video: with a VPN or several adapters, the first
/// address found is often unreachable from the VMS. Every URI handed out is
/// therefore resolved against the address of the client asking for it
/// (spec 23, 26).
/// </remarks>
public static class NetworkAddressResolver
{
    /// <summary>
    /// The local IPv4 address the operating system would use to reach
    /// <paramref name="remote"/>.
    /// </summary>
    /// <remarks>
    /// Connecting a UDP socket sets no packets in flight; it just asks the
    /// routing table which interface would be used, which is exactly the
    /// question being asked.
    /// </remarks>
    public static IPAddress? GetLocalAddressFor(IPAddress remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (IPAddress.IsLoopback(remote))
        {
            return IPAddress.Loopback;
        }

        if (remote.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(remote, 9));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// A reasonable default address to advertise when no particular client is
    /// asking, such as in discovery responses and the user interface.
    /// </summary>
    public static IPAddress GetPreferredLocalAddress()
    {
        // The address that would reach the public internet is almost always the
        // one on the real LAN, even with no internet connectivity - the route
        // exists whether or not the destination answers.
        var routed = GetLocalAddressFor(IPAddress.Parse("8.8.8.8"));
        if (routed is not null && !IPAddress.IsLoopback(routed))
        {
            return routed;
        }

        var candidate = EnumerateInterfaces().FirstOrDefault();
        return candidate?.Address ?? IPAddress.Loopback;
    }

    /// <summary>
    /// Usable IPv4 interfaces, most likely first.
    /// </summary>
    /// <remarks>
    /// Loopback and tunnel adapters are excluded, and adapters whose
    /// description marks them as virtual are pushed to the end rather than
    /// dropped - a VM host adapter is occasionally the right answer, but it is
    /// never the right default.
    /// </remarks>
    public static IReadOnlyList<NetworkInterfaceOption> EnumerateInterfaces()
    {
        var options = new List<NetworkInterfaceOption>();

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(address.Address))
                {
                    continue;
                }

                options.Add(new NetworkInterfaceOption(
                    adapter.Name,
                    adapter.Description,
                    address.Address,
                    GetMacAddress(adapter),
                    IsLikelyVirtual(adapter)));
            }
        }

        return options
            .OrderBy(o => o.IsLikelyVirtual)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The default gateway on the adapter that owns <paramref name="localAddress"/>.
    /// </summary>
    /// <remarks>
    /// A VMS asks for this while building the device's network capabilities.
    /// Returns null when the adapter has no gateway, which is a normal answer
    /// rather than an error.
    /// </remarks>
    public static IPAddress? GetDefaultGatewayFor(IPAddress localAddress)
    {
        ArgumentNullException.ThrowIfNull(localAddress);

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = adapter.GetIPProperties();

            if (!properties.UnicastAddresses.Any(a => a.Address.Equals(localAddress)))
            {
                continue;
            }

            return properties.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }

        return null;
    }

    /// <summary>The IPv4 DNS servers on the adapter that owns <paramref name="localAddress"/>.</summary>
    public static IReadOnlyList<IPAddress> GetDnsServersFor(IPAddress localAddress)
    {
        ArgumentNullException.ThrowIfNull(localAddress);

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = adapter.GetIPProperties();

            if (!properties.UnicastAddresses.Any(a => a.Address.Equals(localAddress)))
            {
                continue;
            }

            return properties.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }

        return [];
    }

    private static string? GetMacAddress(NetworkInterface adapter)
    {
        var bytes = adapter.GetPhysicalAddress().GetAddressBytes();
        return bytes.Length == 6 ? Convert.ToHexString(bytes) : null;
    }

    /// <summary>Recognises the adapters virtualisation and VPN software install.</summary>
    private static bool IsLikelyVirtual(NetworkInterface adapter)
    {
        ReadOnlySpan<string> markers =
        [
            "virtual", "vmware", "hyper-v", "vbox", "virtualbox", "loopback",
            "tap-", "tun", "vpn", "wireguard", "zerotier", "tailscale", "docker",
            "wsl", "bluetooth",
        ];

        var haystack = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();

        foreach (var marker in markers)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>One IPv4 address the application could publish itself on.</summary>
public sealed record NetworkInterfaceOption(
    string Name,
    string Description,
    IPAddress Address,
    string? MacAddress,
    bool IsLikelyVirtual)
{
    public override string ToString() => $"{Name} ({Address})";
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Screen2VMS.Core.Networking;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Discovery;
using SharpOnvifCommon;
using SharpOnvifServer;
using SharpOnvifServer.DeviceMgmt;

namespace Screen2VMS.Onvif;

/// <summary>
/// The ONVIF device management service (spec 16, 19, 20, 22).
/// </summary>
/// <remarks>
/// <para>
/// Only the operations Genetec and XProtect actually call during discovery and
/// unit creation are implemented. Everything else inherits the base class,
/// which faults with "action not supported" - the correct answer for an
/// optional operation.
/// </para>
/// <para>
/// Note that <see cref="GetSystemDateAndTime"/> is deliberately reachable
/// without authentication: it is the first call both platforms make and they
/// use it to correct for clock skew before signing anything else. Requiring
/// credentials for it makes every later request fail with a misleading
/// authentication error.
/// </para>
/// </remarks>
public class Screen2VmsDeviceService : DeviceBase
{
    private readonly IOnvifDeviceContext context;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly OnvifHostOptions options;
    private readonly ILogger<Screen2VmsDeviceService> logger;

    public Screen2VmsDeviceService(
        IOnvifDeviceContext context,
        IHttpContextAccessor httpContextAccessor,
        OnvifHostOptions options,
        ILogger<Screen2VmsDeviceService> logger)
    {
        this.context = context;
        this.httpContextAccessor = httpContextAccessor;
        this.options = options;
        this.logger = logger;
    }

    public override SystemDateTime GetSystemDateAndTime()
    {
        var utc = System.DateTime.UtcNow;
        var local = System.DateTime.Now;

        logger.LogDebug("OnvifRequest: GetSystemDateAndTime");

        return new SystemDateTime
        {
            DateTimeType = SetDateTimeType.Manual,
            DaylightSavings = TimeZoneInfo.Local.IsDaylightSavingTime(local),
            TimeZone = new SharpOnvifServer.DeviceMgmt.TimeZone
            {
                // ONVIF wants a POSIX TZ string. The offset sign is inverted
                // relative to UTC offset, which is the POSIX convention.
                TZ = FormatPosixTimeZone(TimeZoneInfo.Local.GetUtcOffset(local)),
            },
            UTCDateTime = ToOnvifDateTime(utc),
            LocalDateTime = ToOnvifDateTime(local),
        };
    }

    public override GetDeviceInformationResponse GetDeviceInformation(GetDeviceInformationRequest request)
    {
        var info = context.DeviceInfo;
        logger.LogDebug("OnvifRequest: GetDeviceInformation");

        return new GetDeviceInformationResponse
        {
            Manufacturer = info.Manufacturer,
            Model = info.Model,
            FirmwareVersion = info.FirmwareVersion,
            SerialNumber = info.SerialNumber,
            HardwareId = info.HardwareId,
        };
    }

    public override GetCapabilitiesResponse GetCapabilities(GetCapabilitiesRequest request)
    {
        var baseUri = GetServiceBaseUri();
        logger.LogDebug("OnvifRequest: GetCapabilities");

        return new GetCapabilitiesResponse
        {
            Capabilities = new Capabilities
            {
                Device = new DeviceCapabilities
                {
                    XAddr = $"{baseUri}{OnvifHostOptions.DeviceServicePath}",
                    Network = new NetworkCapabilities1
                    {
                        IPFilter = false,
                        IPFilterSpecified = true,
                        ZeroConfiguration = false,
                        ZeroConfigurationSpecified = true,
                        IPVersion6 = false,
                        IPVersion6Specified = true,
                        DynDNS = false,
                        DynDNSSpecified = true,
                    },
                    System = new SystemCapabilities1
                    {
                        DiscoveryResolve = false,
                        DiscoveryBye = true,
                        RemoteDiscovery = false,
                        SystemBackup = false,
                        SystemLogging = false,
                        FirmwareUpgrade = false,
                        SupportedVersions = [new OnvifVersion { Major = 2, Minor = 40 }],
                    },
                    Security = new SecurityCapabilities1
                    {
                        TLS11 = false,
                        TLS12 = false,
                        OnboardKeyGeneration = false,
                        AccessPolicyConfig = false,
                        X509Token = false,
                        SAMLToken = false,
                        KerberosToken = false,
                        RELToken = false,
                    },
                },
                Media = new MediaCapabilities
                {
                    XAddr = $"{baseUri}{OnvifHostOptions.MediaServicePath}",
                    StreamingCapabilities = new RealTimeStreamingCapabilities
                    {
                        // RTSP over TCP is the primary transport (spec 14) and
                        // the one both target platforms prefer.
                        RTP_RTSP_TCP = true,
                        RTP_RTSP_TCPSpecified = true,
                        RTP_TCP = true,
                        RTP_TCPSpecified = true,
                        RTPMulticast = false,
                        RTPMulticastSpecified = true,
                    },
                },

                // Events, PTZ, imaging and analytics are out of scope for the
                // MVP (spec 3, 20), so they are left unset rather than
                // advertised as present-but-broken.
            },
        };
    }

    public override GetServicesResponse GetServices(GetServicesRequest request)
    {
        var baseUri = GetServiceBaseUri();
        logger.LogDebug("OnvifRequest: GetServices");

        // Some clients read only GetCapabilities and others only GetServices,
        // so the two must describe the same endpoints.
        return new GetServicesResponse
        {
            Service =
            [
                new Service
                {
                    Namespace = OnvifServices.DEVICE_MGMT,
                    XAddr = $"{baseUri}{OnvifHostOptions.DeviceServicePath}",
                    Version = new OnvifVersion { Major = 2, Minor = 40 },
                },
                new Service
                {
                    Namespace = OnvifServices.MEDIA,
                    XAddr = $"{baseUri}{OnvifHostOptions.MediaServicePath}",
                    Version = new OnvifVersion { Major = 2, Minor = 40 },
                },
            ],
        };
    }

    public override DeviceServiceCapabilities GetServiceCapabilities() => new()
    {
        Network = new NetworkCapabilities
        {
            IPFilter = false,
            IPFilterSpecified = true,
            ZeroConfiguration = false,
            ZeroConfigurationSpecified = true,
            IPVersion6 = false,
            IPVersion6Specified = true,
            DynDNS = false,
            DynDNSSpecified = true,
        },
        Security = new SharpOnvifServer.DeviceMgmt.SecurityCapabilities
        {
            TLS12 = false,
            TLS12Specified = true,
            OnboardKeyGeneration = false,
            OnboardKeyGenerationSpecified = true,
            AccessPolicyConfig = false,
            AccessPolicyConfigSpecified = true,
            UsernameToken = true,
            UsernameTokenSpecified = true,
            HttpDigest = true,
            HttpDigestSpecified = true,
        },
        System = new SharpOnvifServer.DeviceMgmt.SystemCapabilities
        {
            DiscoveryBye = true,
            DiscoveryByeSpecified = true,
            DiscoveryResolve = false,
            DiscoveryResolveSpecified = true,
            RemoteDiscovery = false,
            RemoteDiscoverySpecified = true,
            SystemBackup = false,
            SystemBackupSpecified = true,
            SystemLogging = false,
            SystemLoggingSpecified = true,
            FirmwareUpgrade = false,
            FirmwareUpgradeSpecified = true,
        },
    };

    public override GetScopesResponse GetScopes(GetScopesRequest request)
    {
        logger.LogDebug("OnvifRequest: GetScopes");

        var scopes = OnvifDiscoveryScopes.Build(context.DeviceInfo, options.Location)
            .Select(scope => new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = scope })
            .ToArray();

        return new GetScopesResponse { Scopes = scopes };
    }

    /// <summary>
    /// Reports the device's network identity.
    /// </summary>
    /// <remarks>
    /// Milestone reads the MAC from here and uses it as the hardware key for
    /// the unit, so it must be the persisted synthetic address and must not
    /// change between restarts.
    /// </remarks>
    public override GetNetworkInterfacesResponse GetNetworkInterfaces(GetNetworkInterfacesRequest request)
    {
        var address = GetLocalAddress();
        var info = context.DeviceInfo;
        logger.LogDebug("OnvifRequest: GetNetworkInterfaces");

        return new GetNetworkInterfacesResponse
        {
            NetworkInterfaces =
            [
                new NetworkInterface
                {
                    token = "eth0",
                    Enabled = true,
                    Info = new NetworkInterfaceInfo
                    {
                        Name = "eth0",
                        HwAddress = FormatMac(info.MacAddress),
                        MTU = 1500,
                        MTUSpecified = true,
                    },
                    IPv4 = new IPv4NetworkInterface
                    {
                        Enabled = true,
                        Config = new IPv4Configuration
                        {
                            DHCP = false,
                            Manual =
                            [
                                new PrefixedIPv4Address
                                {
                                    Address = address,
                                    PrefixLength = 24,
                                },
                            ],
                        },
                    },
                },
            ],
        };
    }

    public override HostnameInformation GetHostname() => new()
    {
        FromDHCP = false,
        Name = context.DeviceInfo.Name.Replace(' ', '-'),
    };

    // --- Network settings ---------------------------------------------------
    //
    // Genetec's ONVIF driver calls this whole family from GetNetworkSettingsCaps
    // while it builds the unit's capabilities. Leaving any of them to the base
    // class produces a SOAP fault, which aborts capability discovery and leaves
    // the unit in Error - the operation is optional in ONVIF, but faulting is
    // not the same as answering "nothing configured". Every one of these
    // therefore returns a truthful, empty answer rather than an error.

    /// <summary>
    /// The default gateway for the interface Screen2VMS is reachable on.
    /// </summary>
    /// <remarks>
    /// This is the call that used to fault and stop Genetec adding the unit.
    /// </remarks>
    public override NetworkGateway GetNetworkDefaultGateway()
    {
        logger.LogDebug("OnvifRequest: GetNetworkDefaultGateway");

        var gateway = NetworkAddressResolver.GetDefaultGatewayFor(GetLocalIpAddress());

        return new NetworkGateway
        {
            IPv4Address = gateway is null ? [] : [gateway.ToString()],
        };
    }

    public override DNSInformation GetDNS()
    {
        logger.LogDebug("OnvifRequest: GetDNS");

        var servers = NetworkAddressResolver.GetDnsServersFor(GetLocalIpAddress())
            .Select(address => new SharpOnvifServer.DeviceMgmt.IPAddress
            {
                Type = IPType.IPv4,
                IPv4Address = address.ToString(),
            })
            .ToArray();

        return new DNSInformation
        {
            FromDHCP = true,
            DNSFromDHCP = servers,
            SearchDomain = [],
        };
    }

    /// <summary>
    /// Time is taken from the host clock, not NTP.
    /// </summary>
    /// <remarks>
    /// Reported as DHCP-supplied with no servers, which is the honest answer:
    /// Screen2VMS does not manage time itself.
    /// </remarks>
    public override NTPInformation GetNTP()
    {
        logger.LogDebug("OnvifRequest: GetNTP");

        return new NTPInformation
        {
            FromDHCP = true,
            NTPFromDHCP = [],
        };
    }

    public override GetNetworkProtocolsResponse GetNetworkProtocols(GetNetworkProtocolsRequest request)
    {
        logger.LogDebug("OnvifRequest: GetNetworkProtocols");

        return new GetNetworkProtocolsResponse
        {
            NetworkProtocols =
            [
                new NetworkProtocol
                {
                    Name = NetworkProtocolType.HTTP,
                    Enabled = true,
                    Port = [options.Port],
                },
                new NetworkProtocol
                {
                    Name = NetworkProtocolType.HTTPS,
                    Enabled = false,
                    Port = [443],
                },
                new NetworkProtocol
                {
                    Name = NetworkProtocolType.RTSP,
                    Enabled = true,
                    Port = [options.RtspPort],
                },
            ],
        };
    }

    /// <summary>Zero-configuration addressing is not used; the host owns the address.</summary>
    public override NetworkZeroConfiguration GetZeroConfiguration()
    {
        logger.LogDebug("OnvifRequest: GetZeroConfiguration");

        return new NetworkZeroConfiguration
        {
            InterfaceToken = "eth0",
            Enabled = false,
            Addresses = [],
        };
    }

    /// <summary>The device answers WS-Discovery probes (spec 17).</summary>
    public override DiscoveryMode GetDiscoveryMode()
    {
        logger.LogDebug("OnvifRequest: GetDiscoveryMode");
        return DiscoveryMode.Discoverable;
    }

    /// <summary>Remote (proxy) discovery is not supported.</summary>
    public override DiscoveryMode GetRemoteDiscoveryMode()
    {
        logger.LogDebug("OnvifRequest: GetRemoteDiscoveryMode");
        return DiscoveryMode.NonDiscoverable;
    }

    public override GetDPAddressesResponse GetDPAddresses(GetDPAddressesRequest request)
    {
        logger.LogDebug("OnvifRequest: GetDPAddresses");
        return new GetDPAddressesResponse { DPAddress = [] };
    }

    /// <summary>Dynamic DNS is not supported, reported as such rather than faulting.</summary>
    public override DynamicDNSInformation GetDynamicDNS()
    {
        logger.LogDebug("OnvifRequest: GetDynamicDNS");

        return new DynamicDNSInformation
        {
            Type = DynamicDNSType.NoUpdate,
        };
    }

    // --- Certificates -------------------------------------------------------
    //
    // TLS is a later milestone (spec 25, 71 v0.5), so the device holds no
    // certificates. Genetec asks anyway while enrolling and logs "an error
    // occurred while inquiring the certificate management capabilities" if the
    // request faults. An empty list is the accurate answer and keeps the
    // enrolment clean.

    public override GetCertificatesResponse GetCertificates(GetCertificatesRequest request)
    {
        logger.LogDebug("OnvifRequest: GetCertificates");
        return new GetCertificatesResponse { NvtCertificate = [] };
    }

    public override GetCACertificatesResponse GetCACertificates(GetCACertificatesRequest request)
    {
        logger.LogDebug("OnvifRequest: GetCACertificates");
        return new GetCACertificatesResponse { CACertificate = [] };
    }

    public override GetCertificatesStatusResponse GetCertificatesStatus(GetCertificatesStatusRequest request)
    {
        logger.LogDebug("OnvifRequest: GetCertificatesStatus");
        return new GetCertificatesStatusResponse { CertificateStatus = [] };
    }

    /// <summary>Client certificates are not required; the device uses password authentication.</summary>
    public override bool GetClientCertificateMode()
    {
        logger.LogDebug("OnvifRequest: GetClientCertificateMode");
        return false;
    }

    public override GetDot1XConfigurationsResponse GetDot1XConfigurations(GetDot1XConfigurationsRequest request)
    {
        logger.LogDebug("OnvifRequest: GetDot1XConfigurations");
        return new GetDot1XConfigurationsResponse { Dot1XConfiguration = [] };
    }

    /// <summary>
    /// The scheme and authority a client reached us on.
    /// </summary>
    /// <remarks>
    /// Taken from the request rather than from configuration so the addresses
    /// handed back are always ones that particular caller can reach.
    /// </remarks>
    private string GetServiceBaseUri()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is not null && request.Host.HasValue)
        {
            return $"{request.Scheme}://{request.Host.Value}";
        }

        return $"http://{options.FallbackAddress}:{options.Port}";
    }

    private string GetLocalAddress() => GetLocalIpAddress().ToString();

    /// <summary>
    /// The local address this request arrived on, as an IP address.
    /// </summary>
    /// <remarks>
    /// Falls back to the configured address when the request came over loopback
    /// or there is no HTTP context, so network answers describe the adapter a
    /// VMS would actually use.
    /// </remarks>
    private System.Net.IPAddress GetLocalIpAddress()
    {
        var local = httpContextAccessor.HttpContext?.Connection.LocalIpAddress;

        if (local is not null && !System.Net.IPAddress.IsLoopback(local))
        {
            return local.MapToIPv4();
        }

        return System.Net.IPAddress.TryParse(options.FallbackAddress, out var configured)
            ? configured
            : System.Net.IPAddress.Loopback;
    }

    private static string FormatMac(string macAddress) =>
        macAddress.Length == 12
            ? string.Join(':', Enumerable.Range(0, 6).Select(i => macAddress.Substring(i * 2, 2)))
            : macAddress;

    private static SharpOnvifServer.DeviceMgmt.DateTime ToOnvifDateTime(System.DateTime value) => new()
    {
        Date = new Date { Year = value.Year, Month = value.Month, Day = value.Day },
        Time = new Time { Hour = value.Hour, Minute = value.Minute, Second = value.Second },
    };

    /// <summary>
    /// Formats a UTC offset as a POSIX TZ string.
    /// </summary>
    /// <remarks>
    /// POSIX inverts the sign: a zone that is UTC+5:30 is written
    /// <c>UTC-05:30</c>. Getting this backwards makes some clients place the
    /// device a full day away and reject its timestamps.
    /// </remarks>
    private static string FormatPosixTimeZone(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "+" : "-";
        var absolute = offset.Duration();
        return $"UTC{sign}{absolute.Hours:00}:{absolute.Minutes:00}";
    }
}

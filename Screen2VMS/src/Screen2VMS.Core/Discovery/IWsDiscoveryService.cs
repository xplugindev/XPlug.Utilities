namespace Screen2VMS.Core.Discovery;

/// <summary>
/// Answers ONVIF WS-Discovery probes on 239.255.255.250:3702 (spec 17).
/// Implemented in Phase 4.
/// </summary>
/// <remarks>
/// Multicast does not cross subnets: a VMS on a different VLAN will never see
/// these responses and has to add the device by IP instead.
/// </remarks>
public interface IWsDiscoveryService : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Raised for every probe received, for the trace log (spec 37).</summary>
    event EventHandler<string>? ProbeReceived;

    void Start(int port);

    void Stop();
}

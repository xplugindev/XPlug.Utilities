namespace Screen2VMS.Core.Onvif;

/// <summary>
/// Hosts the ONVIF SOAP endpoints (spec 16). Implemented in Phase 4.
/// </summary>
/// <remarks>
/// The ONVIF layer never sees a webcam. It publishes the virtual camera's
/// metadata and hands out a stream URI; the RTSP server supplies the pixels
/// (spec 44).
/// </remarks>
public interface IOnvifServiceHost : IDisposable
{
    bool IsRunning { get; }

    int Port { get; }

    /// <summary>Absolute address of the device service, e.g. http://192.168.1.50:8000/onvif/device_service.</summary>
    string? DeviceServiceUri { get; }

    void Start(int port);

    void Stop();
}

/// <summary>
/// Resolves the RTSP and snapshot URIs handed back to a VMS.
/// </summary>
/// <remarks>
/// The address must be the local interface that routes to the caller. Returning
/// a VPN or virtual-adapter address is the usual reason a unit adds cleanly and
/// then shows no video (spec 23, 26).
/// </remarks>
public interface IStreamUriProvider
{
    /// <summary>The RTSP URI reachable from <paramref name="clientAddress"/>.</summary>
    string GetStreamUri(System.Net.IPAddress clientAddress);

    /// <summary>The JPEG snapshot URI reachable from <paramref name="clientAddress"/>.</summary>
    string GetSnapshotUri(System.Net.IPAddress clientAddress);
}

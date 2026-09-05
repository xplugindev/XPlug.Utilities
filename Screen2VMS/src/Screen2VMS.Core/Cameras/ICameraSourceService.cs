namespace Screen2VMS.Core.Cameras;

/// <summary>
/// Discovers capture devices and opens them. The single seam between the
/// application and whichever capture backend is in use (spec 7).
/// </summary>
public interface ICameraSourceService
{
    /// <summary>
    /// Every video capture device currently attached, with its supported modes.
    /// Enumeration is a live query - call it again to pick up hot-plugged devices.
    /// </summary>
    IReadOnlyList<CameraDevice> EnumerateDevices();

    /// <summary>
    /// The modes advertised by one device, or null if it is no longer attached.
    /// </summary>
    IReadOnlyList<CameraMode>? GetCapabilities(string deviceId);

    /// <summary>
    /// Opens a device without starting it. Call <see cref="ICameraSource.Start"/> next.
    /// </summary>
    /// <exception cref="CameraException">The device is not attached or could not be opened.</exception>
    ICameraSource CreateSource(string deviceId);
}

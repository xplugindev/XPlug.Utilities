namespace Screen2VMS.Core.Cameras;

/// <summary>
/// A video capture device discovered on this machine.
/// </summary>
/// <param name="Id">Stable per-device identifier. Survives reboots and USB re-plugs.</param>
/// <param name="Name">Friendly name as shown to the user, e.g. "Integrated Camera".</param>
/// <param name="Manufacturer">Vendor string when the driver supplies one, otherwise null.</param>
/// <param name="Modes">Every capture mode the device advertises.</param>
public sealed record CameraDevice(
    string Id,
    string Name,
    string? Manufacturer,
    IReadOnlyList<CameraMode> Modes)
{
    public override string ToString() => Name;
}

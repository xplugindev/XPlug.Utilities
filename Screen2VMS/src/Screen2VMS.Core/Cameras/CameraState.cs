namespace Screen2VMS.Core.Cameras;

/// <summary>Lifecycle of a single camera source (spec 36).</summary>
public enum CameraState
{
    Stopped = 0,
    Starting,
    Running,

    /// <summary>Device vanished mid-capture. Recovery is retrying (spec 31).</summary>
    Disconnected,

    /// <summary>Another application holds the device (spec 32).</summary>
    Busy,

    Error,
}

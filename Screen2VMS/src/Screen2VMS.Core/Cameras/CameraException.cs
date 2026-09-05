namespace Screen2VMS.Core.Cameras;

/// <summary>A camera operation failed.</summary>
public class CameraException : Exception
{
    public CameraException(string message)
        : base(message)
    {
    }

    public CameraException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The device exists but is held by another application - Teams, Zoom, a
/// browser tab (spec 32). Distinct from <see cref="CameraException"/> because
/// the UI offers a retry rather than an error.
/// </summary>
public sealed class CameraBusyException : CameraException
{
    public CameraBusyException(string message)
        : base(message)
    {
    }

    public CameraBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

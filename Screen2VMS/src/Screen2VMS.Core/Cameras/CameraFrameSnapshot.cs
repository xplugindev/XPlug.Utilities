namespace Screen2VMS.Core.Cameras;

/// <summary>
/// An owned copy of a frame, safe to hold onto. Produced by
/// <see cref="ICameraSource.GetLatestFrame"/>.
/// </summary>
public sealed record CameraFrameSnapshot(
    byte[] Data,
    int Width,
    int Height,
    int Stride,
    VideoPixelFormat Format,
    TimeSpan Timestamp);

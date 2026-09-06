namespace Screen2VMS.Core.Cameras;

/// <summary>
/// Receives frames from a running <see cref="ICameraSource"/>.
/// </summary>
/// <remarks>
/// Capture pushes rather than the pipeline pulling, because one capture has
/// several independent consumers (preview, encoder, snapshot) and the camera
/// must only ever be opened and encoded once (spec 33, 43).
/// </remarks>
public interface IVideoFrameSink
{
    /// <summary>
    /// Called on the capture thread for every frame.
    /// </summary>
    /// <remarks>
    /// Implementations must return quickly and must not throw. Blocking here
    /// stalls capture for every other sink. The frame's memory is not valid
    /// after this method returns.
    /// </remarks>
    void OnFrame(in VideoFrame frame);
}

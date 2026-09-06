namespace Screen2VMS.Core.Cameras;

/// <summary>
/// One capture mode advertised by a camera.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="MinFrameRate">Lowest frame rate this mode supports.</param>
/// <param name="MaxFrameRate">Highest frame rate this mode supports.</param>
/// <param name="Format">Pixel format the camera emits in this mode.</param>
/// <param name="NativeTypeIndex">
/// Opaque handle the capture backend uses to re-select this mode. Meaningless
/// outside the backend that produced it; never interpret it elsewhere.
/// </param>
public sealed record CameraMode(
    int Width,
    int Height,
    double MinFrameRate,
    double MaxFrameRate,
    VideoPixelFormat Format,
    int NativeTypeIndex)
{
    /// <summary>True when <paramref name="fps"/> falls inside this mode's supported range.</summary>
    public bool SupportsFrameRate(double fps) =>
        fps >= MinFrameRate - FrameRateTolerance && fps <= MaxFrameRate + FrameRateTolerance;

    /// <summary>The frame rate this mode would actually run at if asked for <paramref name="fps"/>.</summary>
    public double ClampFrameRate(double fps) => Math.Clamp(fps, MinFrameRate, MaxFrameRate);

    public long PixelCount => (long)Width * Height;

    /// <summary>Cameras report rates as ratios, so exact equality is never safe.</summary>
    internal const double FrameRateTolerance = 0.01;

    public override string ToString() =>
        MaxFrameRate - MinFrameRate < FrameRateTolerance
            ? $"{Width}x{Height} @ {MaxFrameRate:0.##} fps ({Format})"
            : $"{Width}x{Height} @ {MinFrameRate:0.##}-{MaxFrameRate:0.##} fps ({Format})";
}

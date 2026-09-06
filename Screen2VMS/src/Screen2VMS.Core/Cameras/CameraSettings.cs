namespace Screen2VMS.Core.Cameras;

/// <summary>
/// What the user asked the camera for. The backend picks the closest mode the
/// hardware actually supports (spec 9) rather than failing on an exact miss.
/// </summary>
public sealed record CameraSettings
{
    public int Width { get; init; } = DefaultWidth;

    public int Height { get; init; } = DefaultHeight;

    public double FrameRate { get; init; } = DefaultFrameRate;

    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;
    public const double DefaultFrameRate = 30;

    public const int FallbackWidth = 1280;
    public const int FallbackHeight = 720;
    public const double FallbackFrameRate = 15;

    public static CameraSettings Default { get; } = new();

    public static CameraSettings Fallback { get; } = new()
    {
        Width = FallbackWidth,
        Height = FallbackHeight,
        FrameRate = FallbackFrameRate,
    };

    public override string ToString() => $"{Width}x{Height} @ {FrameRate:0.##} fps";
}

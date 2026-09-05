namespace Screen2VMS.Core.Cameras;

/// <summary>
/// Picks the camera mode closest to what the user asked for.
/// </summary>
/// <remarks>
/// Spec 9: never fail just because 1920x1080@30 is unavailable. Pure and
/// side-effect free so the negotiation rules are unit-testable without hardware.
/// </remarks>
public static class CameraModeSelector
{
    /// <summary>
    /// The best match for <paramref name="requested"/>, or null when
    /// <paramref name="modes"/> is empty.
    /// </summary>
    public static CameraMode? Select(IReadOnlyList<CameraMode> modes, CameraSettings requested)
    {
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(requested);

        CameraMode? best = null;
        var bestScore = default(Score);

        foreach (var mode in modes)
        {
            var score = ScoreOf(mode, requested);
            if (best is null || score.CompareTo(bestScore) < 0)
            {
                best = mode;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// The frame rate <paramref name="mode"/> will actually run at for this request.
    /// </summary>
    public static double ResolveFrameRate(CameraMode mode, CameraSettings requested)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(requested);

        return mode.ClampFrameRate(requested.FrameRate);
    }

    private static Score ScoreOf(CameraMode mode, CameraSettings requested)
    {
        // Resolution dominates: a user who asked for 1080p would rather have
        // 1080p at 15 fps than 480p at 30.
        var resolutionDistance =
            Math.Abs(mode.Width - requested.Width) + Math.Abs(mode.Height - requested.Height);

        var frameRateDistance = mode.SupportsFrameRate(requested.FrameRate)
            ? 0d
            : Math.Min(
                Math.Abs(mode.MinFrameRate - requested.FrameRate),
                Math.Abs(mode.MaxFrameRate - requested.FrameRate));

        return new Score(resolutionDistance, frameRateDistance, FormatRank(mode.Format), -mode.MaxFrameRate);
    }

    /// <summary>
    /// Tie-break only. The source reader converts whatever the camera emits to
    /// NV12, so format is a mild preference for the shortest conversion path,
    /// never a reason to pick a worse resolution.
    /// </summary>
    private static int FormatRank(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Nv12 => 0,
        VideoPixelFormat.I420 => 1,
        VideoPixelFormat.Yuy2 => 2,
        VideoPixelFormat.Uyvy => 3,
        VideoPixelFormat.Mjpg => 4,
        VideoPixelFormat.Bgra32 => 5,
        VideoPixelFormat.Bgr24 => 6,
        _ => 9,
    };

    private readonly record struct Score(
        int ResolutionDistance,
        double FrameRateDistance,
        int FormatRank,
        double NegatedMaxFrameRate)
        : IComparable<Score>
    {
        public int CompareTo(Score other)
        {
            var result = ResolutionDistance.CompareTo(other.ResolutionDistance);
            if (result != 0)
            {
                return result;
            }

            result = FrameRateDistance.CompareTo(other.FrameRateDistance);
            if (result != 0)
            {
                return result;
            }

            result = FormatRank.CompareTo(other.FormatRank);
            return result != 0
                ? result
                : NegatedMaxFrameRate.CompareTo(other.NegatedMaxFrameRate);
        }
    }
}

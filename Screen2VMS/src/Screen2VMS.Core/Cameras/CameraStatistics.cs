namespace Screen2VMS.Core.Cameras;

/// <summary>Live counters for the status panel (spec 36, 63).</summary>
public sealed record CameraStatistics
{
    public long FramesCaptured { get; init; }

    public long FramesDropped { get; init; }

    /// <summary>Frame rate measured over the last sampling window, not the requested rate.</summary>
    public double MeasuredFrameRate { get; init; }

    public static CameraStatistics Empty { get; } = new();
}

namespace Screen2VMS.Core.Diagnostics;

/// <summary>
/// Aggregates the state of every component into one snapshot (spec 36, 63).
/// </summary>
public interface IHealthMonitor
{
    HealthSnapshot Current { get; }

    event EventHandler<HealthSnapshot>? Changed;

    void Report(string component, ComponentState state, string? detail = null);
}

/// <summary>A point-in-time view of the whole pipeline.</summary>
public sealed record HealthSnapshot
{
    public IReadOnlyDictionary<string, ComponentHealth> Components { get; init; } =
        new Dictionary<string, ComponentHealth>();

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public static HealthSnapshot Empty { get; } = new();
}

public sealed record ComponentHealth(string Name, ComponentState State, string? Detail);

/// <summary>Component names used across the pipeline, so the UI and logs agree.</summary>
public static class ComponentNames
{
    public const string Camera = "Camera";
    public const string Encoder = "Encoder";
    public const string Rtsp = "RTSP";
    public const string Onvif = "ONVIF";
    public const string Discovery = "Discovery";
}

namespace Screen2VMS.Core.Diagnostics;

/// <summary>
/// Thread-safe aggregation of component states for the status panel (spec 36, 63).
/// </summary>
public sealed class HealthMonitor : IHealthMonitor
{
    private readonly object gate = new();
    private readonly Dictionary<string, ComponentHealth> components = new(StringComparer.Ordinal);

    public HealthMonitor()
    {
        foreach (var name in new[]
                 {
                     ComponentNames.Camera,
                     ComponentNames.Encoder,
                     ComponentNames.Rtsp,
                     ComponentNames.Onvif,
                     ComponentNames.Discovery,
                 })
        {
            components[name] = new ComponentHealth(name, ComponentState.Stopped, Detail: null);
        }

        Current = Snapshot();
    }

    public event EventHandler<HealthSnapshot>? Changed;

    public HealthSnapshot Current { get; private set; }

    public void Report(string component, ComponentState state, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(component);

        HealthSnapshot snapshot;

        lock (gate)
        {
            if (components.TryGetValue(component, out var existing)
                && existing.State == state
                && existing.Detail == detail)
            {
                return;
            }

            components[component] = new ComponentHealth(component, state, detail);
            snapshot = Snapshot();
            Current = snapshot;
        }

        // Raised outside the lock: a handler that touches the monitor again
        // would otherwise deadlock.
        Changed?.Invoke(this, snapshot);
    }

    private HealthSnapshot Snapshot() => new()
    {
        Components = new Dictionary<string, ComponentHealth>(components, StringComparer.Ordinal),
        TimestampUtc = DateTimeOffset.UtcNow,
    };
}

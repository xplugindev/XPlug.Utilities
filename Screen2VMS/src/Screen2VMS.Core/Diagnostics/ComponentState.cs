namespace Screen2VMS.Core.Diagnostics;

/// <summary>Coarse state of one pipeline component, for the status panel (spec 36, 63).</summary>
public enum ComponentState
{
    Stopped = 0,
    Starting,
    Running,
    Degraded,
    Error,
}

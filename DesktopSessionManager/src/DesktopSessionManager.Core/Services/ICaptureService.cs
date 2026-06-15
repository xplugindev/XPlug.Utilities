using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Services;

public interface ICaptureService
{
    /// <summary>Display name used in progress reporting.</summary>
    string Name { get; }

    /// <summary>Populate relevant fields in <paramref name="state"/>.</summary>
    Task CaptureAsync(SessionState state, CancellationToken ct = default);
}

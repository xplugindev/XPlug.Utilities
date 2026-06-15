using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Repositories;

public interface ISessionRepository
{
    Task SaveAsync(SessionState session, CancellationToken ct = default);
    Task<SessionState?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task<SessionState?> LoadLatestAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}

public sealed record SessionSummary(
    string   SessionId,
    string   SessionName,
    DateTime CreatedAt,
    int      TotalItems);

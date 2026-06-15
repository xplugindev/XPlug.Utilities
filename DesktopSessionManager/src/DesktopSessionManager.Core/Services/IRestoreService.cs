using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Services;

public interface IRestoreService
{
    string Name { get; }
    Task RestoreAsync(SessionState state, CancellationToken ct = default);
}

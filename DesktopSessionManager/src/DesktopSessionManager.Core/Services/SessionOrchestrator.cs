using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Core.Services;

public sealed class SessionOrchestrator
{
    private readonly IEnumerable<ICaptureService> _captureServices;
    private readonly IEnumerable<IRestoreService> _restoreServices;
    private readonly ISessionRepository           _repository;
    private readonly ILogger<SessionOrchestrator> _logger;

    public SessionOrchestrator(
        IEnumerable<ICaptureService> captureServices,
        IEnumerable<IRestoreService> restoreServices,
        ISessionRepository           repository,
        ILogger<SessionOrchestrator> logger)
    {
        _captureServices = captureServices;
        _restoreServices = restoreServices;
        _repository      = repository;
        _logger          = logger;
    }

    public async Task<SessionState> CaptureAndSaveAsync(
        string? sessionName = null, CancellationToken ct = default)
    {
        var state = new SessionState
        {
            SessionName = sessionName ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _logger.LogInformation("Starting session capture: {Name}", state.SessionName);

        foreach (var svc in _captureServices)
        {
            try
            {
                _logger.LogDebug("Capturing: {Service}", svc.Name);
                await svc.CaptureAsync(state, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Capture service {Service} failed — skipping", svc.Name);
            }
        }

        await _repository.SaveAsync(state, ct);

        _logger.LogInformation(
            "Session saved — Browsers:{B} Folders:{F} TextFiles:{T} Apps:{A}",
            state.BrowserWindows.Count,
            state.ExplorerFolders.Count,
            state.TextFiles.Count,
            state.Applications.Count);

        return state;
    }

    public async Task RestoreAsync(string? sessionId = null, CancellationToken ct = default)
    {
        SessionState? state = sessionId is not null
            ? await _repository.LoadAsync(sessionId, ct)
            : await _repository.LoadLatestAsync(ct);

        if (state is null)
        {
            _logger.LogWarning("No session found to restore.");
            return;
        }

        _logger.LogInformation("Restoring session: {Name} (saved {When})",
            state.SessionName, state.CreatedAt);

        foreach (var svc in _restoreServices)
        {
            try
            {
                _logger.LogDebug("Restoring: {Service}", svc.Name);
                await svc.RestoreAsync(state, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Restore service {Service} failed — skipping", svc.Name);
            }
        }

        state.RestoredAt = DateTime.Now;
        await _repository.SaveAsync(state, ct);

        _logger.LogInformation("Session restore complete.");
    }
}

using System.Text.Json;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Storage;

public sealed class JsonSessionRepository : ISessionRepository
{
    private readonly string _storageDir;
    private readonly int    _maxBackups;
    private readonly ILogger<JsonSessionRepository> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonSessionRepository(
        string storagePath,
        int    maxBackups,
        ILogger<JsonSessionRepository> log)
    {
        _storageDir = Environment.ExpandEnvironmentVariables(storagePath);
        _maxBackups  = maxBackups;
        _log         = log;
        Directory.CreateDirectory(_storageDir);
    }

    // ── Save ──────────────────────────────────────────────────────────────────
    public async Task SaveAsync(SessionState session, CancellationToken ct = default)
    {
        var path = SessionPath(session.SessionId);
        var tmp  = path + ".tmp";

        var json = JsonSerializer.Serialize(session, JsonOpts);
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, path, overwrite: true);

        await SaveLatestPointerAsync(session.SessionId, ct);
        await PruneOldSessionsAsync(ct);

        _log.LogInformation("Session saved: {Id} at {Path}", session.SessionId, path);
    }

    // ── Load by ID ────────────────────────────────────────────────────────────
    public async Task<SessionState?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = SessionPath(sessionId);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
    }

    // ── Load latest ───────────────────────────────────────────────────────────
    public async Task<SessionState?> LoadLatestAsync(CancellationToken ct = default)
    {
        var pointer = LatestPointerPath();
        if (!File.Exists(pointer)) return null;

        var id = (await File.ReadAllTextAsync(pointer, ct)).Trim();
        return await LoadAsync(id, ct);
    }

    // ── List ──────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_storageDir, "session_*.json")
                             .OrderByDescending(f => f).ToArray();

        var result = new List<SessionSummary>(files.Length);

        foreach (var f in files)
        {
            try
            {
                var json  = await File.ReadAllTextAsync(f, ct);
                var state = JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
                if (state is null) continue;

                var total = state.BrowserWindows.Sum(w => w.Tabs.Count)
                          + state.ExplorerFolders.Count
                          + state.TextFiles.Count
                          + state.Applications.Count;

                result.Add(new SessionSummary(
                    state.SessionId, state.SessionName, state.CreatedAt, total));
            }
            catch { /* skip corrupt file */ }
        }

        return result;
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var path = SessionPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private string SessionPath(string id)
        => Path.Combine(_storageDir, $"session_{id}.json");

    private string LatestPointerPath()
        => Path.Combine(_storageDir, "latest.txt");

    private async Task SaveLatestPointerAsync(string id, CancellationToken ct)
        => await File.WriteAllTextAsync(LatestPointerPath(), id, ct);

    private Task PruneOldSessionsAsync(CancellationToken ct)
    {
        var files = Directory.GetFiles(_storageDir, "session_*.json")
                             .OrderByDescending(f => f)
                             .Skip(_maxBackups)
                             .ToArray();

        foreach (var f in files) File.Delete(f);
        return Task.CompletedTask;
    }
}

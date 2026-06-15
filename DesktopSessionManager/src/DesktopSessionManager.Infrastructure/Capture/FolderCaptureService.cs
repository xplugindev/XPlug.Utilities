using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.WindowsAPI;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Capture;

public sealed class FolderCaptureService : ICaptureService
{
    public string Name => "Folder Capture";
    private readonly ILogger<FolderCaptureService> _log;

    private static readonly Dictionary<string, string> SpecialFolders = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"]   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        ["Documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["Downloads"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        ["Pictures"]  = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["Music"]     = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ["Videos"]    = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
    };

    public FolderCaptureService(ILogger<FolderCaptureService> log) => _log = log;

    public Task CaptureAsync(SessionState state, CancellationToken ct = default)
    {
        var explorerProcs = Process.GetProcessesByName("explorer");
        if (explorerProcs.Length == 0) return Task.CompletedTask;

        var allWindows = WindowEnumerator.GetAll();
        var seen       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in explorerProcs)
        {
            foreach (var w in allWindows.Where(x => x.ProcessId == (uint)proc.Id))
            {
                var path = ResolveExplorerPath(w.Title);
                if (string.IsNullOrEmpty(path))    continue;
                if (!Directory.Exists(path))       continue;
                if (!seen.Add(path))               continue;

                state.ExplorerFolders.Add(new ExplorerFolder
                {
                    FolderPath  = path,
                    Rect        = w.Rect,
                    IsMaximized = w.IsMaximized
                });
            }
        }

        _log.LogDebug("Captured {N} Explorer folders", state.ExplorerFolders.Count);
        return Task.CompletedTask;
    }

    private static string ResolveExplorerPath(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Strip item-count suffix: "Documents (3 items)" → "Documents"
        var clean = global::System.Text.RegularExpressions.Regex
            .Replace(title, @"\s*\(\d+ items?\)", string.Empty).Trim();

        // Full absolute path
        if (clean.Length > 2 && clean[1] == ':') return clean;

        // Network path
        if (clean.StartsWith(@"\\")) return clean;

        // Common names
        return SpecialFolders.TryGetValue(clean, out var resolved) ? resolved : string.Empty;
    }
}

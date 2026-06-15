using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Restore;

public sealed class FolderRestoreService : IRestoreService
{
    public string Name => "Folder Restore";
    private readonly ILogger<FolderRestoreService> _log;
    private readonly int _delayMs;

    public FolderRestoreService(ILogger<FolderRestoreService> log, int delayMs = 800)
    {
        _log     = log;
        _delayMs = delayMs;
    }

    public async Task RestoreAsync(SessionState state, CancellationToken ct = default)
    {
        foreach (var folder in state.ExplorerFolders)
        {
            if (ct.IsCancellationRequested) break;

            if (!Directory.Exists(folder.FolderPath))
            {
                _log.LogWarning("Folder not found, skipping: {Path}", folder.FolderPath);
                continue;
            }

            try
            {
                Process.Start("explorer.exe", $"\"{folder.FolderPath}\"");
                _log.LogInformation("Opened folder: {Path}", folder.FolderPath);
                await Task.Delay(_delayMs, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to open folder: {Path}", folder.FolderPath);
            }
        }
    }
}

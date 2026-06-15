using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Restore;

public sealed class ApplicationRestoreService : IRestoreService
{
    public string Name => "Application Restore";
    private readonly ILogger<ApplicationRestoreService> _log;
    private readonly int _delayMs;

    public ApplicationRestoreService(ILogger<ApplicationRestoreService> log, int delayMs = 2000)
    {
        _log     = log;
        _delayMs = delayMs;
    }

    public async Task RestoreAsync(SessionState state, CancellationToken ct = default)
    {
        foreach (var app in state.Applications)
        {
            if (ct.IsCancellationRequested) break;

            if (!File.Exists(app.ExePath))
            {
                _log.LogWarning("Executable not found, skipping: {Exe}", app.ExePath);
                continue;
            }

            try
            {
                var si = new ProcessStartInfo
                {
                    FileName         = app.ExePath,
                    Arguments        = app.Arguments,
                    WorkingDirectory = Directory.Exists(app.WorkDir)
                                        ? app.WorkDir
                                        : Path.GetDirectoryName(app.ExePath) ?? string.Empty,
                    UseShellExecute  = true
                };

                Process.Start(si);
                _log.LogInformation("Started: {Name}", app.ProcessName);
                await Task.Delay(_delayMs, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to start: {Name}", app.ProcessName);
            }
        }
    }
}

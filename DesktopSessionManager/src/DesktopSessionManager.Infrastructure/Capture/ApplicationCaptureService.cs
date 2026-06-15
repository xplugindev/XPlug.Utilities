using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.WindowsAPI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Capture;

public sealed class ApplicationCaptureService : ICaptureService
{
    public string Name => "Application Capture";

    private readonly ILogger<ApplicationCaptureService> _log;
    private readonly HashSet<string> _skip;

    public ApplicationCaptureService(
        ILogger<ApplicationCaptureService> log,
        IConfiguration cfg)
    {
        _log  = log;
        _skip = new HashSet<string>(
            cfg.GetSection("SessionManager:SkipProcessNames").Get<string[]>()
            ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task CaptureAsync(SessionState state, CancellationToken ct = default)
    {
        // Names already handled by specialised capture services
        var alreadyHandled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome","msedge","firefox","opera","brave",
            "notepad","notepad++","code","devenv","sublime_text",
            "rider64","webstorm64","idea64","pycharm64",
            "explorer","cmd","powershell","WindowsTerminal"
        };

        var allWindows = WindowEnumerator.GetAll();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero)                    continue;
                if (string.IsNullOrWhiteSpace(proc.MainWindowTitle))         continue;
                if (_skip.Contains(proc.ProcessName))                        continue;
                if (alreadyHandled.Contains(proc.ProcessName))               continue;

                var exePath = ProcessHelper.GetExecutablePath(proc);
                if (string.IsNullOrEmpty(exePath))                           continue;

                var w = allWindows.FirstOrDefault(x => x.ProcessId == (uint)proc.Id);

                state.Applications.Add(new AppInstance
                {
                    ProcessName = proc.ProcessName,
                    ExePath     = exePath,
                    Arguments   = ProcessHelper.GetCommandLine(proc.Id),
                    WorkDir     = ProcessHelper.GetWorkingDirectory(proc.Id),
                    WindowTitle = proc.MainWindowTitle,
                    Rect        = w?.Rect ?? new WindowRect(),
                    IsMaximized = w?.IsMaximized ?? false,
                    IsMinimized = w?.IsMinimized ?? false
                });
            }
            catch { /* process may have exited */ }
        }

        _log.LogDebug("Captured {N} general applications", state.Applications.Count);
        return Task.CompletedTask;
    }
}

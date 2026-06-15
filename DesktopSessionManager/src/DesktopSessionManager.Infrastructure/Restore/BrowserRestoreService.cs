using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Restore;

public sealed class BrowserRestoreService : IRestoreService
{
    public string Name => "Browser Restore";
    private readonly ILogger<BrowserRestoreService> _log;
    private readonly int _delayBetweenTabsMs;

    private static readonly Dictionary<string, string[]> BrowserCommands = new()
    {
        ["chrome"]  = ["chrome",  "Google Chrome",      @"C:\Program Files\Google\Chrome\Application\chrome.exe"],
        ["msedge"]  = ["msedge",  "Microsoft Edge",     @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"],
        ["firefox"] = ["firefox", "Mozilla Firefox",    @"C:\Program Files\Mozilla Firefox\firefox.exe"],
        ["opera"]   = ["opera",   "Opera",              @"C:\Users\Public\Desktop\Opera.lnk"],
        ["brave"]   = ["brave",   "Brave Browser",      @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"]
    };

    public BrowserRestoreService(ILogger<BrowserRestoreService> log, int delayBetweenTabsMs = 300)
    {
        _log                = log;
        _delayBetweenTabsMs = delayBetweenTabsMs;
    }

    public async Task RestoreAsync(SessionState state, CancellationToken ct = default)
    {
        foreach (var win in state.BrowserWindows)
        {
            if (ct.IsCancellationRequested) break;
            if (!win.Tabs.Any()) continue;

            var tabsWithUrls = win.Tabs.Where(t => !string.IsNullOrEmpty(t.Url)).ToList();

            if (tabsWithUrls.Count > 0)
            {
                await OpenTabsWithUrls(win, tabsWithUrls, ct);
            }
            else
            {
                _log.LogWarning(
                    "{Browser}: only titles available, cannot auto-restore URLs. " +
                    "Tip: start browser with --remote-debugging-port=9222 before saving.",
                    win.DisplayName);
            }
        }
    }

    private async Task OpenTabsWithUrls(
        BrowserWindow win, List<BrowserTab> tabs, CancellationToken ct)
    {
        if (!BrowserCommands.TryGetValue(win.BrowserKey, out var cmds)) return;

        // First tab: new window
        var exe = FindExe(win.ExecutablePath, cmds[0], cmds[2]);
        if (string.IsNullOrEmpty(exe))
        {
            _log.LogWarning("Cannot find executable for {Browser}", win.DisplayName);
            return;
        }

        var first = tabs[0];
        Process.Start(new ProcessStartInfo(exe, $"--new-window \"{first.Url}\"")
            { UseShellExecute = true });

        _log.LogInformation("Opened {Browser} → {Url}", win.DisplayName, first.Url);
        await Task.Delay(2000, ct); // Wait for browser window

        // Remaining tabs
        foreach (var tab in tabs.Skip(1))
        {
            if (ct.IsCancellationRequested) break;
            Process.Start(new ProcessStartInfo(exe, $"--new-tab \"{tab.Url}\"")
                { UseShellExecute = true });
            _log.LogInformation("  + Tab → {Url}", tab.Url);
            await Task.Delay(_delayBetweenTabsMs, ct);
        }
    }

    private static string FindExe(string savedPath, string procName, string defaultPath)
    {
        if (File.Exists(savedPath))  return savedPath;
        if (File.Exists(defaultPath)) return defaultPath;

        // Try PATH
        var fromPath = FindInPath(procName + ".exe");
        return fromPath ?? string.Empty;
    }

    private static string? FindInPath(string exe)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return pathEnv.Split(Path.PathSeparator)
                      .Select(dir => Path.Combine(dir, exe))
                      .FirstOrDefault(File.Exists);
    }
}

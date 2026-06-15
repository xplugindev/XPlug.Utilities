using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.WindowsAPI;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;

namespace DesktopSessionManager.Infrastructure.Capture;

public sealed class BrowserCaptureService : ICaptureService
{
    public string Name => "Browser Capture";

    // process-name → display name
    private static readonly Dictionary<string, string> KnownBrowsers = new()
    {
        ["chrome"]  = "Google Chrome",
        ["msedge"]  = "Microsoft Edge",
        ["firefox"] = "Mozilla Firefox",
        ["opera"]   = "Opera",
        ["brave"]   = "Brave"
    };

    private readonly ILogger<BrowserCaptureService> _log;
    private readonly int _cdpPort;

    public BrowserCaptureService(ILogger<BrowserCaptureService> log, int cdpPort = 9222)
    {
        _log     = log;
        _cdpPort = cdpPort;
    }

    public async Task CaptureAsync(SessionState state, CancellationToken ct = default)
    {
        foreach (var (procName, displayName) in KnownBrowsers)
        {
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length == 0) continue;

            // Try CDP (Chrome/Edge only) for real URLs
            if (procName is "chrome" or "msedge")
            {
                var cdpTabs = await TryCaptureCdpTabsAsync(procs[0], displayName, procName, ct);
                if (cdpTabs is not null)
                {
                    state.BrowserWindows.Add(cdpTabs);
                    continue;
                }
            }

            // Fallback: enumerate visible windows and capture titles
            var exePath = ProcessHelper.GetExecutablePath(procs[0]);
            var win = new BrowserWindow
            {
                BrowserKey     = procName,
                DisplayName    = displayName,
                ExecutablePath = exePath
            };

            var allWindows = WindowEnumerator.GetAll();
            int idx = 0;
            foreach (var w in allWindows)
            {
                if (!Array.Exists(procs, p => (uint)p.Id == w.ProcessId)) continue;
                if (string.IsNullOrWhiteSpace(w.Title))                   continue;
                if (w.Title.Contains("DevTools", StringComparison.OrdinalIgnoreCase)) continue;

                win.Tabs.Add(new BrowserTab { Title = w.Title, Index = idx++ });

                if (win.Rect.Width == 0)
                {
                    win.Rect        = w.Rect;
                    win.IsMaximized = w.IsMaximized;
                }
            }

            if (win.Tabs.Count > 0)
                state.BrowserWindows.Add(win);
        }
    }

    private async Task<BrowserWindow?> TryCaptureCdpTabsAsync(
        Process proc, string displayName, string procName, CancellationToken ct)
    {
        try
        {
            var opts = new ChromeOptions();
            opts.DebuggerAddress = $"127.0.0.1:{_cdpPort}";
            opts.AddArgument("--no-sandbox");

            // Timeout guard
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var driver = await Task.Run(() => new ChromeDriver(opts), cts.Token);
            try
            {
                var win = new BrowserWindow
                {
                    BrowserKey     = procName,
                    DisplayName    = displayName,
                    ExecutablePath = ProcessHelper.GetExecutablePath(proc)
                };

                var handles = driver.WindowHandles;
                for (int i = 0; i < handles.Count; i++)
                {
                    driver.SwitchTo().Window(handles[i]);
                    win.Tabs.Add(new BrowserTab
                    {
                        Url   = driver.Url,
                        Title = driver.Title,
                        Index = i
                    });
                }

                return win.Tabs.Count > 0 ? win : null;
            }
            finally
            {
                driver.Quit();
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("CDP capture failed for {Browser}: {Msg}", displayName, ex.Message);
            return null;
        }
    }
}

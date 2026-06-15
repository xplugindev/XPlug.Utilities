using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DesktopSessionManager.Core.Services;

namespace DesktopSessionManager.Tray;

public sealed class TrayApplicationContext : IDisposable
{
    private readonly NotifyIcon         _tray;
    private readonly SessionOrchestrator _orchestrator;
    private readonly System.Timers.Timer _autoSaveTimer;
    private          bool                _disposed;

    public TrayApplicationContext(SessionOrchestrator orchestrator, int autoSaveMinutes = 15)
    {
        _orchestrator = orchestrator;

        // Build context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Save Now",         null, async (_, _) => await SaveNow());
        menu.Items.Add("Restore Last",     null, async (_, _) => await RestoreLast());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Manager...",  null, OpenManager);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",             null, Exit);

        // Tray icon
        _tray = new NotifyIcon
        {
            Text            = "Desktop Session Manager",
            Icon            = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible         = true
        };

        _tray.BalloonTipClicked += (_, _) => { };
        _tray.DoubleClick       += (_, _) => OpenManager(null, EventArgs.Empty);

        // Auto-save timer
        _autoSaveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(autoSaveMinutes));
        _autoSaveTimer.Elapsed += async (_, _) => await SaveNow(silent: true);
        _autoSaveTimer.Start();
    }

    public TrayApplicationContext() : this(null!, 15) { }

    private async Task SaveNow(bool silent = false)
    {
        try
        {
            await _orchestrator.CaptureAndSaveAsync();
            if (!silent)
                _tray.ShowBalloonTip(3000, "Session Saved",
                    "Your workspace has been saved.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "Save Failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private async Task RestoreLast()
    {
        try
        {
            await _orchestrator.RestoreAsync();
            _tray.ShowBalloonTip(3000, "Session Restored",
                "Your workspace is being restored.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "Restore Failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private static void OpenManager(object? sender, EventArgs e)
    {
        // Launch the console/WPF manager
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var mgr = Path.Combine(dir, "SessionManager.exe");
        if (File.Exists(mgr))
            System.Diagnostics.Process.Start(mgr);
    }

    private void Exit(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoSaveTimer.Dispose();
        _tray.Dispose();
    }
}

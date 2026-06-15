using System.Windows;

namespace DesktopSessionManager.Tray;

public partial class App : System.Windows.Application
{
    private TrayApplicationContext? _context;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _context = new TrayApplicationContext();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _context?.Dispose();
        base.OnExit(e);
    }
}

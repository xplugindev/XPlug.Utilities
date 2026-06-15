using System.Text;
using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Infrastructure.WindowsAPI;

public sealed class WindowInfo
{
    public IntPtr   Handle      { get; init; }
    public string   Title       { get; init; } = string.Empty;
    public uint     ProcessId   { get; init; }
    public WindowRect Rect      { get; init; } = new();
    public bool     IsMaximized { get; init; }
    public bool     IsMinimized { get; init; }
}

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> GetAll()
    {
        var list = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int len = NativeMethods.GetWindowTextLength(hWnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            NativeMethods.GetWindowRect(hWnd, out var r);

            list.Add(new WindowInfo
            {
                Handle    = hWnd,
                Title     = sb.ToString(),
                ProcessId = pid,
                Rect      = new WindowRect
                {
                    X      = r.Left,
                    Y      = r.Top,
                    Width  = r.Right  - r.Left,
                    Height = r.Bottom - r.Top
                },
                IsMaximized = NativeMethods.IsZoomed(hWnd),
                IsMinimized = NativeMethods.IsIconic(hWnd)
            });

            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static IReadOnlyList<WindowInfo> GetForProcess(uint pid)
        => GetAll().Where(w => w.ProcessId == pid).ToList();
}

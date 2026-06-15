using System.Diagnostics;
using System.Management;

namespace DesktopSessionManager.Infrastructure.WindowsAPI;

public static class ProcessHelper
{
    public static string GetExecutablePath(Process p)
    {
        try   { return p.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string GetCommandLine(int pid)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject o in s.Get())
                return o["CommandLine"]?.ToString() ?? string.Empty;
        }
        catch { /* insufficient permissions */ }
        return string.Empty;
    }

    public static string GetWorkingDirectory(int pid)
    {
        // WMI does not expose working directory; best effort via exe dir
        try
        {
            using var p = Process.GetProcessById(pid);
            var exe = GetExecutablePath(p);
            return string.IsNullOrEmpty(exe) ? string.Empty
                : Path.GetDirectoryName(exe) ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}

using Microsoft.Win32;

namespace DesktopSessionManager.Infrastructure.System;

public static class RegistryHelper
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void EnableAutoStart(string appName, string exePath, string args = "--autostart")
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Cannot open Run registry key");
        key.SetValue(appName, $"\"{exePath}\" {args}");
    }

    public static void DisableAutoStart(string appName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(appName, throwOnMissingValue: false);
    }

    public static bool IsAutoStartEnabled(string appName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(appName) is not null;
    }
}

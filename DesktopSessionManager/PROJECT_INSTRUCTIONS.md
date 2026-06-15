# Desktop Session Manager — Complete Project Instructions for Claude Code
# VERSION: 1.0 | TARGET: .NET 8.0 | PLATFORM: Windows 10/11

## INSTRUCTIONS FOR CLAUDE CODE
When you receive this file, read it completely before doing anything.
Then execute every step in order. Do not skip any step.
After each major step, run `dotnet build` to verify no errors before continuing.

---

## SECTION 1: PROJECT OVERVIEW

**Project Name**: DesktopSessionManager  
**Type**: Windows Desktop Utility (.NET 8.0, Console + WPF System Tray)  
**Purpose**: Capture and restore complete desktop session state — browser tabs,
File Explorer folders, open text files, and running applications —
so the user's workspace survives Windows Updates and unexpected reboots.  
**Primary User**: Developers/power users with complex multi-app workflows  
**Target OS**: Windows 10 (1809+) / Windows 11  
**IDE**: Visual Studio 2022 or JetBrains Rider  
**Language**: C# 12  
**Architecture**: Clean Architecture (Core → Infrastructure → UI)

---

## SECTION 2: FUNCTIONAL REQUIREMENTS

### FR-01 Session Capture
- Detect all open browser windows/tabs: Chrome, Firefox, Edge, Opera, Brave
- Capture actual URLs via Chrome DevTools Protocol (CDP) where possible
- Fall back to window titles when CDP is unavailable
- Track all File Explorer windows with full folder paths
- Detect files open in: Notepad, Notepad++, VS Code, Visual Studio 2022,
  Sublime Text, Atom, JetBrains Rider, WebStorm, IntelliJ IDEA, PyCharm
- Record window positions (X, Y, Width, Height, monitor index)
- Record text-editor cursor position (line, column) where inferable from title
- Record working directory for terminal windows (cmd, PowerShell, Windows Terminal)
- Capture process name, executable path, command-line arguments for all apps

### FR-02 Session Persistence
- Save session as JSON in %LOCALAPPDATA%\DesktopSessionManager\sessions\
- Support multiple named sessions (default name = timestamp)
- Keep last 10 sessions as rolling backup
- Atomic write (write to .tmp, then rename) to prevent corruption
- Include schema version in JSON for future migration

### FR-03 Session Restoration
- Re-open File Explorer folders in the same positions
- Re-open text files in their original editors at the same line numbers
- Re-launch applications from their original executable paths with original args
- Re-open browser tabs (URL if available, else search for title)
- Restore in correct order: folders → editors → apps → browsers
- Delay 2 s between each application launch to avoid race conditions
- Skip missing files/apps gracefully and log what was skipped

### FR-04 Auto-Start & Auto-Restore
- Register with HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
- On startup with --autostart flag: wait 10 s, then restore last session
- Detect if last shutdown was due to Windows Update (check WinEvent log)
- Save session automatically every 15 minutes while running in tray mode

### FR-05 User Interface
- Console app for manual save/restore/list operations
- System tray icon (WPF, no visible window) for background auto-save
- Right-click tray menu: Save Now, Restore Last, Open Manager, Exit
- Spectre.Console for rich terminal output (tables, progress bars)
- Windows toast notifications for save/restore events

---

## SECTION 3: NON-FUNCTIONAL REQUIREMENTS

- Session capture: complete within 5 seconds for typical workloads
- Restoration success rate: ≥90% for standard applications
- No passwords, browser cookies, or sensitive data stored
- Logs stored at %LOCALAPPDATA%\DesktopSessionManager\logs\
- Log rotation: 7-day rolling, max 50 MB per file
- Graceful degradation: if one capture service fails, others continue
- Startup detection: distinguish normal login from post-update reboot

---

## SECTION 4: COMPLETE FOLDER STRUCTURE

```
DesktopSessionManager/                          ← solution root
│
├── DesktopSessionManager.sln
│
├── src/
│   ├── DesktopSessionManager.Core/             ← no Windows deps, pure C#
│   │   ├── DesktopSessionManager.Core.csproj
│   │   ├── Models/
│   │   │   └── SessionState.cs                 ← ALL model classes in one file
│   │   ├── Services/
│   │   │   ├── ICaptureService.cs
│   │   │   ├── IRestoreService.cs
│   │   │   └── SessionOrchestrator.cs          ← main coordinator
│   │   └── Repositories/
│   │       └── ISessionRepository.cs
│   │
│   ├── DesktopSessionManager.Infrastructure/   ← Windows API, Selenium, file I/O
│   │   ├── DesktopSessionManager.Infrastructure.csproj
│   │   ├── WindowsAPI/
│   │   │   ├── NativeMethods.cs                ← all P/Invoke signatures
│   │   │   ├── WindowEnumerator.cs
│   │   │   └── ProcessHelper.cs
│   │   ├── Capture/
│   │   │   ├── BrowserCaptureService.cs
│   │   │   ├── FolderCaptureService.cs
│   │   │   ├── TextFileCaptureService.cs
│   │   │   └── ApplicationCaptureService.cs
│   │   ├── Restore/
│   │   │   ├── BrowserRestoreService.cs
│   │   │   ├── FolderRestoreService.cs
│   │   │   ├── TextFileRestoreService.cs
│   │   │   └── ApplicationRestoreService.cs
│   │   ├── Storage/
│   │   │   └── JsonSessionRepository.cs
│   │   └── System/
│   │       └── RegistryHelper.cs
│   │
│   ├── DesktopSessionManager.ConsoleApp/       ← CLI entry point
│   │   ├── DesktopSessionManager.ConsoleApp.csproj
│   │   ├── Program.cs
│   │   ├── Commands/
│   │   │   ├── SaveCommand.cs
│   │   │   ├── RestoreCommand.cs
│   │   │   └── ListCommand.cs
│   │   └── appsettings.json
│   │
│   └── DesktopSessionManager.Tray/             ← WPF system tray app
│       ├── DesktopSessionManager.Tray.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       └── TrayApplicationContext.cs
│
└── tests/
    └── DesktopSessionManager.Tests/
        ├── DesktopSessionManager.Tests.csproj
        ├── Unit/
        │   ├── FolderCaptureServiceTests.cs
        │   ├── TextFileCaptureServiceTests.cs
        │   ├── JsonSessionRepositoryTests.cs
        │   └── SessionOrchestratorTests.cs
        └── Helpers/
            └── MockWindowEnumerator.cs
```
---

## SECTION 5: PROJECT FILE CONFIGURATIONS (.csproj)

### Core.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>DesktopSessionManager.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

### Infrastructure.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <UseWindowsForms>true</UseWindowsForms>
    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>DesktopSessionManager.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Selenium.WebDriver" Version="4.18.1" />
    <PackageReference Include="Selenium.WebDriver.ChromeDriver" Version="122.0.6261.9400" />
    <PackageReference Include="Serilog" Version="3.1.1" />
    <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
    <PackageReference Include="System.Management" Version="8.0.0" />
  </ItemGroup>
</Project>
```

### ConsoleApp.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <UseWindowsForms>true</UseWindowsForms>
    <AssemblyName>SessionManager</AssemblyName>
    <RootNamespace>DesktopSessionManager.ConsoleApp</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Spectre.Console" Version="0.48.0" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="appsettings.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

### Tray.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <UseWPF>true</UseWPF>
    <AssemblyName>SessionManagerTray</AssemblyName>
    <RootNamespace>DesktopSessionManager.Tray</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="1.1.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
  </ItemGroup>
</Project>
```

### Tests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>
</Project>
```

---

## SECTION 6: appsettings.json

**Path**: `src/DesktopSessionManager.ConsoleApp/appsettings.json`

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "%LOCALAPPDATA%/DesktopSessionManager/logs/log-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "fileSizeLimitBytes": 52428800
        }
      }
    ]
  },
  "SessionManager": {
    "StoragePath": "%LOCALAPPDATA%/DesktopSessionManager/sessions",
    "MaxSessionBackups": 10,
    "AutoSaveIntervalMinutes": 15,
    "AutoRestoreDelaySeconds": 10,
    "RestoreDelayBetweenAppsMs": 2000,
    "BrowserAutomation": {
      "ChromeDebugPort": 9222,
      "TimeoutSeconds": 10
    },
    "SkipProcessNames": [
      "dwm","winlogon","csrss","wininit","services","lsass",
      "svchost","explorer","taskhost","conhost","dllhost",
      "ShellExperienceHost","SearchHost","StartMenuExperienceHost",
      "RuntimeBroker","TextInputHost","SystemSettings",
      "ApplicationFrameHost","ctfmon","sihost","taskhostw",
      "SecurityHealthSystray","OneDrive","Teams","Slack"
    ]
  }
}
```

---

## SECTION 7: COMPLETE SOURCE CODE — CORE LAYER

### File: src/DesktopSessionManager.Core/Models/SessionState.cs

```csharp
namespace DesktopSessionManager.Core.Models;

// ── Root session container ────────────────────────────────────────────────────
public sealed class SessionState
{
    public string   SessionId   { get; set; } = Guid.NewGuid().ToString("N");
    public string   SessionName { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    public int      SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt   { get; set; } = DateTime.Now;
    public DateTime? RestoredAt { get; set; }

    public List<BrowserWindow>    BrowserWindows  { get; set; } = [];
    public List<ExplorerFolder>   ExplorerFolders { get; set; } = [];
    public List<TextFileInstance> TextFiles       { get; set; } = [];
    public List<AppInstance>      Applications    { get; set; } = [];
    public List<TerminalSession>  Terminals       { get; set; } = [];
    public SystemInfo             System          { get; set; } = new();
}

// ── Browser ───────────────────────────────────────────────────────────────────
public sealed class BrowserWindow
{
    public string         BrowserKey    { get; set; } = string.Empty; // "chrome","msedge", etc.
    public string         DisplayName   { get; set; } = string.Empty;
    public string         ExecutablePath { get; set; } = string.Empty;
    public string         ProfileName   { get; set; } = "Default";
    public List<BrowserTab> Tabs        { get; set; } = [];
    public int            ActiveTabIndex { get; set; }
    public WindowRect     Rect          { get; set; } = new();
    public bool           IsMaximized   { get; set; }
}

public sealed class BrowserTab
{
    public string Url           { get; set; } = string.Empty;
    public string Title         { get; set; } = string.Empty;
    public int    Index         { get; set; }
    public bool   IsPinned      { get; set; }
    public int    ScrollY       { get; set; }
}

// ── File Explorer ─────────────────────────────────────────────────────────────
public sealed class ExplorerFolder
{
    public string       FolderPath { get; set; } = string.Empty;
    public WindowRect   Rect       { get; set; } = new();
    public bool         IsMaximized { get; set; }
}

// ── Text editors ─────────────────────────────────────────────────────────────
public sealed class TextFileInstance
{
    public string     FilePath        { get; set; } = string.Empty;
    public string     EditorKey       { get; set; } = string.Empty; // "notepad","code", etc.
    public string     EditorExe       { get; set; } = string.Empty; // full path to exe
    public string     WorkspaceFolder { get; set; } = string.Empty; // VS Code workspace
    public int        LineNumber      { get; set; } = 1;
    public int        ColumnNumber    { get; set; } = 1;
    public WindowRect Rect            { get; set; } = new();
    public bool       IsMaximized     { get; set; }
}

// ── General applications ──────────────────────────────────────────────────────
public sealed class AppInstance
{
    public string     ProcessName { get; set; } = string.Empty;
    public string     ExePath     { get; set; } = string.Empty;
    public string     Arguments   { get; set; } = string.Empty;
    public string     WorkDir     { get; set; } = string.Empty;
    public string     WindowTitle { get; set; } = string.Empty;
    public WindowRect Rect        { get; set; } = new();
    public bool       IsMaximized { get; set; }
    public bool       IsMinimized { get; set; }
}

// ── Terminals ─────────────────────────────────────────────────────────────────
public sealed class TerminalSession
{
    public string     TerminalType { get; set; } = string.Empty; // "cmd","powershell","wt"
    public string     WorkDir      { get; set; } = string.Empty;
    public string     Profile      { get; set; } = "Default";     // Windows Terminal profile
    public WindowRect Rect         { get; set; } = new();
}

// ── Shared value types ────────────────────────────────────────────────────────
public sealed class WindowRect
{
    public int X       { get; set; }
    public int Y       { get; set; }
    public int Width   { get; set; }
    public int Height  { get; set; }
    public int Monitor { get; set; }
}

public sealed class SystemInfo
{
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName    { get; set; } = Environment.UserName;
    public string OSVersion   { get; set; } = Environment.OSVersion.VersionString;
    public int    ScreenCount { get; set; }
}
```

### File: src/DesktopSessionManager.Core/Services/ICaptureService.cs

```csharp
using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Services;

public interface ICaptureService
{
    /// <summary>Display name used in progress reporting.</summary>
    string Name { get; }

    /// <summary>Populate relevant fields in <paramref name="state"/>.</summary>
    Task CaptureAsync(SessionState state, CancellationToken ct = default);
}
```

### File: src/DesktopSessionManager.Core/Services/IRestoreService.cs

```csharp
using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Services;

public interface IRestoreService
{
    string Name { get; }
    Task RestoreAsync(SessionState state, CancellationToken ct = default);
}
```

### File: src/DesktopSessionManager.Core/Repositories/ISessionRepository.cs

```csharp
using DesktopSessionManager.Core.Models;

namespace DesktopSessionManager.Core.Repositories;

public interface ISessionRepository
{
    Task SaveAsync(SessionState session, CancellationToken ct = default);
    Task<SessionState?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task<SessionState?> LoadLatestAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}

public sealed record SessionSummary(
    string   SessionId,
    string   SessionName,
    DateTime CreatedAt,
    int      TotalItems);
```

### File: src/DesktopSessionManager.Core/Services/SessionOrchestrator.cs

```csharp
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Core.Services;

public sealed class SessionOrchestrator
{
    private readonly IEnumerable<ICaptureService> _captureServices;
    private readonly IEnumerable<IRestoreService> _restoreServices;
    private readonly ISessionRepository           _repository;
    private readonly ILogger<SessionOrchestrator> _logger;

    public SessionOrchestrator(
        IEnumerable<ICaptureService> captureServices,
        IEnumerable<IRestoreService> restoreServices,
        ISessionRepository           repository,
        ILogger<SessionOrchestrator> logger)
    {
        _captureServices = captureServices;
        _restoreServices = restoreServices;
        _repository      = repository;
        _logger          = logger;
    }

    public async Task<SessionState> CaptureAndSaveAsync(
        string? sessionName = null, CancellationToken ct = default)
    {
        var state = new SessionState
        {
            SessionName = sessionName ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _logger.LogInformation("Starting session capture: {Name}", state.SessionName);

        foreach (var svc in _captureServices)
        {
            try
            {
                _logger.LogDebug("Capturing: {Service}", svc.Name);
                await svc.CaptureAsync(state, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Capture service {Service} failed — skipping", svc.Name);
            }
        }

        await _repository.SaveAsync(state, ct);

        _logger.LogInformation(
            "Session saved — Browsers:{B} Folders:{F} TextFiles:{T} Apps:{A}",
            state.BrowserWindows.Count,
            state.ExplorerFolders.Count,
            state.TextFiles.Count,
            state.Applications.Count);

        return state;
    }

    public async Task RestoreAsync(string? sessionId = null, CancellationToken ct = default)
    {
        SessionState? state = sessionId is not null
            ? await _repository.LoadAsync(sessionId, ct)
            : await _repository.LoadLatestAsync(ct);

        if (state is null)
        {
            _logger.LogWarning("No session found to restore.");
            return;
        }

        _logger.LogInformation("Restoring session: {Name} (saved {When})",
            state.SessionName, state.CreatedAt);

        foreach (var svc in _restoreServices)
        {
            try
            {
                _logger.LogDebug("Restoring: {Service}", svc.Name);
                await svc.RestoreAsync(state, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Restore service {Service} failed — skipping", svc.Name);
            }
        }

        state.RestoredAt = DateTime.Now;
        await _repository.SaveAsync(state, ct);

        _logger.LogInformation("Session restore complete.");
    }
}
```

---

## SECTION 8: INFRASTRUCTURE — WINDOWS API

### File: src/DesktopSessionManager.Infrastructure/WindowsAPI/NativeMethods.cs

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSessionManager.Infrastructure.WindowsAPI;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int  GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int  GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }
}
```

### File: src/DesktopSessionManager.Infrastructure/WindowsAPI/WindowEnumerator.cs

```csharp
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
```

### File: src/DesktopSessionManager.Infrastructure/WindowsAPI/ProcessHelper.cs

```csharp
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
```

### File: src/DesktopSessionManager.Infrastructure/System/RegistryHelper.cs

```csharp
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
```

---

## SECTION 9: INFRASTRUCTURE — CAPTURE SERVICES

### File: src/DesktopSessionManager.Infrastructure/Capture/BrowserCaptureService.cs

```csharp
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
```

### File: src/DesktopSessionManager.Infrastructure/Capture/FolderCaptureService.cs

```csharp
using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.WindowsAPI;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Capture;

public sealed class FolderCaptureService : ICaptureService
{
    public string Name => "Folder Capture";
    private readonly ILogger<FolderCaptureService> _log;

    private static readonly Dictionary<string, string> SpecialFolders = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"]   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        ["Documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["Downloads"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        ["Pictures"]  = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        ["Music"]     = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        ["Videos"]    = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
    };

    public FolderCaptureService(ILogger<FolderCaptureService> log) => _log = log;

    public Task CaptureAsync(SessionState state, CancellationToken ct = default)
    {
        var explorerProcs = Process.GetProcessesByName("explorer");
        if (explorerProcs.Length == 0) return Task.CompletedTask;

        var allWindows = WindowEnumerator.GetAll();
        var seen       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in explorerProcs)
        {
            foreach (var w in allWindows.Where(x => x.ProcessId == (uint)proc.Id))
            {
                var path = ResolveExplorerPath(w.Title);
                if (string.IsNullOrEmpty(path))    continue;
                if (!Directory.Exists(path))       continue;
                if (!seen.Add(path))               continue;

                state.ExplorerFolders.Add(new ExplorerFolder
                {
                    FolderPath  = path,
                    Rect        = w.Rect,
                    IsMaximized = w.IsMaximized
                });
            }
        }

        _log.LogDebug("Captured {N} Explorer folders", state.ExplorerFolders.Count);
        return Task.CompletedTask;
    }

    private static string ResolveExplorerPath(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Strip item-count suffix: "Documents (3 items)" → "Documents"
        var clean = System.Text.RegularExpressions.Regex
            .Replace(title, @"\s*\(\d+ items?\)", string.Empty).Trim();

        // Full absolute path
        if (clean.Length > 2 && clean[1] == ':') return clean;

        // Network path
        if (clean.StartsWith(@"\\")) return clean;

        // Common names
        return SpecialFolders.TryGetValue(clean, out var resolved) ? resolved : string.Empty;
    }
}
```

### File: src/DesktopSessionManager.Infrastructure/Capture/TextFileCaptureService.cs

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.WindowsAPI;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Capture;

public sealed class TextFileCaptureService : ICaptureService
{
    public string Name => "Text File Capture";
    private readonly ILogger<TextFileCaptureService> _log;

    private sealed record EditorDef(
        string Key, string DisplayName, Func<string, (string file, string workspace)> Parser);

    private static readonly EditorDef[] Editors =
    [
        new("notepad",      "Notepad",
            t => (StripSuffix(t, " - Notepad"), string.Empty)),

        new("notepad++",    "Notepad++",
            t => (StripSuffix(StripDirty(t), " - Notepad++"), string.Empty)),

        new("code",         "Visual Studio Code",
            t => ParseVsCode(t)),

        new("devenv",       "Visual Studio 2022",
            t => (ExtractFileFromVS(t), string.Empty)),

        new("sublime_text", "Sublime Text",
            t => ParseSublime(t)),

        new("rider64",      "JetBrains Rider",
            t => (string.Empty, ExtractProjectName(t))),

        new("webstorm64",   "WebStorm",
            t => ParseJetBrains(t)),

        new("idea64",       "IntelliJ IDEA",
            t => ParseJetBrains(t)),

        new("pycharm64",    "PyCharm",
            t => ParseJetBrains(t))
    ];

    public TextFileCaptureService(ILogger<TextFileCaptureService> log) => _log = log;

    public Task CaptureAsync(SessionState state, CancellationToken ct = default)
    {
        var allWindows = WindowEnumerator.GetAll();

        foreach (var ed in Editors)
        {
            var procs = Process.GetProcessesByName(ed.Key);
            foreach (var proc in procs)
            {
                var exePath = ProcessHelper.GetExecutablePath(proc);
                foreach (var w in allWindows.Where(x => x.ProcessId == (uint)proc.Id))
                {
                    try
                    {
                        var (filePath, workspace) = ed.Parser(w.Title);
                        if (string.IsNullOrWhiteSpace(filePath) &&
                            string.IsNullOrWhiteSpace(workspace)) continue;

                        state.TextFiles.Add(new TextFileInstance
                        {
                            FilePath        = filePath,
                            EditorKey       = ed.Key,
                            EditorExe       = exePath,
                            WorkspaceFolder = workspace,
                            Rect            = w.Rect,
                            IsMaximized     = w.IsMaximized
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Title parse failed for {Editor}", ed.Key);
                    }
                }
            }
        }

        _log.LogDebug("Captured {N} text-file instances", state.TextFiles.Count);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string StripSuffix(string s, string suffix)
        => s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? s[..^suffix.Length].Trim()
            : s.Trim();

    private static string StripDirty(string s)
        => s.TrimStart('*', '●', ' ');

    private static (string, string) ParseVsCode(string title)
    {
        // "filename.ext — WorkspaceFolder — Visual Studio Code"
        var parts = title.Split(" — ");
        if (parts.Length >= 3)
        {
            var file      = StripDirty(parts[0]).Trim();
            var workspace = parts[1].Trim();
            var full      = Path.Combine(workspace, file);
            return (File.Exists(full) ? full : file, workspace);
        }
        if (parts.Length == 2)
            return (StripDirty(parts[0]).Trim(), string.Empty);

        // Older VS Code uses " - " separator
        var p2 = title.Split(" - ");
        if (p2.Length >= 3)
        {
            var file      = StripDirty(p2[0]).Trim();
            var workspace = p2[1].Trim();
            var full      = Path.Combine(workspace, file);
            return (File.Exists(full) ? full : file, workspace);
        }
        return (string.Empty, string.Empty);
    }

    private static string ExtractFileFromVS(string title)
    {
        // "filename.cs (readonly) - ProjectName - Microsoft Visual Studio"
        var parts = title.Split(" - ");
        return parts.Length >= 1 ? StripDirty(parts[0]).Trim() : string.Empty;
    }

    private static (string, string) ParseSublime(string title)
    {
        var m = Regex.Match(title, @"^(.*?)\s*[—\-]\s*(.+?)\s*[—\-]\s*Sublime Text");
        return m.Success
            ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
            : (string.Empty, string.Empty);
    }

    private static (string, string) ParseJetBrains(string title)
    {
        var parts = title.Split(" – ");
        return parts.Length >= 2
            ? (parts[0].Trim(), parts[1].Trim())
            : (string.Empty, string.Empty);
    }

    private static string ExtractProjectName(string title)
    {
        var parts = title.Split(" – ");
        return parts.Length >= 1 ? parts[0].Trim() : string.Empty;
    }
}
```

### File: src/DesktopSessionManager.Infrastructure/Capture/ApplicationCaptureService.cs

```csharp
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
```

---

## SECTION 10: INFRASTRUCTURE — RESTORE SERVICES

### File: src/DesktopSessionManager.Infrastructure/Restore/FolderRestoreService.cs

```csharp
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
```

### File: src/DesktopSessionManager.Infrastructure/Restore/TextFileRestoreService.cs

```csharp
using System.Diagnostics;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Services;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Restore;

public sealed class TextFileRestoreService : IRestoreService
{
    public string Name => "Text File Restore";
    private readonly ILogger<TextFileRestoreService> _log;
    private readonly int _delayMs;

    public TextFileRestoreService(ILogger<TextFileRestoreService> log, int delayMs = 500)
    {
        _log     = log;
        _delayMs = delayMs;
    }

    public async Task RestoreAsync(SessionState state, CancellationToken ct = default)
    {
        foreach (var tf in state.TextFiles)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (!string.IsNullOrEmpty(tf.FilePath) && !File.Exists(tf.FilePath))
                {
                    _log.LogWarning("File not found, skipping: {Path}", tf.FilePath);
                    continue;
                }

                LaunchEditor(tf);
                _log.LogInformation("Opened {File} in {Editor}", tf.FilePath, tf.EditorKey);
                await Task.Delay(_delayMs, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to open file {File}", tf.FilePath);
            }
        }
    }

    private static void LaunchEditor(TextFileInstance tf)
    {
        var exe = tf.EditorExe;
        if (string.IsNullOrEmpty(exe))
            exe = FindEditorExe(tf.EditorKey);

        switch (tf.EditorKey)
        {
            case "notepad":
                Process.Start("notepad.exe",
                    string.IsNullOrEmpty(tf.FilePath) ? "" : $"\"{tf.FilePath}\"");
                break;

            case "notepad++":
                // -n<line> sets cursor line on open
                var nppArgs = string.IsNullOrEmpty(tf.FilePath) ? string.Empty
                    : $"-n{tf.LineNumber} \"{tf.FilePath}\"";
                StartProcess(exe.Length > 0 ? exe : "notepad++.exe", nppArgs);
                break;

            case "code":
                // vs code: code --goto "file:line:col"
                if (!string.IsNullOrEmpty(tf.FilePath))
                    StartProcess("code", $"--goto \"{tf.FilePath}:{tf.LineNumber}:{tf.ColumnNumber}\"");
                else if (!string.IsNullOrEmpty(tf.WorkspaceFolder))
                    StartProcess("code", $"\"{tf.WorkspaceFolder}\"");
                break;

            case "devenv":
                if (!string.IsNullOrEmpty(tf.FilePath))
                    StartProcess(exe, $"\"{tf.FilePath}\"");
                break;

            case "sublime_text":
                if (!string.IsNullOrEmpty(tf.FilePath))
                    StartProcess(exe.Length > 0 ? exe : "subl", $"\"{tf.FilePath}\"");
                else if (!string.IsNullOrEmpty(tf.WorkspaceFolder))
                    StartProcess(exe.Length > 0 ? exe : "subl", $"\"{tf.WorkspaceFolder}\"");
                break;

            default:
                if (!string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(tf.FilePath))
                    StartProcess(exe, $"\"{tf.FilePath}\"");
                break;
        }
    }

    private static void StartProcess(string exe, string args)
        => Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });

    private static string FindEditorExe(string key) => key switch
    {
        "notepad++"    => @"C:\Program Files\Notepad++\notepad++.exe",
        "sublime_text" => @"C:\Program Files\Sublime Text\sublime_text.exe",
        "rider64"      => FindJetBrains("Rider"),
        "webstorm64"   => FindJetBrains("WebStorm"),
        _              => string.Empty
    };

    private static string FindJetBrains(string product)
    {
        var jbRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JetBrains", "Toolbox", "apps");

        if (!Directory.Exists(jbRoot)) return string.Empty;

        return Directory.GetFiles(jbRoot, $"{product}*.exe", SearchOption.AllDirectories)
                        .OrderByDescending(f => f).FirstOrDefault() ?? string.Empty;
    }
}
```

### File: src/DesktopSessionManager.Infrastructure/Restore/ApplicationRestoreService.cs

```csharp
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
```

### File: src/DesktopSessionManager.Infrastructure/Restore/BrowserRestoreService.cs

```csharp
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
```

---

## SECTION 11: INFRASTRUCTURE — STORAGE

### File: src/DesktopSessionManager.Infrastructure/Storage/JsonSessionRepository.cs

```csharp
using System.Text.Json;
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace DesktopSessionManager.Infrastructure.Storage;

public sealed class JsonSessionRepository : ISessionRepository
{
    private readonly string _storageDir;
    private readonly int    _maxBackups;
    private readonly ILogger<JsonSessionRepository> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonSessionRepository(
        string storagePath,
        int    maxBackups,
        ILogger<JsonSessionRepository> log)
    {
        _storageDir = Environment.ExpandEnvironmentVariables(storagePath);
        _maxBackups  = maxBackups;
        _log         = log;
        Directory.CreateDirectory(_storageDir);
    }

    // ── Save ──────────────────────────────────────────────────────────────────
    public async Task SaveAsync(SessionState session, CancellationToken ct = default)
    {
        var path = SessionPath(session.SessionId);
        var tmp  = path + ".tmp";

        var json = JsonSerializer.Serialize(session, JsonOpts);
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, path, overwrite: true);

        await SaveLatestPointerAsync(session.SessionId, ct);
        await PruneOldSessionsAsync(ct);

        _log.LogInformation("Session saved: {Id} at {Path}", session.SessionId, path);
    }

    // ── Load by ID ────────────────────────────────────────────────────────────
    public async Task<SessionState?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = SessionPath(sessionId);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
    }

    // ── Load latest ───────────────────────────────────────────────────────────
    public async Task<SessionState?> LoadLatestAsync(CancellationToken ct = default)
    {
        var pointer = LatestPointerPath();
        if (!File.Exists(pointer)) return null;

        var id = (await File.ReadAllTextAsync(pointer, ct)).Trim();
        return await LoadAsync(id, ct);
    }

    // ── List ──────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_storageDir, "session_*.json")
                             .OrderByDescending(f => f).ToArray();

        var result = new List<SessionSummary>(files.Length);

        foreach (var f in files)
        {
            try
            {
                var json  = await File.ReadAllTextAsync(f, ct);
                var state = JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
                if (state is null) continue;

                var total = state.BrowserWindows.Sum(w => w.Tabs.Count)
                          + state.ExplorerFolders.Count
                          + state.TextFiles.Count
                          + state.Applications.Count;

                result.Add(new SessionSummary(
                    state.SessionId, state.SessionName, state.CreatedAt, total));
            }
            catch { /* skip corrupt file */ }
        }

        return result;
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var path = SessionPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private string SessionPath(string id)
        => Path.Combine(_storageDir, $"session_{id}.json");

    private string LatestPointerPath()
        => Path.Combine(_storageDir, "latest.txt");

    private async Task SaveLatestPointerAsync(string id, CancellationToken ct)
        => await File.WriteAllTextAsync(LatestPointerPath(), id, ct);

    private Task PruneOldSessionsAsync(CancellationToken ct)
    {
        var files = Directory.GetFiles(_storageDir, "session_*.json")
                             .OrderByDescending(f => f)
                             .Skip(_maxBackups)
                             .ToArray();

        foreach (var f in files) File.Delete(f);
        return Task.CompletedTask;
    }
}
```

---

## SECTION 12: CONSOLE APPLICATION

### File: src/DesktopSessionManager.ConsoleApp/Program.cs

```csharp
using DesktopSessionManager.Core.Repositories;
using DesktopSessionManager.Core.Services;
using DesktopSessionManager.Infrastructure.Capture;
using DesktopSessionManager.Infrastructure.Restore;
using DesktopSessionManager.Infrastructure.Storage;
using DesktopSessionManager.Infrastructure.System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;
using System.Reflection;

// ── Bootstrap ─────────────────────────────────────────────────────────────────
var cfg = new ConfigurationBuilder()
    .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(cfg)
    .CreateLogger();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSerilog(dispose: true));
services.AddSingleton<IConfiguration>(cfg);

// Storage
var storagePath  = cfg["SessionManager:StoragePath"] ?? "%LOCALAPPDATA%/DesktopSessionManager/sessions";
var maxBackups   = int.Parse(cfg["SessionManager:MaxSessionBackups"] ?? "10");
services.AddSingleton<ISessionRepository>(sp =>
    new JsonSessionRepository(storagePath, maxBackups,
        sp.GetRequiredService<ILogger<JsonSessionRepository>>()));

// Capture services (registration order = execution order)
services.AddSingleton<ICaptureService, FolderCaptureService>();
services.AddSingleton<ICaptureService, TextFileCaptureService>();
services.AddSingleton<ICaptureService, ApplicationCaptureService>();
services.AddSingleton<ICaptureService, BrowserCaptureService>();

// Restore services (registration order = execution order)
services.AddSingleton<IRestoreService, FolderRestoreService>();
services.AddSingleton<IRestoreService, TextFileRestoreService>();
services.AddSingleton<IRestoreService, ApplicationRestoreService>();
services.AddSingleton<IRestoreService, BrowserRestoreService>();

services.AddSingleton<SessionOrchestrator>();

var sp = services.BuildServiceProvider();
var orchestrator = sp.GetRequiredService<SessionOrchestrator>();
var repo         = sp.GetRequiredService<ISessionRepository>();

// ── Auto-restore mode (called by Windows startup registry entry) ───────────────
if (args.Contains("--autostart"))
{
    AnsiConsole.MarkupLine("[yellow]Auto-restore mode: waiting 10 s for desktop to settle...[/]");
    await Task.Delay(TimeSpan.FromSeconds(10));
    await orchestrator.RestoreAsync();
    return 0;
}

// ── Interactive CLI ───────────────────────────────────────────────────────────
AnsiConsole.Write(new FigletText("Session Mgr").Color(Color.Cyan1));
AnsiConsole.MarkupLine("[grey]Desktop Session Manager — v1.0[/]\n");

while (true)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]What do you want to do?[/]")
            .AddChoices(
                "Save session",
                "Restore last session",
                "Restore named session",
                "List sessions",
                "Delete a session",
                "Enable auto-start on login",
                "Disable auto-start on login",
                "Exit"));

    switch (choice)
    {
        case "Save session":
            var name = AnsiConsole.Ask<string>(
                "Session name [grey](Enter for timestamp)[/]:", string.Empty);
            
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Capturing session...");
                    var state = await orchestrator.CaptureAndSaveAsync(
                        string.IsNullOrWhiteSpace(name) ? null : name);
                    task.Increment(100);

                    var table = new Table().AddColumn("Category").AddColumn("Count");
                    table.AddRow("Browser tabs",  state.BrowserWindows.Sum(w => w.Tabs.Count).ToString());
                    table.AddRow("Folders",        state.ExplorerFolders.Count.ToString());
                    table.AddRow("Text files",     state.TextFiles.Count.ToString());
                    table.AddRow("Applications",   state.Applications.Count.ToString());
                    AnsiConsole.Write(table);
                });
            break;

        case "Restore last session":
            AnsiConsole.MarkupLine("[yellow]Restoring last session...[/]");
            await orchestrator.RestoreAsync();
            AnsiConsole.MarkupLine("[green]Done.[/]");
            break;

        case "Restore named session":
            var sessions = await repo.ListAsync();
            if (!sessions.Any()) { AnsiConsole.MarkupLine("[red]No sessions found.[/]"); break; }

            var picked = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select session:")
                    .AddChoices(sessions.Select(s =>
                        $"{s.SessionName} ({s.CreatedAt:g}) [{s.TotalItems} items]")));

            var idx = sessions.ToList().FindIndex(s =>
                picked.StartsWith(s.SessionName));
            if (idx >= 0)
                await orchestrator.RestoreAsync(sessions[idx].SessionId);
            break;

        case "List sessions":
            var list = await repo.ListAsync();
            if (!list.Any()) { AnsiConsole.MarkupLine("[red]No sessions found.[/]"); break; }

            var t = new Table()
                .AddColumn("Name")
                .AddColumn("Saved At")
                .AddColumn("Items");
            foreach (var s in list)
                t.AddRow(s.SessionName, s.CreatedAt.ToString("g"), s.TotalItems.ToString());
            AnsiConsole.Write(t);
            break;

        case "Delete a session":
            var listD = await repo.ListAsync();
            if (!listD.Any()) { AnsiConsole.MarkupLine("[red]No sessions.[/]"); break; }

            var toDelete = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[red]Select session to delete:[/]")
                    .AddChoices(listD.Select(s => $"{s.SessionName} ({s.CreatedAt:g})")));

            var idxD = listD.ToList().FindIndex(s => toDelete.StartsWith(s.SessionName));
            if (idxD >= 0)
            {
                await repo.DeleteAsync(listD[idxD].SessionId);
                AnsiConsole.MarkupLine("[green]Deleted.[/]");
            }
            break;

        case "Enable auto-start on login":
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location
                         .Replace(".dll", ".exe");
            RegistryHelper.EnableAutoStart("DesktopSessionManager", exePath);
            AnsiConsole.MarkupLine("[green]Auto-start enabled. Session will restore 10 s after login.[/]");
            break;

        case "Disable auto-start on login":
            RegistryHelper.DisableAutoStart("DesktopSessionManager");
            AnsiConsole.MarkupLine("[yellow]Auto-start disabled.[/]");
            break;

        case "Exit":
            return 0;
    }

    AnsiConsole.WriteLine();
}
```

---

## SECTION 13: SYSTEM TRAY APPLICATION

### File: src/DesktopSessionManager.Tray/App.xaml

```xml
<Application x:Class="DesktopSessionManager.Tray.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
</Application>
```

### File: src/DesktopSessionManager.Tray/App.xaml.cs

```csharp
using System.Windows;

namespace DesktopSessionManager.Tray;

public partial class App : Application
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
```

### File: src/DesktopSessionManager.Tray/TrayApplicationContext.cs

```csharp
using System.Drawing;
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
```

---

## SECTION 14: UNIT TESTS

### File: tests/DesktopSessionManager.Tests/Unit/JsonSessionRepositoryTests.cs

```csharp
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DesktopSessionManager.Tests.Unit;

public sealed class JsonSessionRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly JsonSessionRepository _repo;

    public JsonSessionRepositoryTests()
    {
        _repo = new JsonSessionRepository(_tempDir, maxBackups: 5,
            NullLogger<JsonSessionRepository>.Instance);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_Succeeds()
    {
        var state = new SessionState { SessionName = "Test" };
        state.ExplorerFolders.Add(new ExplorerFolder { FolderPath = @"C:\TestFolder" });

        await _repo.SaveAsync(state);
        var loaded = await _repo.LoadAsync(state.SessionId);

        loaded.Should().NotBeNull();
        loaded!.SessionId.Should().Be(state.SessionId);
        loaded.ExplorerFolders.Should().HaveCount(1);
        loaded.ExplorerFolders[0].FolderPath.Should().Be(@"C:\TestFolder");
    }

    [Fact]
    public async Task LoadLatest_ReturnsLastSaved()
    {
        var first  = new SessionState { SessionName = "First" };
        var second = new SessionState { SessionName = "Second" };

        await _repo.SaveAsync(first);
        await Task.Delay(10); // ensure ordering
        await _repo.SaveAsync(second);

        var latest = await _repo.LoadLatestAsync();
        latest.Should().NotBeNull();
        latest!.SessionName.Should().Be("Second");
    }

    [Fact]
    public async Task List_ReturnsCorrectSummaries()
    {
        var s1 = new SessionState { SessionName = "A" };
        var s2 = new SessionState { SessionName = "B" };
        s1.TextFiles.Add(new TextFileInstance());
        s2.ExplorerFolders.Add(new ExplorerFolder());

        await _repo.SaveAsync(s1);
        await _repo.SaveAsync(s2);

        var list = await _repo.ListAsync();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var state = new SessionState { SessionName = "ToDelete" };
        await _repo.SaveAsync(state);

        await _repo.DeleteAsync(state.SessionId);
        var loaded = await _repo.LoadAsync(state.SessionId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Prune_KeepsMaxBackups()
    {
        for (int i = 0; i < 8; i++)
        {
            await _repo.SaveAsync(new SessionState { SessionName = $"Session {i}" });
            await Task.Delay(5);
        }

        var list = await _repo.ListAsync();
        list.Should().HaveCountLessOrEqualTo(5);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

### File: tests/DesktopSessionManager.Tests/Unit/SessionOrchestratorTests.cs

```csharp
using DesktopSessionManager.Core.Models;
using DesktopSessionManager.Core.Repositories;
using DesktopSessionManager.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DesktopSessionManager.Tests.Unit;

public sealed class SessionOrchestratorTests
{
    private readonly Mock<ICaptureService>   _captureA = new();
    private readonly Mock<ICaptureService>   _captureB = new();
    private readonly Mock<IRestoreService>   _restore  = new();
    private readonly Mock<ISessionRepository> _repo    = new();

    private SessionOrchestrator CreateOrchestrator()
    {
        _captureA.Setup(s => s.Name).Returns("CaptureA");
        _captureB.Setup(s => s.Name).Returns("CaptureB");
        _restore.Setup(s => s.Name).Returns("Restore");

        return new SessionOrchestrator(
            [_captureA.Object, _captureB.Object],
            [_restore.Object],
            _repo.Object,
            NullLogger<SessionOrchestrator>.Instance);
    }

    [Fact]
    public async Task CaptureAndSave_CallsAllCaptureServices()
    {
        var sut = CreateOrchestrator();
        _repo.Setup(r => r.SaveAsync(It.IsAny<SessionState>(), default))
             .Returns(Task.CompletedTask);

        await sut.CaptureAndSaveAsync("Test");

        _captureA.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
        _captureB.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
        _repo.Verify(r => r.SaveAsync(It.IsAny<SessionState>(), default), Times.Once);
    }

    [Fact]
    public async Task CaptureAndSave_FailingService_DoesNotStopOthers()
    {
        var sut = CreateOrchestrator();
        _captureA.Setup(s => s.CaptureAsync(It.IsAny<SessionState>(), default))
                 .ThrowsAsync(new Exception("A failed"));
        _repo.Setup(r => r.SaveAsync(It.IsAny<SessionState>(), default))
             .Returns(Task.CompletedTask);

        await sut.Invoking(o => o.CaptureAndSaveAsync()).Should().NotThrowAsync();

        _captureB.Verify(s => s.CaptureAsync(It.IsAny<SessionState>(), default), Times.Once);
    }

    [Fact]
    public async Task Restore_WhenNoSession_DoesNotThrow()
    {
        var sut = CreateOrchestrator();
        _repo.Setup(r => r.LoadLatestAsync(default)).ReturnsAsync((SessionState?)null);

        await sut.Invoking(o => o.RestoreAsync()).Should().NotThrowAsync();
        _restore.Verify(r => r.RestoreAsync(It.IsAny<SessionState>(), default), Times.Never);
    }
}
```

---

## SECTION 15: BUILD, PUBLISH & DEPLOYMENT

### Build Commands

```bash
# Build entire solution
dotnet build DesktopSessionManager.sln -c Release

# Run all tests
dotnet test DesktopSessionManager.sln

# Publish Console App as single-file self-contained exe
dotnet publish src/DesktopSessionManager.ConsoleApp \
  -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o ./publish/console

# Publish Tray App
dotnet publish src/DesktopSessionManager.Tray \
  -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/tray
```

### Installation Script (install.ps1)

```powershell
# Run as Administrator
$installDir = "$env:LOCALAPPDATA\DesktopSessionManager"
New-Item -ItemType Directory -Force -Path $installDir

Copy-Item "./publish/console/*" $installDir -Recurse -Force
Copy-Item "./publish/tray/*"    $installDir -Recurse -Force

$exePath = Join-Path $installDir "SessionManager.exe"
Write-Host "Installed to: $exePath"
Write-Host "Run 'SessionManager.exe' to start, or use the tray app."
```

---

## SECTION 16: USAGE GUIDE

### First-Time Setup

```bash
# 1. Install
./install.ps1

# 2. Save your current session (do this BEFORE any update)
SessionManager.exe

# 3. Enable auto-restore on login (do once)
# Select "Enable auto-start on login" from the menu

# 4. Windows will now auto-restore your session after any restart
```

### For Full Browser URL Capture (Optional)

Start Chrome with remote debugging enabled:

```powershell
# Create a shortcut with this target:
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222
```

Without this, browser tabs are captured by title only (URLs cannot be restored automatically).

### Manual Session Save Before an Update


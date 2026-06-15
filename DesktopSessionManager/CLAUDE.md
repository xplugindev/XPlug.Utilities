# CLAUDE.md — Desktop Session Manager

## Project Overview

**DesktopSessionManager** is a Windows 10/11 desktop utility (.NET 8.0) that captures and restores a user's complete workspace state — browser tabs, File Explorer folders, open text files, and running applications.

- **Language**: C# 12
- **Framework**: .NET 8.0 (`net8.0-windows10.0.19041.0` for Windows-specific projects)
- **Architecture**: Clean Architecture — Core → Infrastructure → UI
- **Solution file**: `DesktopSessionManager.sln`

---

## Repository Layout

```
src/
  DesktopSessionManager.Core/           # Pure C#, no platform deps
  DesktopSessionManager.Infrastructure/ # Windows API, Selenium, file I/O
  DesktopSessionManager.ConsoleApp/     # CLI entry point (SessionManager.exe)
  DesktopSessionManager.Tray/           # WPF system tray (SessionManagerTray.exe)
tests/
  DesktopSessionManager.Tests/          # xUnit tests
```

---

## Build & Test Commands

```bash
# Build entire solution
dotnet build DesktopSessionManager.sln

# Build release
dotnet build DesktopSessionManager.sln -c Release

# Run all tests
dotnet test DesktopSessionManager.sln

# Publish console app (Windows, self-contained single exe)
dotnet publish src/DesktopSessionManager.ConsoleApp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/console

# Publish tray app
dotnet publish src/DesktopSessionManager.Tray -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/tray
```

---

## Architecture & Key Files

### Core Layer (`DesktopSessionManager.Core`)
No Windows or I/O dependencies. Contains only interfaces and the orchestrator.

| File | Purpose |
|------|---------|
| `Models/SessionState.cs` | All model classes: `SessionState`, `BrowserWindow`, `BrowserTab`, `ExplorerFolder`, `TextFileInstance`, `AppInstance`, `TerminalSession`, `WindowRect`, `SystemInfo` |
| `Services/ICaptureService.cs` | Interface for capture services — `Name` + `CaptureAsync(SessionState, ct)` |
| `Services/IRestoreService.cs` | Interface for restore services — `Name` + `RestoreAsync(SessionState, ct)` |
| `Repositories/ISessionRepository.cs` | CRUD interface for sessions + `SessionSummary` record |
| `Services/SessionOrchestrator.cs` | Iterates all `ICaptureService`s / `IRestoreService`s; catches per-service failures gracefully |

### Infrastructure Layer (`DesktopSessionManager.Infrastructure`)
All Windows API, Selenium, WMI, registry, and file I/O code lives here.

| File | Purpose |
|------|---------|
| `WindowsAPI/NativeMethods.cs` | P/Invoke: `EnumWindows`, `GetWindowText`, `GetWindowRect`, `IsZoomed`, `IsIconic` |
| `WindowsAPI/WindowEnumerator.cs` | Enumerates all visible top-level windows into `WindowInfo` objects |
| `WindowsAPI/ProcessHelper.cs` | Gets exe path, command-line (via WMI), working directory for a process |
| `Capture/BrowserCaptureService.cs` | Captures browser tabs via CDP (Selenium) or window title fallback |
| `Capture/FolderCaptureService.cs` | Captures open Explorer windows via title → path resolution |
| `Capture/TextFileCaptureService.cs` | Captures open files in editors (Notepad, VS Code, Rider, etc.) by parsing window titles |
| `Capture/ApplicationCaptureService.cs` | Captures all other foreground applications not handled by specialised services |
| `Restore/FolderRestoreService.cs` | Opens folders with `explorer.exe` |
| `Restore/TextFileRestoreService.cs` | Launches editors with the correct file/line arguments |
| `Restore/ApplicationRestoreService.cs` | Re-launches apps with original exe path + arguments |
| `Restore/BrowserRestoreService.cs` | Opens browser windows with `--new-window` / `--new-tab` flags |
| `Storage/JsonSessionRepository.cs` | Atomic JSON save/load, `latest.txt` pointer, rolling pruning |
| `System/RegistryHelper.cs` | Enable/disable/check `HKCU\...\Run` auto-start entry |

### ConsoleApp (`DesktopSessionManager.ConsoleApp`)

| File | Purpose |
|------|---------|
| `Program.cs` | Top-level program: bootstraps DI, Serilog, and runs the Spectre.Console interactive menu |
| `appsettings.json` | Runtime configuration (storage path, skip list, CDP port, delays) |

### Tray App (`DesktopSessionManager.Tray`)
WPF app with no visible window. Windows-only; builds as an empty library stub on Linux/macOS.

| File | Purpose |
|------|---------|
| `App.xaml / App.xaml.cs` | WPF Application entry; creates `TrayApplicationContext` on startup |
| `TrayApplicationContext.cs` | `NotifyIcon` + `ContextMenuStrip`; 15-minute auto-save timer |

### Tests (`DesktopSessionManager.Tests`)

| File | What it tests |
|------|---------------|
| `Unit/JsonSessionRepositoryTests.cs` | Save/load round-trip, latest pointer, list, delete, rolling prune |
| `Unit/SessionOrchestratorTests.cs` | All capture services called, failing service doesn't abort others, missing session handled |

---

## Dependency Injection Setup

Services are registered in `Program.cs` (ConsoleApp). Registration order matters — it controls execution order for capture and restore:

```
Capture order:  FolderCapture → TextFileCapture → ApplicationCapture → BrowserCapture
Restore order:  FolderRestore → TextFileRestore → ApplicationRestore → BrowserRestore
```

`SessionOrchestrator` receives `IEnumerable<ICaptureService>` and `IEnumerable<IRestoreService>` and iterates them in registration order, catching per-service exceptions so one failure does not abort the rest.

---

## Session JSON Format

Sessions are stored in `%LOCALAPPDATA%\DesktopSessionManager\sessions\session_<id>.json`.

```json
{
  "sessionId": "...",
  "sessionName": "2025-01-15 09:30",
  "schemaVersion": 1,
  "createdAt": "2025-01-15T09:30:00",
  "browserWindows": [...],
  "explorerFolders": [...],
  "textFiles": [...],
  "applications": [...],
  "terminals": [...],
  "system": { "machineName": "...", "userName": "...", "osVersion": "...", "screenCount": 2 }
}
```

A `latest.txt` file in the same directory holds the `sessionId` of the most recently saved session.

---

## Configuration (`appsettings.json`)

```json
{
  "SessionManager": {
    "StoragePath": "%LOCALAPPDATA%/DesktopSessionManager/sessions",
    "MaxSessionBackups": 10,
    "AutoSaveIntervalMinutes": 15,
    "AutoRestoreDelaySeconds": 10,
    "RestoreDelayBetweenAppsMs": 2000,
    "BrowserAutomation": { "ChromeDebugPort": 9222, "TimeoutSeconds": 10 },
    "SkipProcessNames": ["dwm", "winlogon", "svchost", ...]
  }
}
```

---

## Platform Notes

- All Windows-targeted projects set `<EnableWindowsTargeting>true</EnableWindowsTargeting>` to allow building on Linux CI.
- The Tray project uses `<UseWPF>` / `<UseWindowsForms>` only when `$(OS) == Windows_NT`; source files are excluded and `OutputType` is set to `Library` on non-Windows hosts.
- Infrastructure and ConsoleApp do **not** set `UseWindowsForms` — they use only P/Invoke and WMI.
- Tests target `net8.0-windows10.0.19041.0` (matches Infrastructure) so NuGet compatibility is satisfied.

---

## Logging

Serilog writes to:
- **Console** (structured, coloured)
- **File**: `%LOCALAPPDATA%\DesktopSessionManager\logs\log-YYYYMMDD.txt`
  - Rolling interval: daily
  - Retention: 7 files
  - Max size: 50 MB

Log level is `Information` by default; set `"MinimumLevel": "Debug"` in `appsettings.json` for verbose output.

---

## Adding a New Capture or Restore Service

1. Create a class in `Infrastructure/Capture/` or `Infrastructure/Restore/` implementing `ICaptureService` or `IRestoreService`.
2. Implement `string Name { get; }` and the async method.
3. Register it with `services.AddSingleton<ICaptureService, YourNewService>()` in `Program.cs` at the desired position in the execution order.
4. No changes needed to `SessionOrchestrator` — it discovers services via DI.

---

## Common Gotchas

- **Namespace collision**: `DesktopSessionManager.Infrastructure.System` shadows the BCL `System` namespace in files within the Infrastructure assembly. Use `global::System.Text.RegularExpressions.Regex` (as done in `FolderCaptureService.cs`) when you need BCL types.
- **CDP capture requires the browser to already be running** with `--remote-debugging-port=9222`. If not, `BrowserCaptureService` falls back to window-title capture silently.
- **`ProcessHelper.GetCommandLine`** uses WMI (`Win32_Process`) which may fail without elevated permissions; it catches and returns empty string.
- **Tray constructor**: `TrayApplicationContext` has both a parameterised and a parameterless constructor (the latter exists for non-Windows stub builds where source is excluded).

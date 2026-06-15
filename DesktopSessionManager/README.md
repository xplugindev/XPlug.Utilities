# Desktop Session Manager

A Windows desktop utility that captures and restores your complete workspace — browser tabs, File Explorer folders, open text files, and running applications — so your workflow survives Windows Updates and unexpected reboots.

**Target**: Windows 10 (1809+) / Windows 11  
**Runtime**: .NET 8.0  
**Architecture**: Clean Architecture (Core → Infrastructure → UI)

---

## Features

### Session Capture
- Detects all open browser windows/tabs: Chrome, Firefox, Edge, Opera, Brave
- Captures real URLs via Chrome DevTools Protocol (CDP) where available; falls back to window titles
- Tracks all File Explorer windows with full folder paths
- Detects files open in: Notepad, Notepad++, VS Code, Visual Studio 2022, Sublime Text, JetBrains Rider/WebStorm/IntelliJ/PyCharm
- Records window positions (X, Y, Width, Height, monitor index)
- Records text-editor cursor position (line, column) where inferable from title
- Records working directory for terminal windows
- Captures process name, executable path, and command-line arguments for all apps

### Session Persistence
- Saves session as JSON in `%LOCALAPPDATA%\DesktopSessionManager\sessions\`
- Supports multiple named sessions (default name = timestamp)
- Keeps last 10 sessions as rolling backup
- Atomic write (write to `.tmp`, then rename) to prevent corruption
- Includes schema version in JSON for future migration

### Session Restoration
- Re-opens File Explorer folders in their original positions
- Re-opens text files in their original editors at the same line numbers
- Re-launches applications with their original arguments
- Re-opens browser tabs (URL if available, else skips with a warning)
- Restores in correct order: folders → editors → apps → browsers
- 2-second delay between each application launch to avoid race conditions
- Skips missing files/apps gracefully and logs what was skipped

### Auto-Start & Auto-Restore
- Registers with `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- On startup with `--autostart` flag: waits 10 s, then restores last session
- Auto-saves every 15 minutes while running in tray mode

### User Interface
- **Console App** — interactive CLI with save/restore/list/delete and Spectre.Console rich output
- **System Tray App** — WPF tray icon with right-click menu (Save Now, Restore Last, Open Manager, Exit)
- Windows toast notifications for save/restore events

---

## Project Structure

```
DesktopSessionManager/
├── DesktopSessionManager.sln
├── src/
│   ├── DesktopSessionManager.Core/             # Pure C#, no Windows deps
│   │   ├── Models/
│   │   │   └── SessionState.cs                 # All model classes
│   │   ├── Services/
│   │   │   ├── ICaptureService.cs
│   │   │   ├── IRestoreService.cs
│   │   │   └── SessionOrchestrator.cs          # Main coordinator
│   │   └── Repositories/
│   │       └── ISessionRepository.cs
│   │
│   ├── DesktopSessionManager.Infrastructure/   # Windows API, Selenium, file I/O
│   │   ├── WindowsAPI/
│   │   │   ├── NativeMethods.cs                # P/Invoke signatures
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
│   ├── DesktopSessionManager.ConsoleApp/       # CLI entry point
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── DesktopSessionManager.Tray/             # WPF system tray app (Windows only)
│       ├── App.xaml / App.xaml.cs
│       └── TrayApplicationContext.cs
│
└── tests/
    └── DesktopSessionManager.Tests/
        └── Unit/
            ├── JsonSessionRepositoryTests.cs
            └── SessionOrchestratorTests.cs
```

---

## Requirements

- Windows 10 (1809+) or Windows 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- For browser URL capture: Chrome or Edge started with `--remote-debugging-port=9222`

---

## Build

```bash
# Restore and build entire solution
dotnet build DesktopSessionManager.sln -c Release

# Run all tests
dotnet test DesktopSessionManager.sln

# Publish Console App (single-file self-contained exe)
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

> **Note for Linux/macOS developers:** The Infrastructure and ConsoleApp projects target `net8.0-windows` and build with `EnableWindowsTargeting=true`. The Tray project (WPF/WinForms) builds as a stub on non-Windows hosts; full compilation requires Windows.

---

## Installation

```powershell
# Run install.ps1 (copies published output to %LOCALAPPDATA%\DesktopSessionManager)
.\install.ps1
```

The script copies both `SessionManager.exe` (console) and `SessionManagerTray.exe` (tray) to `%LOCALAPPDATA%\DesktopSessionManager`.

---

## Usage

### Interactive Console

```
SessionManager.exe
```

Menu options:
- **Save session** — captures current workspace and saves as JSON
- **Restore last session** — relaunches everything from the most recent save
- **Restore named session** — pick any saved session from a list
- **List sessions** — shows all saved sessions with item counts
- **Delete a session** — removes a saved session
- **Enable auto-start on login** — registers with Windows startup; auto-restores 10 s after login
- **Disable auto-start on login** — removes the startup registry entry

### System Tray

```
SessionManagerTray.exe
```

Runs silently in the system tray and auto-saves every 15 minutes. Right-click the tray icon for Save Now, Restore Last, Open Manager, and Exit.

### Auto-start (Manual)

```
SessionManager.exe --autostart
```

Called automatically by the Windows startup entry. Waits 10 seconds for the desktop to settle, then restores the last session.

---

## Browser URL Capture (Optional)

Without remote debugging, browser tabs are captured by window title only and cannot be automatically restored by URL.

To enable full URL capture, start Chrome with:

```powershell
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222
```

Create a shortcut with this target for daily use.

---

## Configuration

`appsettings.json` (in the ConsoleApp output directory):

| Key | Default | Description |
|-----|---------|-------------|
| `SessionManager:StoragePath` | `%LOCALAPPDATA%/DesktopSessionManager/sessions` | Where session JSON files are stored |
| `SessionManager:MaxSessionBackups` | `10` | Rolling backup count |
| `SessionManager:AutoSaveIntervalMinutes` | `15` | Tray auto-save interval |
| `SessionManager:AutoRestoreDelaySeconds` | `10` | Delay before restoring on autostart |
| `SessionManager:RestoreDelayBetweenAppsMs` | `2000` | Delay between launching each app |
| `SessionManager:BrowserAutomation:ChromeDebugPort` | `9222` | CDP debug port |
| `SessionManager:SkipProcessNames` | *(system processes)* | Process names excluded from capture |

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              ConsoleApp / Tray (UI Layer)                │
│   Program.cs / TrayApplicationContext                    │
└────────────────────────┬────────────────────────────────┘
                         │ uses
┌────────────────────────▼────────────────────────────────┐
│                  Core Layer                              │
│  SessionOrchestrator  ←→  ISessionRepository            │
│  ICaptureService[]        IRestoreService[]             │
└────────────────────────┬────────────────────────────────┘
                         │ implements
┌────────────────────────▼────────────────────────────────┐
│              Infrastructure Layer                        │
│  BrowserCaptureService    FolderRestoreService          │
│  FolderCaptureService     TextFileRestoreService        │
│  TextFileCaptureService   ApplicationRestoreService     │
│  ApplicationCapture...    BrowserRestoreService         │
│  JsonSessionRepository    RegistryHelper                │
│  WindowEnumerator         ProcessHelper                 │
└─────────────────────────────────────────────────────────┘
```

### Key Design Decisions
- **Clean Architecture**: Core has zero Windows or file-system dependencies; all platform code lives in Infrastructure.
- **Graceful degradation**: Each capture/restore service runs independently; a failure in one does not stop others.
- **Atomic saves**: Sessions are written to a `.tmp` file then renamed to prevent partial writes corrupting data.
- **Ordered restoration**: Folders open first so editors can find their workspaces; browsers open last to avoid focus stealing.

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Selenium.WebDriver | 4.18.1 | Chrome DevTools Protocol for URL capture |
| Serilog | 3.1.1 | Structured logging |
| Serilog.Sinks.File | 5.0.0 | Rolling file log sink |
| Spectre.Console | 0.48.0 | Rich terminal UI (tables, progress bars) |
| Hardcodet.NotifyIcon.Wpf | 1.1.0 | WPF system tray icon |
| Microsoft.Extensions.Hosting | 8.0.0 | DI / configuration |
| xunit | 2.6.6 | Unit test framework |
| Moq | 4.20.70 | Test mocking |
| FluentAssertions | 6.12.0 | Fluent test assertions |

---

## Logs

Logs are written to `%LOCALAPPDATA%\DesktopSessionManager\logs\log-YYYYMMDD.txt`.  
Rolling interval: daily. Retention: 7 days. Max size: 50 MB per file.

---

## License

MIT

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

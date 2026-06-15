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

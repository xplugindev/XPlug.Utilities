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

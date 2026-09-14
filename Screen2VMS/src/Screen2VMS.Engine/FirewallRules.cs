using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Screen2VMS.Engine;

/// <summary>
/// Creates the Windows Firewall rules Screen2VMS needs (spec 27).
/// </summary>
/// <remarks>
/// <para>
/// Screen2VMS itself runs unelevated (spec 5), and adding a firewall rule
/// needs administrator rights, so this launches an elevated helper on demand
/// and only when the user asks. Nothing here happens silently.
/// </para>
/// <para>
/// Windows will usually offer its own prompt the first time a socket is bound;
/// this exists for the case where that prompt was dismissed or suppressed by
/// policy, which otherwise leaves a camera that works locally and is invisible
/// to the VMS.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class FirewallRules
{
    public const string DiscoveryRuleName = "Screen2VMS WS-Discovery";

    /// <summary>One inbound rule to create: a display name, a protocol ("TCP"/"UDP") and a port.</summary>
    public readonly record struct Rule(string Name, string Protocol, int Port);

    /// <summary>
    /// Adds inbound rules for every port passed in, replacing whatever
    /// Screen2VMS rules already exist under those same names.
    /// </summary>
    /// <remarks>
    /// Triggers a UAC prompt. Returns false if the user declined it or the
    /// helper failed, which is not fatal - it just means the ports may be
    /// blocked. Every camera's RTSP and ONVIF ports are passed in alongside
    /// the one shared WS-Discovery port, since several cameras publish from
    /// the same process (spec override, see CLAUDE.md "multi-camera").
    /// </remarks>
    public static bool TryCreate(IReadOnlyCollection<Rule> rules, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        logger ??= NullLogger.Instance;

        if (rules.Count == 0)
        {
            return true;
        }

        // Existing rules are removed first so changing a port does not leave a
        // stale rule behind that still allows the old one.
        var script = string.Join(
            "; ",
            rules.Select(rule => RemoveCommand(rule.Name))
                .Concat(rules.Select(rule => AddCommand(rule.Name, rule.Protocol, rule.Port))));

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(30000);
            var created = process.HasExited && process.ExitCode == 0;

            if (created)
            {
                logger.LogInformation(
                    "Firewall rules created for {Rules}.",
                    string.Join(", ", rules.Select(rule => $"{rule.Protocol} {rule.Port}")));
            }
            else
            {
                logger.LogWarning("The firewall helper did not complete successfully.");
            }

            return created;
        }
        catch (Exception ex)
        {
            // Declining the UAC prompt throws; that is a choice, not a fault.
            logger.LogWarning(ex, "Firewall rules were not created.");
            return false;
        }
    }

    private static string AddCommand(string name, string protocol, int port) =>
        $"New-NetFirewallRule -DisplayName '{name}' -Direction Inbound -Action Allow " +
        $"-Protocol {protocol} -LocalPort {port} -Profile Domain,Private | Out-Null";

    private static string RemoveCommand(string name) =>
        $"Remove-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue";
}

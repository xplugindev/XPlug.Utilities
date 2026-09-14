using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Configuration;

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
/// <para>
/// Every call is a full sync rather than an add: all Screen2VMS rules are
/// removed, including ones from earlier versions and from cameras that have
/// since been removed, and exactly the rules for the current configuration are
/// created. So pressing the button again is always the fix for stale rules.
/// </para>
/// <para>
/// The elevated script is built only from constants and validated port
/// numbers. Camera names come from device firmware and must never reach it:
/// an apostrophe in one used to break the script, and anything a device reports
/// has no business in a command running as administrator.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class FirewallRules
{
    /// <summary>The Windows Firewall group every rule is created in.</summary>
    public const string GroupName = "Screen2VMS";

    /// <summary>
    /// Display names from earlier versions, matched with a trailing wildcard
    /// when cleaning up. The single-camera version used exactly these names and
    /// no group; the first multi-camera build appended the camera name.
    /// </summary>
    internal static readonly string[] LegacyDisplayNamePatterns =
    [
        "Screen2VMS RTSP*",
        "Screen2VMS ONVIF*",
        "Screen2VMS WS-Discovery*",
    ];

    public enum RuleKind
    {
        Discovery,
        Rtsp,
        Onvif,
    }

    /// <summary>One inbound allow rule. Its names derive from kind and port only.</summary>
    public readonly record struct Rule(RuleKind Kind, int Port)
    {
        public string Protocol => Kind == RuleKind.Discovery ? "UDP" : "TCP";

        /// <summary>The rule's unique internal name. Unique per port, so identically named cameras cannot collide.</summary>
        public string Name => string.Create(CultureInfo.InvariantCulture, $"Screen2VMS-{Kind}-{Protocol}-{Port}");

        /// <summary>What the Windows Firewall console shows.</summary>
        public string DisplayName => string.Create(CultureInfo.InvariantCulture, $"Screen2VMS {Label} ({Protocol} {Port})");

        private string Label => Kind switch
        {
            RuleKind.Discovery => "WS-Discovery",
            RuleKind.Rtsp => "RTSP",
            _ => "ONVIF",
        };
    }

    /// <summary>
    /// The rules for a configuration: WS-Discovery once, plus RTSP and ONVIF
    /// for every configured camera. With no cameras there is nothing to open.
    /// </summary>
    public static IReadOnlyList<Rule> For(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Cameras.Count == 0)
        {
            return [];
        }

        return new[] { new Rule(RuleKind.Discovery, configuration.Discovery.Port) }
            .Concat(configuration.Cameras.SelectMany(profile => new[]
            {
                new Rule(RuleKind.Rtsp, profile.Rtsp.Port),
                new Rule(RuleKind.Onvif, profile.Onvif.Port),
            }))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Replaces every Screen2VMS firewall rule with <paramref name="rules"/>.
    /// An empty list removes them all.
    /// </summary>
    /// <remarks>
    /// Triggers a UAC prompt. Returns false if the user declined it or the
    /// helper failed, which is not fatal - it just means the ports may be
    /// blocked.
    /// </remarks>
    public static bool TrySync(IReadOnlyCollection<Rule> rules, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        logger ??= NullLogger.Instance;

        var script = BuildScript(rules);

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
            var synced = process.HasExited && process.ExitCode == 0;

            if (synced)
            {
                logger.LogInformation(
                    "Firewall rules synced: {Rules}.",
                    rules.Count == 0 ? "all removed" : string.Join(", ", rules.Select(rule => $"{rule.Protocol} {rule.Port}")));
            }
            else
            {
                logger.LogWarning("The firewall helper did not complete successfully.");
            }

            return synced;
        }
        catch (Exception ex)
        {
            // Declining the UAC prompt throws; that is a choice, not a fault.
            logger.LogWarning(ex, "Firewall rules were not changed.");
            return false;
        }
    }

    /// <summary>The PowerShell run elevated. Separate from <see cref="TrySync"/> so it can be tested without elevation.</summary>
    internal static string BuildScript(IReadOnlyCollection<Rule> rules)
    {
        foreach (var rule in rules)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(rule.Port, 1, nameof(rules));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(rule.Port, 65535, nameof(rules));

            if (!Enum.IsDefined(rule.Kind))
            {
                throw new ArgumentOutOfRangeException(nameof(rules), rule.Kind, "Unknown firewall rule kind.");
            }
        }

        var legacy = string.Join(",", LegacyDisplayNamePatterns.Select(pattern => $"'{pattern}'"));

        // Stop on the first failure so a half-applied sync exits non-zero
        // instead of reporting success.
        //
        // Removal filters with Remove-NetFirewallRule's own parameters, never
        // "Get-NetFirewallRule ... | Remove-NetFirewallRule": the filter is then
        // part of the one command, so there is no path where an empty match
        // reaches a Remove with nothing narrowing it. "Not found" is expected
        // and silenced per command. The patterns deliberately do not match the
        // bare "Screen2VMS" rules Windows creates from its own first-run prompt.
        var commands = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            $"Remove-NetFirewallRule -Group '{GroupName}' -ErrorAction SilentlyContinue",
            $"Remove-NetFirewallRule -DisplayName {legacy} -ErrorAction SilentlyContinue",
        };

        commands.AddRange(rules.Distinct().Select(rule =>
            $"New-NetFirewallRule -Name '{rule.Name}' -DisplayName '{rule.DisplayName}' -Group '{GroupName}' " +
            $"-Direction Inbound -Action Allow -Protocol {rule.Protocol} " +
            $"-LocalPort {rule.Port.ToString(CultureInfo.InvariantCulture)} -Profile Domain,Private | Out-Null"));

        return string.Join("; ", commands);
    }
}

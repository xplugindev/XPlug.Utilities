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
    public const string RtspRuleName = "Screen2VMS RTSP";
    public const string OnvifRuleName = "Screen2VMS ONVIF";
    public const string DiscoveryRuleName = "Screen2VMS WS-Discovery";

    /// <summary>
    /// Adds inbound rules for the RTSP, ONVIF and discovery ports.
    /// </summary>
    /// <remarks>
    /// Triggers a UAC prompt. Returns false if the user declined it or the
    /// helper failed, which is not fatal - it just means the ports may be
    /// blocked.
    /// </remarks>
    public static bool TryCreate(int rtspPort, int onvifPort, int discoveryPort, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        // Existing rules are removed first so changing a port does not leave a
        // stale rule behind that still allows the old one.
        var script = string.Join(
            "; ",
            RemoveCommand(RtspRuleName),
            RemoveCommand(OnvifRuleName),
            RemoveCommand(DiscoveryRuleName),
            AddCommand(RtspRuleName, "TCP", rtspPort),
            AddCommand(OnvifRuleName, "TCP", onvifPort),
            AddCommand(DiscoveryRuleName, "UDP", discoveryPort));

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
                    "Firewall rules created for TCP {RtspPort}, TCP {OnvifPort} and UDP {DiscoveryPort}.",
                    rtspPort,
                    onvifPort,
                    discoveryPort);
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

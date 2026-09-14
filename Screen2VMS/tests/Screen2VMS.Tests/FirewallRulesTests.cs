using Screen2VMS.Core.Configuration;
using Screen2VMS.Engine;
using static Screen2VMS.Engine.FirewallRules;

namespace Screen2VMS.Tests;

/// <summary>
/// Covers the rules and the elevated script <see cref="FirewallRules"/> builds.
/// Nothing here runs the script: that needs a UAC prompt. What these pin down
/// is everything that decides what the script does once it is approved.
/// </summary>
public class FirewallRulesTests
{
    [Fact]
    public void For_OpensDiscoveryOnce_AndRtspAndOnvifPerCamera()
    {
        var configuration = WithCameras(Camera("Integrated Camera", 8554, 8000), Camera("USB Camera", 8555, 8001));

        var rules = FirewallRules.For(configuration);

        Assert.Equal(
            [
                new Rule(RuleKind.Discovery, 3702),
                new Rule(RuleKind.Rtsp, 8554),
                new Rule(RuleKind.Onvif, 8000),
                new Rule(RuleKind.Rtsp, 8555),
                new Rule(RuleKind.Onvif, 8001),
            ],
            rules);
    }

    [Fact]
    public void For_WithNoCameras_OpensNothing()
    {
        Assert.Empty(FirewallRules.For(new AppConfiguration()));
    }

    [Fact]
    public void IdenticallyNamedCameras_GetDistinctRuleNames()
    {
        // Two of the same USB webcam report the same friendly name. Rules used
        // to be named after it, so creating the second deleted the first.
        var configuration = WithCameras(Camera("USB Camera", 8554, 8000), Camera("USB Camera", 8555, 8001));

        var rules = FirewallRules.For(configuration);

        Assert.Equal(rules.Count, rules.Select(rule => rule.Name).Distinct().Count());
        Assert.Equal(rules.Count, rules.Select(rule => rule.DisplayName).Distinct().Count());
    }

    [Fact]
    public void Rule_NamesCarryProtocolAndPort()
    {
        var rule = new Rule(RuleKind.Rtsp, 8555);

        Assert.Equal("TCP", rule.Protocol);
        Assert.Equal("Screen2VMS-Rtsp-TCP-8555", rule.Name);
        Assert.Equal("Screen2VMS RTSP (TCP 8555)", rule.DisplayName);
        Assert.Equal("UDP", new Rule(RuleKind.Discovery, 3702).Protocol);
    }

    [Fact]
    public void Script_NeverContainsCameraNames()
    {
        // Device names come from firmware. An apostrophe used to break the
        // elevated script, and nothing a device reports belongs in it.
        const string hostileName = "Prem's Webcam\"; Remove-NetFirewallRule; '";
        var configuration = WithCameras(Camera(hostileName, 8554, 8000));

        var script = BuildScript(FirewallRules.For(configuration));

        Assert.DoesNotContain("Prem", script);
        Assert.DoesNotContain("\"", script);
    }

    [Fact]
    public void Script_RemovesGroupAndLegacyRules_BeforeAddingAnything()
    {
        var script = BuildScript([new Rule(RuleKind.Rtsp, 8554)]);

        var removeGroup = script.IndexOf("Remove-NetFirewallRule -Group 'Screen2VMS'", StringComparison.Ordinal);
        var removeLegacy = script.IndexOf(
            "Remove-NetFirewallRule -DisplayName 'Screen2VMS RTSP*','Screen2VMS ONVIF*','Screen2VMS WS-Discovery*'",
            StringComparison.Ordinal);
        var firstAdd = script.IndexOf("New-NetFirewallRule", StringComparison.Ordinal);

        Assert.True(removeGroup >= 0, script);
        Assert.True(removeLegacy >= 0, script);
        Assert.True(firstAdd > removeGroup && firstAdd > removeLegacy, script);
    }

    [Fact]
    public void Script_EveryRemoveIsFilteredByItsOwnParameters()
    {
        // "Get-NetFirewallRule | Remove-NetFirewallRule" or a bare
        // Remove-NetFirewallRule could reach rules that are not ours.
        var script = BuildScript([]);

        Assert.DoesNotContain("| Remove-NetFirewallRule", script);
        foreach (var command in script.Split("; "))
        {
            if (command.StartsWith("Remove-NetFirewallRule", StringComparison.Ordinal))
            {
                Assert.Matches("^Remove-NetFirewallRule -(Group|DisplayName) '", command);
            }
        }
    }

    [Fact]
    public void Script_LeavesWindowsOwnScreen2VmsAppRulesAlone()
    {
        // Windows names the rules from its first-run prompt just "Screen2VMS".
        // No removal pattern may match that.
        foreach (var pattern in LegacyDisplayNamePatterns)
        {
            Assert.NotEqual("Screen2VMS*", pattern);
            Assert.StartsWith("Screen2VMS ", pattern);
        }
    }

    [Fact]
    public void Script_WithNoRules_OnlyRemoves()
    {
        var script = BuildScript([]);

        Assert.Contains("Remove-NetFirewallRule", script);
        Assert.DoesNotContain("New-NetFirewallRule", script);
    }

    [Fact]
    public void Script_AddsEachRuleOnceInTheGroup()
    {
        var script = BuildScript([new Rule(RuleKind.Onvif, 8001), new Rule(RuleKind.Onvif, 8001)]);

        Assert.Single(script.Split("; "), command => command.StartsWith("New-NetFirewallRule", StringComparison.Ordinal));
        Assert.Contains(
            "New-NetFirewallRule -Name 'Screen2VMS-Onvif-TCP-8001' -DisplayName 'Screen2VMS ONVIF (TCP 8001)' -Group 'Screen2VMS' " +
            "-Direction Inbound -Action Allow -Protocol TCP -LocalPort 8001 -Profile Domain,Private",
            script);
    }

    [Fact]
    public void Script_StopsOnTheFirstFailure()
    {
        Assert.StartsWith("$ErrorActionPreference = 'Stop'", BuildScript([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Script_RejectsPortsOutOfRange(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildScript([new Rule(RuleKind.Rtsp, port)]));
    }

    private static AppConfiguration WithCameras(params CameraProfile[] cameras) => new() { Cameras = cameras };

    private static CameraProfile Camera(string name, int rtspPort, int onvifPort) => new()
    {
        Camera = new CameraConfiguration { Name = name },
        Rtsp = new RtspConfiguration { Port = rtspPort },
        Onvif = new OnvifConfiguration { Port = onvifPort },
    };
}

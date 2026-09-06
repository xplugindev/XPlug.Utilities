using System.Net;
using Screen2VMS.Configuration;
using Screen2VMS.Core.Diagnostics;
using Screen2VMS.Core.Networking;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Discovery;

namespace Screen2VMS.Tests;

public class DiscoveryScopeTests
{
    private static readonly OnvifDeviceInfo Device = new()
    {
        SerialNumber = "S2V-000000000001",
        MacAddress = "02AABBCCDDEE",
    };

    [Fact]
    public void Build_IncludesTheScopesAVmsClassifiesOn()
    {
        // Without these two a VMS sees the probe response but will not treat
        // the device as a camera.
        var scopes = OnvifDiscoveryScopes.Build(Device);

        Assert.Contains(OnvifDiscoveryScopes.ProfileStreamingScope, scopes);
        Assert.Contains(OnvifDiscoveryScopes.VideoEncoderTypeScope, scopes);
    }

    [Fact]
    public void Build_IncludesNameAndHardwareScopes()
    {
        var scopes = OnvifDiscoveryScopes.Build(Device);

        Assert.Contains(scopes, s => s.StartsWith("onvif://www.onvif.org/name/", StringComparison.Ordinal));
        Assert.Contains(scopes, s => s.StartsWith("onvif://www.onvif.org/hardware/", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EscapesSpacesInScopeValues()
    {
        // Scope values are URIs. An unescaped space makes strict clients drop
        // the whole scope.
        var scopes = OnvifDiscoveryScopes.Build(Device);
        var nameScope = scopes.Single(s => s.StartsWith("onvif://www.onvif.org/name/", StringComparison.Ordinal));

        Assert.DoesNotContain(' ', nameScope);
        Assert.Contains("Screen2VMS", nameScope, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AddsALocationScopeOnlyWhenOneIsGiven()
    {
        Assert.DoesNotContain(
            OnvifDiscoveryScopes.Build(Device),
            s => s.Contains("/location/", StringComparison.Ordinal));

        Assert.Contains(
            OnvifDiscoveryScopes.Build(Device, "Front Door"),
            s => s.Contains("/location/Front%20Door", StringComparison.Ordinal));
    }
}

public class HealthMonitorTests
{
    [Fact]
    public void NewMonitor_ReportsEveryComponentAsStopped()
    {
        var monitor = new HealthMonitor();

        foreach (var name in new[]
                 {
                     ComponentNames.Camera, ComponentNames.Encoder, ComponentNames.Rtsp,
                     ComponentNames.Onvif, ComponentNames.Discovery,
                 })
        {
            Assert.Equal(ComponentState.Stopped, monitor.Current.Components[name].State);
        }
    }

    [Fact]
    public void Report_RaisesChangedAndUpdatesTheSnapshot()
    {
        var monitor = new HealthMonitor();
        var raised = 0;
        monitor.Changed += (_, _) => raised++;

        monitor.Report(ComponentNames.Camera, ComponentState.Running, "1920x1080");

        Assert.Equal(1, raised);
        Assert.Equal(ComponentState.Running, monitor.Current.Components[ComponentNames.Camera].State);
        Assert.Equal("1920x1080", monitor.Current.Components[ComponentNames.Camera].Detail);
    }

    [Fact]
    public void Report_IsSilentWhenNothingChanged()
    {
        // The pipeline reports state on every restart attempt; re-raising an
        // identical state would spin the UI for no reason.
        var monitor = new HealthMonitor();
        monitor.Report(ComponentNames.Rtsp, ComponentState.Running, "port 8554");

        var raised = 0;
        monitor.Changed += (_, _) => raised++;
        monitor.Report(ComponentNames.Rtsp, ComponentState.Running, "port 8554");

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Snapshots_AreIndependentOfLaterChanges()
    {
        var monitor = new HealthMonitor();
        var before = monitor.Current;

        monitor.Report(ComponentNames.Onvif, ComponentState.Running);

        Assert.Equal(ComponentState.Stopped, before.Components[ComponentNames.Onvif].State);
        Assert.Equal(ComponentState.Running, monitor.Current.Components[ComponentNames.Onvif].State);
    }
}

public class NetworkAddressResolverTests
{
    [Fact]
    public void GetLocalAddressFor_ResolvesLoopbackToLoopback()
    {
        Assert.Equal(IPAddress.Loopback, NetworkAddressResolver.GetLocalAddressFor(IPAddress.Loopback));
    }

    [Fact]
    public void GetLocalAddressFor_IgnoresIPv6()
    {
        // Only IPv4 endpoints are published in the MVP.
        Assert.Null(NetworkAddressResolver.GetLocalAddressFor(IPAddress.IPv6Any));
    }

    [Fact]
    public void GetPreferredLocalAddress_NeverReturnsNull()
    {
        Assert.NotNull(NetworkAddressResolver.GetPreferredLocalAddress());
    }

    [Fact]
    public void EnumerateInterfaces_ExcludesLoopbackAndSortsVirtualAdaptersLast()
    {
        var interfaces = NetworkAddressResolver.EnumerateInterfaces();

        Assert.DoesNotContain(interfaces, i => IPAddress.IsLoopback(i.Address));

        var firstVirtual = interfaces.ToList().FindIndex(i => i.IsLikelyVirtual);
        var lastPhysical = interfaces.ToList().FindLastIndex(i => !i.IsLikelyVirtual);

        if (firstVirtual >= 0 && lastPhysical >= 0)
        {
            Assert.True(firstVirtual > lastPhysical, "Virtual adapters must sort after physical ones.");
        }
    }
}

public class PasswordProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        const string password = "Screen2VMS!secret";

        Assert.Equal(password, PasswordProtector.Unprotect(PasswordProtector.Protect(password)));
    }

    [Fact]
    public void Protect_DoesNotLeaveThePasswordReadable()
    {
        const string password = "PlainTextPassword";

        Assert.DoesNotContain(password, PasswordProtector.Protect(password), StringComparison.Ordinal);
    }

    [Fact]
    public void Unprotect_TreatsUnreadableBlobsAsNoPassword()
    {
        // A config copied from another machine will not decrypt. That must ask
        // for a new password, not crash on startup.
        Assert.Null(PasswordProtector.Unprotect(null));
        Assert.Null(PasswordProtector.Unprotect(string.Empty));
        Assert.Null(PasswordProtector.Unprotect("not-base64!"));
        Assert.Null(PasswordProtector.Unprotect(Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])));
    }

    [Fact]
    public void GeneratePassword_IsUniqueAndFreeOfAmbiguousCharacters()
    {
        var generated = Enumerable.Range(0, 200).Select(_ => PasswordProtector.GeneratePassword()).ToList();

        Assert.Equal(generated.Count, generated.Distinct().Count());
        Assert.All(generated, p => Assert.Equal(16, p.Length));

        // These get misread when typed from the screen into a VMS dialog.
        Assert.All(generated, p => Assert.DoesNotContain(p, c => c is 'O' or '0' or 'l' or 'I' or '1'));
    }
}

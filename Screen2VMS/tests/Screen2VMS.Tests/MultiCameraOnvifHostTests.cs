using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Onvif;

namespace Screen2VMS.Tests;

/// <summary>
/// Proves or disproves the assumption the whole multi-camera design rests on:
/// that two <see cref="OnvifServiceHost"/> instances - each with its own
/// WS-Discovery responder on UDP 3702 - can run in the same process at once.
/// </summary>
/// <remarks>
/// <para>
/// Every other camera adds only a second full pipeline running on distinct
/// TCP ports, which is unremarkable. WS-Discovery is the one shared resource:
/// each host's <c>AddOnvifDiscovery</c> binds the same UDP multicast port,
/// which is exactly how two real multi-sensor ONVIF devices coexist on one
/// LAN, but nothing in this codebase had exercised two hosts in one process
/// before this change.
/// </para>
/// <para>
/// If this test throws (an address-in-use failure on the second host's
/// discovery socket, most likely), the in-process design in CLAUDE.md's
/// multi-camera section does not hold and each additional camera needs its
/// own OS process instead - do not try to patch around it here.
/// </para>
/// </remarks>
public class MultiCameraOnvifHostTests
{
    [Fact]
    public void TwoHosts_WithDistinctPortsAndIdentities_BothStartConcurrently()
    {
        var rtspPortA = FindFreePort();
        var onvifPortA = FindFreePort();
        var rtspPortB = FindFreePort();
        var onvifPortB = FindFreePort();

        using var hostA = CreateHost(onvifPortA, rtspPortA, serial: "S2V-CAMERA0000A", mac: "02AAAAAAAAAA");
        using var hostB = CreateHost(onvifPortB, rtspPortB, serial: "S2V-CAMERA0000B", mac: "02BBBBBBBBBB");

        hostA.Start(onvifPortA);
        hostB.Start(onvifPortB);

        Assert.True(hostA.IsRunning);
        Assert.True(hostB.IsRunning);
        Assert.NotEqual(hostA.DeviceServiceUri, hostB.DeviceServiceUri);

        hostB.Stop();
        hostA.Stop();

        Assert.False(hostA.IsRunning);
        Assert.False(hostB.IsRunning);
    }

    private static OnvifServiceHost CreateHost(int onvifPort, int rtspPort, string serial, string mac) =>
        new(
            new StubDeviceContext(serial, mac, rtspPort),
            new OnvifHostOptions
            {
                Port = onvifPort,
                UserName = "admin",
                Password = "test-password",
                FallbackAddress = "127.0.0.1",
                RtspPort = rtspPort,
                RtspPath = "/live",
            },
            NullLoggerFactory.Instance);

    /// <summary>An ephemeral port the operating system has just confirmed is free.</summary>
    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal device context; this test exercises host coexistence only.</summary>
    private sealed class StubDeviceContext(string serial, string mac, int rtspPort) : IOnvifDeviceContext
    {
        public OnvifDeviceInfo DeviceInfo { get; } = new()
        {
            SerialNumber = serial,
            MacAddress = mac,
        };

        public OnvifVideoConfiguration Video { get; private set; } = new();

        public bool IsStreaming => false;

        public string GetStreamUri(IPAddress client) => $"rtsp://127.0.0.1:{rtspPort}/live";

        public string GetSnapshotUri(IPAddress client) => "http://127.0.0.1/onvif/snapshot";

        public byte[]? CaptureSnapshotJpeg() => null;

        public void RequestSynchronizationPoint()
        {
        }

        public void ApplyVideoConfiguration(OnvifVideoConfiguration configuration) => Video = configuration;
    }
}

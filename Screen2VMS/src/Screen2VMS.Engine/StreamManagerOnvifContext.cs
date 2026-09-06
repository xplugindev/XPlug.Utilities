using System.Net;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Networking;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Core.Streaming;

namespace Screen2VMS.Engine;

/// <summary>
/// Presents the running pipeline to the ONVIF layer (spec 44).
/// </summary>
/// <remarks>
/// This is the only place the two halves meet. ONVIF asks for metadata, URIs
/// and the occasional snapshot; it never learns that the source is a webcam,
/// and the pipeline never learns that a VMS exists.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class StreamManagerOnvifContext : IOnvifDeviceContext
{
    private readonly IStreamManager streamManager;
    private readonly ILogger logger;
    private readonly Func<int> onvifPort;

    public StreamManagerOnvifContext(
        IStreamManager streamManager,
        OnvifDeviceInfo deviceInfo,
        Func<int> onvifPort,
        ILogger<StreamManagerOnvifContext>? logger = null)
    {
        this.streamManager = streamManager;
        this.onvifPort = onvifPort;
        this.logger = logger ?? NullLogger<StreamManagerOnvifContext>.Instance;
        DeviceInfo = deviceInfo;
    }

    public OnvifDeviceInfo DeviceInfo { get; }

    public bool IsStreaming => streamManager.RtspServer.IsRunning;

    public OnvifVideoConfiguration Video
    {
        get
        {
            var encoder = streamManager.Encoder;
            var settings = streamManager.Settings;

            // Report what is actually running where possible; a profile that
            // disagrees with the stream is rejected by both target platforms.
            if (encoder is not null)
            {
                return new OnvifVideoConfiguration
                {
                    Width = encoder.Width,
                    Height = encoder.Height,
                    FrameRate = encoder.FrameRate,
                    BitrateKbps = encoder.BitrateKbps,
                    GopLength = settings?.Encoder.GopLength ?? 30,
                    Profile = settings?.Encoder.Profile ?? 77,
                };
            }

            var configured = settings?.Encoder;

            return new OnvifVideoConfiguration
            {
                Width = configured?.Width ?? 1920,
                Height = configured?.Height ?? 1080,
                FrameRate = configured?.FrameRate ?? 30,
                BitrateKbps = configured?.BitrateKbps ?? 4000,
                GopLength = configured?.GopLength ?? 30,
                Profile = configured?.Profile ?? 77,
            };
        }
    }

    public string GetStreamUri(IPAddress client)
    {
        var host = ResolveHostFor(client);
        var server = streamManager.RtspServer;
        var port = server.Port > 0 ? server.Port : streamManager.Settings?.RtspPort ?? 8554;
        var path = string.IsNullOrEmpty(server.Path) ? streamManager.Settings?.RtspPath ?? "/live" : server.Path;

        return $"rtsp://{host}:{port}{path}";
    }

    public string GetSnapshotUri(IPAddress client) =>
        $"http://{ResolveHostFor(client)}:{onvifPort()}/onvif/snapshot";

    public byte[]? CaptureSnapshotJpeg()
    {
        var camera = streamManager.Camera;
        if (camera is null)
        {
            return null;
        }

        try
        {
            var snapshot = camera.GetLatestFrame();
            return snapshot is null ? null : SnapshotEncoder.ToJpeg(snapshot);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not produce a snapshot.");
            return null;
        }
    }

    public void ApplyVideoConfiguration(OnvifVideoConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = streamManager.Settings;
        if (settings is null)
        {
            return;
        }

        // Only settings that can be changed without renegotiating capture are
        // applied. Resolution and frame rate come from the camera, so a VMS
        // asking for something else is answered with what it is going to get
        // rather than with a fault.
        var current = settings.Encoder;
        var bitrate = Math.Clamp(configuration.BitrateKbps, 256, 20000);
        var gop = Math.Clamp(configuration.GopLength, 1, 300);

        if (current.BitrateKbps == bitrate && current.GopLength == gop)
        {
            return;
        }

        logger.LogInformation(
            "Storing encoder settings written by the VMS: {Bitrate} kbps, GOP {Gop}. " +
            "They take effect on the next restart.",
            bitrate,
            gop);

        // Deliberately not restarting here. A VMS writes these while it is
        // adding the unit, and dropping the stream at that moment aborts the
        // add. The settings are accepted and applied the next time the
        // pipeline starts.
        streamManager.UpdateSettings(settings with
        {
            Encoder = current with { BitrateKbps = bitrate, GopLength = gop },
        });
    }

    /// <summary>
    /// The local address that routes to <paramref name="client"/>.
    /// </summary>
    /// <remarks>
    /// Resolved per request rather than fixed, so a VMS on another subnet is
    /// given an address it can reach (spec 23, 26).
    /// </remarks>
    private static string ResolveHostFor(IPAddress client)
    {
        var local = NetworkAddressResolver.GetLocalAddressFor(client);
        return (local ?? NetworkAddressResolver.GetPreferredLocalAddress()).ToString();
    }
}

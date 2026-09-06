using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Screen2VMS.Core.Onvif;
using SharpOnvifServer.Media;

namespace Screen2VMS.Onvif;

/// <summary>
/// The ONVIF media service: one ready-to-use profile and the URIs to reach it
/// (spec 21, 22, 23).
/// </summary>
/// <remarks>
/// Profile S requires a device to expose at least one profile that needs no
/// configuration before it can be streamed, which is exactly what
/// <see cref="ProfileToken"/> is.
/// </remarks>
public class Screen2VmsMediaService : MediaBase
{
    /// <summary>Token of the single media profile (spec 12, 21).</summary>
    public const string ProfileToken = "Profile_1";

    public const string ProfileName = "MainStream";

    public const string VideoSourceToken = "VideoSource_1";

    public const string VideoSourceConfigurationToken = "VideoSourceConfig_1";

    public const string VideoEncoderConfigurationToken = "VideoEncoder_1";

    private readonly IOnvifDeviceContext context;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ILogger<Screen2VmsMediaService> logger;

    public Screen2VmsMediaService(
        IOnvifDeviceContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<Screen2VmsMediaService> logger)
    {
        this.context = context;
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    public override Capabilities GetServiceCapabilities() => new()
    {
        SnapshotUri = true,
        SnapshotUriSpecified = true,
        Rotation = false,
        RotationSpecified = true,
        VideoSourceMode = false,
        VideoSourceModeSpecified = true,
        OSD = false,
        OSDSpecified = true,
        ProfileCapabilities = new ProfileCapabilities
        {
            MaximumNumberOfProfiles = 1,
            MaximumNumberOfProfilesSpecified = true,
        },
        StreamingCapabilities = new StreamingCapabilities
        {
            RTP_RTSP_TCP = true,
            RTP_RTSP_TCPSpecified = true,
            RTP_TCP = true,
            RTP_TCPSpecified = true,
            RTPMulticast = false,
            RTPMulticastSpecified = true,
            NoRTSPStreaming = false,
            NoRTSPStreamingSpecified = true,
            NonAggregateControl = false,
            NonAggregateControlSpecified = true,
        },
    };

    public override GetProfilesResponse GetProfiles(GetProfilesRequest request)
    {
        logger.LogDebug("OnvifRequest: GetProfiles");
        return new GetProfilesResponse { Profiles = [BuildProfile()] };
    }

    public override Profile GetProfile(string ProfileToken)
    {
        logger.LogDebug("OnvifRequest: GetProfile {Token}", ProfileToken);
        EnsureProfileToken(ProfileToken);
        return BuildProfile();
    }

    /// <summary>
    /// Returns the RTSP URI for this profile.
    /// </summary>
    /// <remarks>
    /// The address is resolved against the caller, so a VMS on a different
    /// subnet or reaching us across a VPN is told an address it can actually
    /// open. Returning a fixed or first-found address is the usual cause of a
    /// unit that adds successfully and then shows no video.
    /// </remarks>
    public override MediaUri GetStreamUri(StreamSetup StreamSetup, string ProfileToken)
    {
        EnsureProfileToken(ProfileToken);

        var client = GetClientAddress();
        var uri = context.GetStreamUri(client);

        logger.LogInformation("OnvifRequest: GetStreamUri from {Client} -> {Uri}", client, uri);

        return new MediaUri
        {
            Uri = uri,
            InvalidAfterConnect = false,
            InvalidAfterReboot = false,
            Timeout = "PT60S",
        };
    }

    public override MediaUri GetSnapshotUri(string ProfileToken)
    {
        EnsureProfileToken(ProfileToken);

        var client = GetClientAddress();
        var uri = context.GetSnapshotUri(client);

        logger.LogInformation("OnvifRequest: GetSnapshotUri from {Client} -> {Uri}", client, uri);

        return new MediaUri
        {
            Uri = uri,
            InvalidAfterConnect = false,
            InvalidAfterReboot = false,
            Timeout = "PT60S",
        };
    }

    public override GetVideoSourcesResponse GetVideoSources(GetVideoSourcesRequest request)
    {
        var video = context.Video;

        return new GetVideoSourcesResponse
        {
            VideoSources =
            [
                new VideoSource
                {
                    token = VideoSourceToken,
                    Framerate = (float)video.FrameRate,
                    Resolution = new VideoResolution { Width = video.Width, Height = video.Height },
                },
            ],
        };
    }

    public override GetVideoSourceConfigurationsResponse GetVideoSourceConfigurations(
        GetVideoSourceConfigurationsRequest request) =>
        new() { Configurations = [BuildVideoSourceConfiguration()] };

    public override VideoSourceConfiguration GetVideoSourceConfiguration(string ConfigurationToken) =>
        BuildVideoSourceConfiguration();

    /// <summary>
    /// Describes how the source's cropping bounds may be adjusted.
    /// </summary>
    /// <remarks>
    /// Cropping is not supported, so every range is pinned to the full frame.
    /// The operation still has to answer: a fault here shows up as a broken
    /// device while a VMS is enumerating configuration options.
    /// </remarks>
    public override VideoSourceConfigurationOptions GetVideoSourceConfigurationOptions(
        string ConfigurationToken,
        string ProfileToken)
    {
        var video = context.Video;

        return new VideoSourceConfigurationOptions
        {
            MaximumNumberOfProfiles = 1,
            MaximumNumberOfProfilesSpecified = true,
            VideoSourceTokensAvailable = [VideoSourceToken],
            BoundsRange = new IntRectangleRange
            {
                XRange = new IntRange { Min = 0, Max = 0 },
                YRange = new IntRange { Min = 0, Max = 0 },
                WidthRange = new IntRange { Min = video.Width, Max = video.Width },
                HeightRange = new IntRange { Min = video.Height, Max = video.Height },
            },
        };
    }

    public override GetVideoEncoderConfigurationsResponse GetVideoEncoderConfigurations(
        GetVideoEncoderConfigurationsRequest request) =>
        new() { Configurations = [BuildVideoEncoderConfiguration()] };

    public override VideoEncoderConfiguration GetVideoEncoderConfiguration(string ConfigurationToken) =>
        BuildVideoEncoderConfiguration();

    public override GetCompatibleVideoEncoderConfigurationsResponse GetCompatibleVideoEncoderConfigurations(
        GetCompatibleVideoEncoderConfigurationsRequest request) =>
        new() { Configurations = [BuildVideoEncoderConfiguration()] };

    public override GetCompatibleVideoSourceConfigurationsResponse GetCompatibleVideoSourceConfigurations(
        GetCompatibleVideoSourceConfigurationsRequest request) =>
        new() { Configurations = [BuildVideoSourceConfiguration()] };

    /// <summary>
    /// Describes what the encoder can be asked for.
    /// </summary>
    /// <remarks>
    /// Only the resolution actually running is advertised. Offering a list the
    /// camera cannot really deliver invites a VMS to select one and then blame
    /// the device when the stream does not match.
    /// </remarks>
    public override VideoEncoderConfigurationOptions GetVideoEncoderConfigurationOptions(
        string ConfigurationToken,
        string ProfileToken)
    {
        var video = context.Video;

        return new VideoEncoderConfigurationOptions
        {
            QualityRange = new IntRange { Min = 1, Max = 10 },
            H264 = new H264Options
            {
                ResolutionsAvailable =
                [
                    new VideoResolution { Width = video.Width, Height = video.Height },
                ],
                GovLengthRange = new IntRange { Min = 1, Max = 120 },
                FrameRateRange = new IntRange { Min = 1, Max = (int)Math.Ceiling(video.FrameRate) },
                EncodingIntervalRange = new IntRange { Min = 1, Max = 1 },
                H264ProfilesSupported = [H264Profile.Baseline, H264Profile.Main],
            },
        };
    }

    /// <summary>
    /// Accepts encoder settings written by a VMS.
    /// </summary>
    /// <remarks>
    /// XProtect writes this during "add hardware". Values are clamped to what
    /// the pipeline can do rather than rejected, because a SOAP fault here
    /// aborts the whole add and leaves the operator with no camera.
    /// </remarks>
    public override void SetVideoEncoderConfiguration(
        VideoEncoderConfiguration Configuration,
        bool ForcePersistence)
    {
        if (Configuration is null)
        {
            return;
        }

        var current = context.Video;

        var updated = current with
        {
            Width = Configuration.Resolution?.Width > 0 ? Configuration.Resolution.Width : current.Width,
            Height = Configuration.Resolution?.Height > 0 ? Configuration.Resolution.Height : current.Height,
            FrameRate = Configuration.RateControl?.FrameRateLimit > 0
                ? Configuration.RateControl.FrameRateLimit
                : current.FrameRate,
            BitrateKbps = Configuration.RateControl?.BitrateLimit > 0
                ? Configuration.RateControl.BitrateLimit
                : current.BitrateKbps,
            GopLength = Configuration.H264?.GovLength > 0 ? Configuration.H264.GovLength : current.GopLength,
            Profile = Configuration.H264 is null ? current.Profile : ToProfileNumber(Configuration.H264.H264Profile),
        };

        logger.LogInformation(
            "OnvifRequest: SetVideoEncoderConfiguration {Width}x{Height} @ {Fps} fps, {Bitrate} kbps, GOP {Gop}",
            updated.Width,
            updated.Height,
            updated.FrameRate,
            updated.BitrateKbps,
            updated.GopLength);

        context.ApplyVideoConfiguration(updated);
    }

    private Profile BuildProfile() => new()
    {
        token = ProfileToken,
        Name = ProfileName,

        // Profile S requires at least one profile that cannot be deleted and is
        // ready to stream without further configuration.
        @fixed = true,
        fixedSpecified = true,
        VideoSourceConfiguration = BuildVideoSourceConfiguration(),
        VideoEncoderConfiguration = BuildVideoEncoderConfiguration(),
    };

    private VideoSourceConfiguration BuildVideoSourceConfiguration()
    {
        var video = context.Video;

        return new VideoSourceConfiguration
        {
            token = VideoSourceConfigurationToken,
            Name = "VideoSourceConfig",
            UseCount = 1,
            SourceToken = VideoSourceToken,
            Bounds = new IntRectangle { x = 0, y = 0, width = video.Width, height = video.Height },
        };
    }

    private VideoEncoderConfiguration BuildVideoEncoderConfiguration()
    {
        var video = context.Video;

        return new VideoEncoderConfiguration
        {
            token = VideoEncoderConfigurationToken,
            Name = "VideoEncoderConfig",
            UseCount = 1,
            Encoding = VideoEncoding.H264,
            Resolution = new VideoResolution { Width = video.Width, Height = video.Height },
            Quality = 5,
            RateControl = new VideoRateControl
            {
                FrameRateLimit = (int)Math.Round(video.FrameRate),
                EncodingInterval = 1,
                BitrateLimit = video.BitrateKbps,
            },
            H264 = new H264Configuration
            {
                GovLength = video.GopLength,
                H264Profile = video.Profile >= 100 ? H264Profile.High
                    : video.Profile >= 77 ? H264Profile.Main
                    : H264Profile.Baseline,
            },
            SessionTimeout = "PT60S",
        };
    }

    private static int ToProfileNumber(H264Profile profile) => profile switch
    {
        H264Profile.High => 100,
        H264Profile.Main => 77,
        _ => 66,
    };

    /// <summary>The address the request came from, used to resolve reachable URIs.</summary>
    private System.Net.IPAddress GetClientAddress()
    {
        var remote = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        return remote is null ? System.Net.IPAddress.Loopback : remote.MapToIPv4();
    }

    private static void EnsureProfileToken(string profileToken)
    {
        if (!string.IsNullOrEmpty(profileToken)
            && !string.Equals(profileToken, ProfileToken, StringComparison.Ordinal))
        {
            SharpOnvifServer.OnvifErrors.ReturnSenderInvalidArg();
        }
    }
}

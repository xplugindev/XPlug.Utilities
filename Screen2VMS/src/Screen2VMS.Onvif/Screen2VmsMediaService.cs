using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Screen2VMS.Core.Onvif;
using SharpOnvifServer;
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

    /// <summary>
    /// Forces a key frame, which is what ONVIF calls a synchronisation point.
    /// </summary>
    /// <remarks>
    /// Genetec calls this whenever it (re)starts a stream. Faulting here leaves
    /// the live view black until the next GOP boundary.
    /// </remarks>
    public override void SetSynchronizationPoint(string ProfileToken)
    {
        EnsureProfileToken(ProfileToken);
        logger.LogDebug("OnvifRequest: SetSynchronizationPoint");
        context.RequestSynchronizationPoint();
    }

    /// <summary>
    /// How many encoder instances the device guarantees.
    /// </summary>
    /// <remarks>
    /// Exactly one: a single camera feeding a single encoder that every client
    /// shares (spec 33).
    /// </remarks>
    public override GetGuaranteedNumberOfVideoEncoderInstancesResponse GetGuaranteedNumberOfVideoEncoderInstances(
        GetGuaranteedNumberOfVideoEncoderInstancesRequest request) =>
        new()
        {
            TotalNumber = 1,
            H264 = 1,
            JPEG = 0,
            MPEG4 = 0,
        };

    /// <summary>
    /// Refuses to create a second profile, with the fault ONVIF defines for it.
    /// </summary>
    /// <remarks>
    /// The device has one fixed profile (spec 12). Genetec tries to create its
    /// own and handles a proper MaxNVTProfiles fault by falling back to the
    /// existing one - but a generic internal error tells it nothing and aborts
    /// the enrolment.
    /// </remarks>
    public override Profile CreateProfile(string Name, string Token)
    {
        logger.LogDebug("OnvifRequest: CreateProfile '{Name}' refused; the device has one fixed profile.", Name);

        OnvifErrors.ReturnSenderError(
            "The maximum number of media profiles is already in use.",
            "MaxNVTProfiles",
            OnvifErrorNamespace,
            System.Net.HttpStatusCode.BadRequest);

        // ReturnSenderError throws; this only satisfies the compiler.
        return BuildProfile();
    }

    /// <summary>Deleting the fixed profile is not permitted, per ONVIF.</summary>
    public override void DeleteProfile(string ProfileToken)
    {
        logger.LogDebug("OnvifRequest: DeleteProfile refused; the profile is fixed.");

        OnvifErrors.ReturnSenderError(
            "The media profile is fixed and cannot be deleted.",
            "DeletionOfFixedProfile",
            OnvifErrorNamespace,
            System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Accepts a configuration the profile already carries.
    /// </summary>
    /// <remarks>
    /// The profile is fixed, so the only configuration that can be added is the
    /// one already in it. Saying yes to that is honest and lets a VMS finish
    /// setting the profile up; anything else is refused properly.
    /// </remarks>
    public override void AddVideoSourceConfiguration(string ProfileToken, string ConfigurationToken)
    {
        EnsureProfileToken(ProfileToken);
        AcceptExistingConfiguration(ConfigurationToken, VideoSourceConfigurationToken, "video source");
    }

    public override void AddVideoEncoderConfiguration(string ProfileToken, string ConfigurationToken)
    {
        EnsureProfileToken(ProfileToken);
        AcceptExistingConfiguration(ConfigurationToken, VideoEncoderConfigurationToken, "video encoder");
    }

    // --- Things this device does not have -----------------------------------
    //
    // Audio, analytics, metadata and OSD are all out of scope (spec 3). A VMS
    // enumerates them anyway while adding a unit, and there is a world of
    // difference between "none" and an internal error: the first is a normal
    // camera with no microphone, the second aborts the enrolment. Every one of
    // these therefore answers with an empty collection.

    public override AudioEncoderConfigurationOptions GetAudioEncoderConfigurationOptions(
        string ConfigurationToken,
        string ProfileToken) =>
        new() { Options = [] };

    public override AudioDecoderConfigurationOptions GetAudioDecoderConfigurationOptions(
        string ConfigurationToken,
        string ProfileToken) =>
        new();

    public override GetAudioOutputsResponse GetAudioOutputs(GetAudioOutputsRequest request) =>
        new() { AudioOutputs = [] };

    public override GetAudioSourcesResponse GetAudioSources(GetAudioSourcesRequest request) =>
        new() { AudioSources = [] };

    public override GetAudioSourceConfigurationsResponse GetAudioSourceConfigurations(
        GetAudioSourceConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetAudioEncoderConfigurationsResponse GetAudioEncoderConfigurations(
        GetAudioEncoderConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetAudioDecoderConfigurationsResponse GetAudioDecoderConfigurations(
        GetAudioDecoderConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetAudioOutputConfigurationsResponse GetAudioOutputConfigurations(
        GetAudioOutputConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetCompatibleAudioSourceConfigurationsResponse GetCompatibleAudioSourceConfigurations(
        GetCompatibleAudioSourceConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetCompatibleAudioEncoderConfigurationsResponse GetCompatibleAudioEncoderConfigurations(
        GetCompatibleAudioEncoderConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetMetadataConfigurationsResponse GetMetadataConfigurations(
        GetMetadataConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetCompatibleMetadataConfigurationsResponse GetCompatibleMetadataConfigurations(
        GetCompatibleMetadataConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetVideoAnalyticsConfigurationsResponse GetVideoAnalyticsConfigurations(
        GetVideoAnalyticsConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetCompatibleVideoAnalyticsConfigurationsResponse GetCompatibleVideoAnalyticsConfigurations(
        GetCompatibleVideoAnalyticsConfigurationsRequest request) =>
        new() { Configurations = [] };

    public override GetOSDsResponse GetOSDs(GetOSDsRequest request) =>
        new() { OSDs = [] };

    /// <summary>The camera has one mode: whatever the capture pipeline negotiated.</summary>
    public override GetVideoSourceModesResponse GetVideoSourceModes(GetVideoSourceModesRequest request) =>
        new() { VideoSourceModes = [] };

    private void AcceptExistingConfiguration(string requested, string existing, string description)
    {
        if (string.Equals(requested, existing, StringComparison.Ordinal))
        {
            logger.LogDebug("OnvifRequest: Add{Description} configuration is already in the profile.", description);
            return;
        }

        OnvifErrors.ReturnSenderError(
            $"The only {description} configuration is '{existing}'.",
            "ConfigurationConflict",
            OnvifErrorNamespace,
            System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>Namespace ONVIF fault subcodes live in.</summary>
    private const string OnvifErrorNamespace = "http://www.onvif.org/ver10/error";

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
            OnvifErrors.ReturnSenderInvalidArg();
        }
    }
}

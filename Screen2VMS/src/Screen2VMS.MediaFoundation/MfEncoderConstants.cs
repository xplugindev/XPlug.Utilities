namespace Screen2VMS.MediaFoundation;

/// <summary>
/// GUIDs and flags used to drive the H.264 encoder MFT.
/// </summary>
internal static class MfEncoderConstants
{
    // MFTEnumEx.
    internal static readonly Guid TransformCategoryVideoEncoder =
        new("f79eac7d-e545-4387-bdee-d647d7bde42a");

    internal const uint EnumFlagSyncMft = 0x00000001;
    internal const uint EnumFlagAsyncMft = 0x00000002;
    internal const uint EnumFlagHardware = 0x00000004;
    internal const uint EnumFlagLocalMft = 0x00000008;
    internal const uint EnumFlagTranscodeOnly = 0x00000020;
    internal const uint EnumFlagSortAndFilter = 0x00000040;

    /// <summary>Present on an MFT that lives in a hardware driver.</summary>
    internal static readonly Guid TransformEnumHardwareUrlAttribute =
        new("2fb866ac-b078-4942-ab6c-003d05cda674");

    internal static readonly Guid TransformFriendlyNameAttribute =
        new("314ffbae-5b41-4c95-9c19-4e7d586face3");

    // Encoder media type attributes.
    internal static readonly Guid MtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    internal static readonly Guid MtMpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    internal static readonly Guid MtMpeg2Level = new("96f66574-11c5-4015-8666-bff516436da7");
    internal static readonly Guid MtMaxKeyframeSpacing = new("c16eb52b-73a1-476f-8d62-839d6a020652");

    /// <summary>Carries SPS and PPS in Annex-B form once the output type is set.</summary>
    internal static readonly Guid MtMpegSequenceHeader = new("3c036de7-3ad0-4c9e-9216-ee6d6ac21cb3");

    internal static readonly Guid MtAllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");

    internal static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");

    /// <summary>Set on a sample that is an IDR.</summary>
    internal static readonly Guid SampleExtensionCleanPoint =
        new("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");

    // ICodecAPI properties.
    internal static readonly Guid AVEncVideoForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");
    internal static readonly Guid AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    internal static readonly Guid AVEncCommonMeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    internal static readonly Guid AVEncCommonQualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
    internal static readonly Guid AVEncMPVGOPSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    internal static readonly Guid AVEncVideoEncodeFrameTypeQP = new("aa70b610-e03f-450c-ad07-07314e639ce7");
    internal static readonly Guid AVLowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    internal static readonly Guid AVEncCommonLowLatency = new("9d3ecd55-89e8-490a-970a-0c9548d5a56e");

    /// <summary>eAVEncCommonRateControlMode_CBR.</summary>
    internal const uint RateControlModeCbr = 0;

    // H.264 profile numbers as used by MF_MT_MPEG2_PROFILE.
    internal const uint H264ProfileBaseline = 66;
    internal const uint H264ProfileMain = 77;
    internal const uint H264ProfileHigh = 100;

    // MFT_MESSAGE_TYPE.
    internal const int MessageCommandFlush = 0x00000000;
    internal const int MessageCommandDrain = 0x00000001;
    internal const int MessageNotifyBeginStreaming = 0x10000000;
    internal const int MessageNotifyEndStreaming = 0x10000001;
    internal const int MessageNotifyEndOfStream = 0x10000002;
    internal const int MessageNotifyStartOfStream = 0x10000003;

    // ProcessOutput.
    internal const uint OutputStatusSampleReady = 0x00000001;
    internal const uint OutputStreamProvidesSamples = 0x00000100;
    internal const uint OutputStreamCanProvideSamples = 0x00000200;

    internal const int MfETransformNeedMoreInput = unchecked((int)0xC00D6D72);
    internal const int MfETransformStreamChange = unchecked((int)0xC00D6D61);
    internal const int MfETransformTypeNotSet = unchecked((int)0xC00D6D60);

    /// <summary>MFVideoInterlace_Progressive.</summary>
    internal const uint InterlaceModeProgressive = 2;

    /// <summary>One second in Media Foundation's 100-nanosecond units.</summary>
    internal const long TicksPerSecond = 10_000_000;
}

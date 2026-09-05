using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Camera.Interop;

/// <summary>
/// GUIDs and magic numbers from the Media Foundation headers.
/// </summary>
/// <remarks>
/// Media subtype GUIDs are FOURCCs promoted into the MF GUID space: the four
/// character codes become Data1 in little-endian order, with a fixed suffix.
/// "NV12" is 4E 56 31 32, so Data1 is 0x3231564E.
/// </remarks>
internal static class MfConstants
{
    // MFStartup: (MF_SDK_VERSION << 16) | MF_API_VERSION.
    internal const int MfVersion = 0x00020070;
    internal const int MfStartupFull = 0;
    internal const int MfStartupLite = 1;

    // Virtual stream indices accepted by IMFSourceReader.
    internal const uint FirstVideoStream = 0xFFFFFFFC;
    internal const uint InvalidStreamIndex = 0xFFFFFFFF;

    // IMFSourceReader::ReadSample stream flags.
    internal const uint StreamFlagError = 0x00000001;
    internal const uint StreamFlagEndOfStream = 0x00000002;
    internal const uint StreamFlagNewStream = 0x00000004;
    internal const uint StreamFlagNativeMediaTypeChanged = 0x00000010;
    internal const uint StreamFlagCurrentMediaTypeChanged = 0x00000020;
    internal const uint StreamFlagStreamTick = 0x00000100;

    // Device enumeration.
    internal static readonly Guid DevSourceAttributeSourceType =
        new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");

    internal static readonly Guid DevSourceAttributeSourceTypeVidCap =
        new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");

    internal static readonly Guid DevSourceAttributeFriendlyName =
        new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");

    internal static readonly Guid DevSourceAttributeVidCapSymbolicLink =
        new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");

    // Media type attributes.
    internal static readonly Guid MtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    internal static readonly Guid MtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid MtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    internal static readonly Guid MtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    internal static readonly Guid MtFrameRateRangeMin = new("d2e7558c-dc1f-403f-9a72-d28bb1eb3b5e");
    internal static readonly Guid MtFrameRateRangeMax = new("e3371d41-b4cf-4a05-bd4e-20b88bb2c4d6");
    internal static readonly Guid MtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    internal static readonly Guid MtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    internal static readonly Guid MtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");

    // Source reader attributes.
    internal static readonly Guid SourceReaderEnableAdvancedVideoProcessing =
        new("0f81da2c-b537-4672-a8b2-a681b17307a3");

    internal static readonly Guid SourceReaderEnableVideoProcessing =
        new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

    internal static readonly Guid SourceReaderDisableDxva =
        new("aa456cfd-3943-4a1e-a77d-1838c0ea2e35");

    // Major types.
    internal static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");

    // Video subtypes.
    internal static readonly Guid VideoFormatNv12 = new("3231564e-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatYuy2 = new("32595559-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatUyvy = new("59565955-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatI420 = new("30323449-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatIyuv = new("56555949-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatYv12 = new("32315659-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatMjpg = new("47504a4d-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatArgb32 = new("00000015-0000-0010-8000-00aa00389b71");
    internal static readonly Guid VideoFormatRgb24 = new("00000014-0000-0010-8000-00aa00389b71");

    /// <summary>Maps an MF subtype onto the pipeline's format enum.</summary>
    internal static VideoPixelFormat ToPixelFormat(Guid subtype)
    {
        if (subtype == VideoFormatNv12) return VideoPixelFormat.Nv12;
        if (subtype == VideoFormatYuy2) return VideoPixelFormat.Yuy2;
        if (subtype == VideoFormatUyvy) return VideoPixelFormat.Uyvy;
        if (subtype == VideoFormatI420 || subtype == VideoFormatIyuv || subtype == VideoFormatYv12) return VideoPixelFormat.I420;
        if (subtype == VideoFormatMjpg) return VideoPixelFormat.Mjpg;
        if (subtype == VideoFormatH264) return VideoPixelFormat.H264;
        if (subtype == VideoFormatRgb32 || subtype == VideoFormatArgb32) return VideoPixelFormat.Bgra32;
        if (subtype == VideoFormatRgb24) return VideoPixelFormat.Bgr24;
        return VideoPixelFormat.Unknown;
    }
}

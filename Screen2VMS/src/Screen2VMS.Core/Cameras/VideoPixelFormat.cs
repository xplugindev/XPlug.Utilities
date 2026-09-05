namespace Screen2VMS.Core.Cameras;

/// <summary>
/// Backend-neutral pixel formats. Deliberately small: only the formats a UVC
/// webcam realistically advertises, plus the two we consume internally.
/// </summary>
public enum VideoPixelFormat
{
    Unknown = 0,

    /// <summary>Planar 4:2:0, Y plane followed by interleaved UV. The pipeline's internal format.</summary>
    Nv12,

    /// <summary>Planar 4:2:0 with separate U and V planes.</summary>
    I420,

    /// <summary>Packed 4:2:2.</summary>
    Yuy2,

    /// <summary>Packed 4:2:2, U first.</summary>
    Uyvy,

    /// <summary>32-bit BGRA. What WPF wants for preview.</summary>
    Bgra32,

    /// <summary>24-bit BGR.</summary>
    Bgr24,

    /// <summary>Motion JPEG. Common as the only 1080p30 mode on cheap webcams.</summary>
    Mjpg,

    /// <summary>Some UVC cameras emit H.264 directly. Not consumed yet.</summary>
    H264,
}

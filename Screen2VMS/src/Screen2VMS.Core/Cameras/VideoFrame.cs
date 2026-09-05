namespace Screen2VMS.Core.Cameras;

/// <summary>
/// One captured frame, borrowed for the duration of a single
/// <see cref="IVideoFrameSink.OnFrame"/> call.
/// </summary>
/// <remarks>
/// This is a ref struct on purpose: it wraps memory the capture backend still
/// owns (spec 10 - no unnecessary copies, and leave room for a GPU path later).
/// The memory is valid only inside the callback. A sink that needs to keep the
/// pixels must copy them out before returning.
/// </remarks>
public readonly ref struct VideoFrame
{
    public VideoFrame(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int stride,
        VideoPixelFormat format,
        TimeSpan timestamp)
    {
        Data = data;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        Timestamp = timestamp;
    }

    /// <summary>Raw pixels. Valid only for the lifetime of the callback that received this frame.</summary>
    public ReadOnlySpan<byte> Data { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row of the first (or only) plane. May exceed Width for aligned buffers.</summary>
    public int Stride { get; }

    public VideoPixelFormat Format { get; }

    /// <summary>Presentation time relative to the start of capture.</summary>
    public TimeSpan Timestamp { get; }
}

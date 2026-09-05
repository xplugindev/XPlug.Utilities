using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Core.Encoding;

/// <summary>
/// Compresses raw frames into an elementary stream (spec 41). Implemented in
/// Phase 2; declared here so the pipeline's dependency direction is fixed now.
/// </summary>
public interface IVideoEncoder : IVideoFrameSink, IDisposable
{
    VideoCodec Codec { get; }

    int Width { get; }

    int Height { get; }

    double FrameRate { get; }

    int BitrateKbps { get; }

    bool IsHardwareAccelerated { get; }

    /// <summary>Raised for every encoded frame, on the encoder's thread.</summary>
    event EventHandler<EncodedFrame>? FrameEncoded;

    void Start(EncoderSettings settings);

    void Stop();

    /// <summary>
    /// Forces the next frame to be an IDR. Called when an RTSP client joins, so
    /// it can start decoding without waiting for the next GOP boundary.
    /// </summary>
    void RequestKeyFrame();
}

public enum VideoCodec
{
    H264 = 0,
}

public sealed record EncoderSettings
{
    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public double FrameRate { get; init; } = 30;

    public int BitrateKbps { get; init; } = 4000;

    /// <summary>Frames between IDRs. 30 at 30 fps gives the 1-second keyframe interval in spec 11.</summary>
    public int GopLength { get; init; } = 30;

    public bool PreferHardware { get; init; } = true;
}

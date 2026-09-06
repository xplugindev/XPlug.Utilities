using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Core.Encoding;

/// <summary>
/// Compresses raw frames into an H.264 elementary stream (spec 41).
/// </summary>
/// <remarks>
/// The encoder is an <see cref="IVideoFrameSink"/>, so it attaches directly to
/// the camera. There is exactly one encoder for the whole application no matter
/// how many RTSP clients are connected (spec 33).
/// </remarks>
public interface IVideoEncoder : IVideoFrameSink, IDisposable
{
    VideoCodec Codec { get; }

    int Width { get; }

    int Height { get; }

    double FrameRate { get; }

    int BitrateKbps { get; }

    bool IsHardwareAccelerated { get; }

    bool IsRunning { get; }

    /// <summary>Name of the encoder actually in use, for the status panel and logs.</summary>
    string EncoderName { get; }

    /// <summary>
    /// The sequence parameter set, without its start code, or null before the
    /// first frame is encoded.
    /// </summary>
    /// <remarks>
    /// RTSP needs this for the SDP <c>sprop-parameter-sets</c> attribute, and
    /// the RTP payloader repeats it in-band before every IDR because some
    /// decoders ignore the SDP copy.
    /// </remarks>
    byte[]? SequenceParameterSet { get; }

    /// <summary>The picture parameter set, without its start code.</summary>
    byte[]? PictureParameterSet { get; }

    EncoderStatistics Statistics { get; }

    /// <summary>Raised for every encoded frame, on the encoder's own thread.</summary>
    event EventHandler<EncodedFrame>? FrameEncoded;

    void Start(EncoderSettings settings);

    void Stop();

    /// <summary>
    /// Asks for the next frame to be an IDR.
    /// </summary>
    /// <remarks>
    /// Called when an RTSP client joins so it can start decoding immediately
    /// rather than waiting for the next GOP boundary. A no-op on encoders that
    /// cannot force one, where the 1-second GOP is the fallback.
    /// </remarks>
    void RequestKeyFrame();
}

public enum VideoCodec
{
    H264 = 0,
}

public sealed record EncoderStatistics
{
    public long FramesEncoded { get; init; }

    public long KeyFramesEncoded { get; init; }

    public long FramesDropped { get; init; }

    /// <summary>Output bitrate measured over the last sampling window, in kbps.</summary>
    public double MeasuredBitrateKbps { get; init; }

    public static EncoderStatistics Empty { get; } = new();
}

public sealed record EncoderSettings
{
    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public double FrameRate { get; init; } = 30;

    public int BitrateKbps { get; init; } = 4000;

    /// <summary>Frames between IDRs. 30 at 30 fps is the 1-second interval in spec 11.</summary>
    public int GopLength { get; init; } = 30;

    public bool PreferHardware { get; init; } = true;

    /// <summary>H.264 profile number: 66 Baseline, 77 Main, 100 High. Main by default (spec 11).</summary>
    public int Profile { get; init; } = 77;

    public override string ToString() =>
        $"H.264 {Width}x{Height}@{FrameRate:0.##} {BitrateKbps}kbps GOP {GopLength}";
}

namespace Screen2VMS.Core.Encoding;

/// <summary>
/// One compressed frame in Annex-B form, owned by the receiver.
/// </summary>
/// <remarks>
/// Unlike <see cref="Cameras.VideoFrame"/> this is a class holding its own
/// buffer, because encoded frames outlive the callback: the RTSP server fans
/// one encoded frame out to every connected client (spec 33, 43).
/// </remarks>
public sealed class EncodedFrame
{
    public EncodedFrame(byte[] data, int length, bool isKeyFrame, TimeSpan timestamp)
    {
        Data = data;
        Length = length;
        IsKeyFrame = isKeyFrame;
        Timestamp = timestamp;
    }

    /// <summary>Annex-B bitstream. May be longer than <see cref="Length"/> when pooled.</summary>
    public byte[] Data { get; }

    public int Length { get; }

    /// <summary>True for an IDR. Sequence and picture parameter sets precede every IDR in-band.</summary>
    public bool IsKeyFrame { get; }

    public TimeSpan Timestamp { get; }

    public ReadOnlySpan<byte> Span => Data.AsSpan(0, Length);
}

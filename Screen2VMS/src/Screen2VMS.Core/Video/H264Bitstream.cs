namespace Screen2VMS.Core.Video;

/// <summary>
/// Reads H.264 elementary streams: splitting NAL units, recognising IDRs, and
/// pulling the profile and level out of a sequence parameter set.
/// </summary>
/// <remarks>
/// Pure and allocation-light so the RTP payloader and the SDP builder can share
/// it, and so the parsing rules are testable without an encoder.
/// </remarks>
public static class H264Bitstream
{
    /// <summary>Coded slice of a non-IDR picture.</summary>
    public const int NalTypeNonIdrSlice = 1;

    /// <summary>Coded slice of an IDR picture.</summary>
    public const int NalTypeIdrSlice = 5;

    /// <summary>Supplemental enhancement information.</summary>
    public const int NalTypeSei = 6;

    /// <summary>Sequence parameter set.</summary>
    public const int NalTypeSps = 7;

    /// <summary>Picture parameter set.</summary>
    public const int NalTypePps = 8;

    /// <summary>Access unit delimiter.</summary>
    public const int NalTypeAccessUnitDelimiter = 9;

    /// <summary>The NAL unit type from the first byte of a NAL unit.</summary>
    public static int NalType(ReadOnlySpan<byte> nalUnit) =>
        nalUnit.IsEmpty ? 0 : nalUnit[0] & 0x1F;

    /// <summary>True when the buffer begins with a three- or four-byte start code.</summary>
    public static bool IsAnnexB(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1)
        {
            return true;
        }

        return data.Length >= 3 && data[0] == 0 && data[1] == 0 && data[2] == 1;
    }

    /// <summary>
    /// Splits an Annex-B stream into NAL units, with start codes stripped.
    /// </summary>
    /// <remarks>
    /// The returned spans point into <paramref name="data"/>; they are only
    /// valid while it is.
    /// </remarks>
    public static List<Range> SplitAnnexB(ReadOnlySpan<byte> data)
    {
        var units = new List<Range>();
        var length = data.Length;
        var cursor = 0;

        while (cursor < length)
        {
            var start = FindStartCode(data, cursor, out var startCodeLength);
            if (start < 0)
            {
                break;
            }

            var payloadStart = start + startCodeLength;
            var next = FindStartCode(data, payloadStart, out _);
            var payloadEnd = next < 0 ? length : next;

            // Trailing zeros belong to the following start code, not this unit.
            while (payloadEnd > payloadStart && data[payloadEnd - 1] == 0)
            {
                payloadEnd--;
            }

            if (payloadEnd > payloadStart)
            {
                units.Add(new Range(payloadStart, payloadEnd));
            }

            if (next < 0)
            {
                break;
            }

            cursor = next;
        }

        return units;
    }

    /// <summary>
    /// Splits an Annex-B stream into freshly allocated NAL unit arrays.
    /// </summary>
    /// <remarks>
    /// Used where the units outlive the source buffer, such as handing samples
    /// to the RTSP server.
    /// </remarks>
    public static List<byte[]> SplitAnnexBToArrays(ReadOnlySpan<byte> data)
    {
        var ranges = SplitAnnexB(data);
        var units = new List<byte[]>(ranges.Count);

        foreach (var range in ranges)
        {
            units.Add(data[range].ToArray());
        }

        return units;
    }

    /// <summary>
    /// Splits an Annex-B buffer into NAL units that share its memory.
    /// </summary>
    /// <param name="data">Buffer holding the frame.</param>
    /// <param name="length">Bytes of <paramref name="data"/> that are in use.</param>
    /// <remarks>
    /// Nothing is copied: each unit is a window onto <paramref name="data"/>,
    /// which is what lets an encoded frame reach the RTP payloader without
    /// being duplicated per NAL unit.
    /// </remarks>
    public static List<ReadOnlyMemory<byte>> SplitAnnexBToMemory(byte[] data, int length)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (length < 0 || length > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var ranges = SplitAnnexB(data.AsSpan(0, length));
        var units = new List<ReadOnlyMemory<byte>>(ranges.Count);

        foreach (var range in ranges)
        {
            var (offset, count) = range.GetOffsetAndLength(length);
            units.Add(new ReadOnlyMemory<byte>(data, offset, count));
        }

        return units;
    }

    /// <summary>
    /// Index of the next start code at or after <paramref name="from"/>, or -1.
    /// </summary>
    private static int FindStartCode(ReadOnlySpan<byte> data, int from, out int startCodeLength)
    {
        startCodeLength = 0;

        for (var i = from; i + 2 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0)
            {
                continue;
            }

            if (data[i + 2] == 1)
            {
                startCodeLength = 3;
                return i;
            }

            if (i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1)
            {
                startCodeLength = 4;
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when any NAL unit in the buffer is an IDR slice.</summary>
    public static bool ContainsKeyFrame(ReadOnlySpan<byte> annexB)
    {
        foreach (var range in SplitAnnexB(annexB))
        {
            var type = NalType(annexB[range]);
            if (type == NalTypeIdrSlice)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the sequence and picture parameter sets from an Annex-B buffer.
    /// </summary>
    /// <returns>Either may be null if the buffer does not carry it.</returns>
    public static (byte[]? Sps, byte[]? Pps) ExtractParameterSets(ReadOnlySpan<byte> annexB)
    {
        byte[]? sps = null;
        byte[]? pps = null;

        foreach (var range in SplitAnnexB(annexB))
        {
            var unit = annexB[range];
            switch (NalType(unit))
            {
                case NalTypeSps:
                    sps ??= unit.ToArray();
                    break;

                case NalTypePps:
                    pps ??= unit.ToArray();
                    break;
            }
        }

        return (sps, pps);
    }

    /// <summary>
    /// Reads profile_idc, the constraint flag byte and level_idc from an SPS.
    /// </summary>
    /// <param name="sps">SPS NAL unit including its header byte, without a start code.</param>
    /// <remarks>
    /// These three bytes become the <c>profile-level-id</c> in the SDP fmtp
    /// line, which is how a VMS decides whether it can decode the stream.
    /// </remarks>
    public static (int ProfileIdc, int ProfileIop, int LevelIdc) ParseProfileLevel(ReadOnlySpan<byte> sps)
    {
        if (sps.Length < 4)
        {
            throw new ArgumentException("An SPS is at least four bytes long.", nameof(sps));
        }

        if (NalType(sps) != NalTypeSps)
        {
            throw new ArgumentException("The buffer is not a sequence parameter set.", nameof(sps));
        }

        return (sps[1], sps[2], sps[3]);
    }

    /// <summary>Formats profile and level as the six hex digits used by SDP.</summary>
    public static string FormatProfileLevelId(int profileIdc, int profileIop, int levelIdc) =>
        $"{profileIdc:x2}{profileIop:x2}{levelIdc:x2}";

    /// <summary>Prefixes a NAL unit with a four-byte Annex-B start code.</summary>
    public static byte[] ToAnnexB(ReadOnlySpan<byte> nalUnit)
    {
        var result = new byte[nalUnit.Length + 4];
        result[3] = 1;
        nalUnit.CopyTo(result.AsSpan(4));
        return result;
    }
}

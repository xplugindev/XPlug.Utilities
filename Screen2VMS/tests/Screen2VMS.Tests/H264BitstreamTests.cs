using Screen2VMS.Core.Video;

namespace Screen2VMS.Tests;

public class H264BitstreamTests
{
    /// <summary>Builds an Annex-B stream from NAL payloads, using four-byte start codes.</summary>
    private static byte[] AnnexB(params byte[][] nalUnits)
    {
        var stream = new List<byte>();

        foreach (var unit in nalUnits)
        {
            stream.AddRange([0, 0, 0, 1]);
            stream.AddRange(unit);
        }

        return stream.ToArray();
    }

    private static byte[] Nal(int type, params byte[] payload) =>
        [(byte)(type & 0x1F), .. payload];

    [Fact]
    public void SplitAnnexB_FindsEveryNalUnit()
    {
        var data = AnnexB(Nal(7, 1, 2), Nal(8, 3), Nal(5, 4, 5, 6));

        var units = H264Bitstream.SplitAnnexB(data);

        Assert.Equal(3, units.Count);
        Assert.Equal(H264Bitstream.NalTypeSps, H264Bitstream.NalType(data.AsSpan()[units[0]]));
        Assert.Equal(H264Bitstream.NalTypePps, H264Bitstream.NalType(data.AsSpan()[units[1]]));
        Assert.Equal(H264Bitstream.NalTypeIdrSlice, H264Bitstream.NalType(data.AsSpan()[units[2]]));
    }

    [Fact]
    public void SplitAnnexB_HandlesThreeByteStartCodes()
    {
        // Encoders mix three- and four-byte start codes within one stream.
        byte[] data = [0, 0, 1, 0x67, 0xAA, 0, 0, 0, 1, 0x68, 0xBB];

        var units = H264Bitstream.SplitAnnexB(data);

        Assert.Equal(2, units.Count);
        Assert.Equal(H264Bitstream.NalTypeSps, H264Bitstream.NalType(data.AsSpan()[units[0]]));
        Assert.Equal(H264Bitstream.NalTypePps, H264Bitstream.NalType(data.AsSpan()[units[1]]));
    }

    [Fact]
    public void SplitAnnexB_DoesNotSwallowTrailingZerosIntoTheUnit()
    {
        // The zeros before a start code belong to the delimiter, not the
        // preceding payload; counting them corrupts the NAL length.
        byte[] data = [0, 0, 0, 1, 0x65, 0x11, 0x22, 0, 0, 0, 1, 0x61, 0x33];

        var units = H264Bitstream.SplitAnnexB(data);

        Assert.Equal(2, units.Count);
        Assert.Equal(3, units[0].End.Value - units[0].Start.Value);
        Assert.Equal([0x65, 0x11, 0x22], data.AsSpan()[units[0]].ToArray());
    }

    [Fact]
    public void SplitAnnexB_ReturnsNothingForABufferWithNoStartCode()
    {
        Assert.Empty(H264Bitstream.SplitAnnexB([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void IsAnnexB_RecognisesBothStartCodeLengths()
    {
        Assert.True(H264Bitstream.IsAnnexB([0, 0, 0, 1, 0x67]));
        Assert.True(H264Bitstream.IsAnnexB([0, 0, 1, 0x67]));
        Assert.False(H264Bitstream.IsAnnexB([0x67, 0x42, 0, 0]));
    }

    [Fact]
    public void ContainsKeyFrame_IsTrueOnlyForAnIdr()
    {
        Assert.True(H264Bitstream.ContainsKeyFrame(AnnexB(Nal(7), Nal(8), Nal(5, 1))));
        Assert.False(H264Bitstream.ContainsKeyFrame(AnnexB(Nal(1, 1), Nal(1, 2))));
    }

    [Fact]
    public void ExtractParameterSets_PullsSpsAndPps()
    {
        var data = AnnexB(Nal(9), Nal(7, 0x4D, 0x40, 0x1F), Nal(8, 0xCE), Nal(5, 1));

        var (sps, pps) = H264Bitstream.ExtractParameterSets(data);

        Assert.NotNull(sps);
        Assert.NotNull(pps);
        Assert.Equal(H264Bitstream.NalTypeSps, H264Bitstream.NalType(sps));
        Assert.Equal(H264Bitstream.NalTypePps, H264Bitstream.NalType(pps));
    }

    [Fact]
    public void ExtractParameterSets_ReturnsNullsWhenAbsent()
    {
        var (sps, pps) = H264Bitstream.ExtractParameterSets(AnnexB(Nal(1, 1)));

        Assert.Null(sps);
        Assert.Null(pps);
    }

    [Fact]
    public void ParseProfileLevel_ReadsTheRealEncoderOutput()
    {
        // Captured from the Media Foundation H.264 encoder: Main profile,
        // level 3.1, which SDP writes as profile-level-id 4d401f.
        var sps = Convert.FromHexString("674D401F95A014016EC044000003000400000300F03682211A80");

        var (profileIdc, profileIop, levelIdc) = H264Bitstream.ParseProfileLevel(sps);

        Assert.Equal(0x4D, profileIdc);
        Assert.Equal(0x40, profileIop);
        Assert.Equal(0x1F, levelIdc);
        Assert.Equal("4d401f", H264Bitstream.FormatProfileLevelId(profileIdc, profileIop, levelIdc));
    }

    [Fact]
    public void ParseProfileLevel_RejectsABufferThatIsNotAnSps()
    {
        Assert.Throws<ArgumentException>(() => H264Bitstream.ParseProfileLevel(Nal(8, 0xCE, 0x3C, 0x80)));
    }

    [Fact]
    public void SplitAnnexBToMemory_SharesTheSourceBuffer()
    {
        // The RTP payloader relies on this not copying: each unit must be a
        // window onto the encoded frame.
        var data = AnnexB(Nal(7, 0xAA), Nal(5, 0xBB));

        var units = H264Bitstream.SplitAnnexBToMemory(data, data.Length);

        Assert.Equal(2, units.Count);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(units[0], out var segment));
        Assert.Same(data, segment.Array);
    }

    [Fact]
    public void SplitAnnexBToMemory_HonoursTheUsedLength()
    {
        // Encoded frames sit in a buffer that is longer than the frame, so
        // anything past the used length must be ignored.
        var frame = AnnexB(Nal(5, 1, 2, 3));
        var padded = new byte[frame.Length + 32];
        frame.CopyTo(padded, 0);

        var units = H264Bitstream.SplitAnnexBToMemory(padded, frame.Length);

        Assert.Single(units);
        Assert.Equal(4, units[0].Length);
    }

    [Fact]
    public void SplitAnnexBToMemory_RejectsALengthBeyondTheBuffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Bitstream.SplitAnnexBToMemory(new byte[4], 99));
    }
}

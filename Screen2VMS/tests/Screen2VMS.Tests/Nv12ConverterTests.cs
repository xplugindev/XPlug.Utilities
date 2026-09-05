using Screen2VMS.Core.Video;

namespace Screen2VMS.Tests;

public class Nv12ConverterTests
{
    /// <summary>Builds an NV12 frame where every pixel carries the same YUV triple.</summary>
    private static byte[] SolidNv12(int width, int height, int stride, byte y, byte u, byte v)
    {
        var chromaOffset = stride * height;
        var buffer = new byte[chromaOffset + (stride * ((height + 1) / 2))];

        for (var row = 0; row < height; row++)
        {
            buffer.AsSpan(row * stride, width).Fill(y);
        }

        for (var row = 0; row < (height + 1) / 2; row++)
        {
            var start = chromaOffset + (row * stride);
            for (var column = 0; column < width; column += 2)
            {
                buffer[start + column] = u;
                buffer[start + column + 1] = v;
            }
        }

        return buffer;
    }

    [Fact]
    public void ToBgra32_ConvertsBlackToBlack()
    {
        // Limited-range black is Y=16 with neutral chroma.
        var source = SolidNv12(4, 4, 4, y: 16, u: 128, v: 128);
        var destination = new byte[4 * 4 * 4];

        Nv12Converter.ToBgra32(source, 4, 4, 4, destination, 4, 4);

        for (var i = 0; i < destination.Length; i += 4)
        {
            Assert.Equal(0, destination[i]);
            Assert.Equal(0, destination[i + 1]);
            Assert.Equal(0, destination[i + 2]);
            Assert.Equal(255, destination[i + 3]);
        }
    }

    [Fact]
    public void ToBgra32_ConvertsWhiteToWhite()
    {
        var source = SolidNv12(4, 4, 4, y: 235, u: 128, v: 128);
        var destination = new byte[4 * 4 * 4];

        Nv12Converter.ToBgra32(source, 4, 4, 4, destination, 4, 4);

        for (var i = 0; i < destination.Length; i += 4)
        {
            Assert.Equal(255, destination[i]);
            Assert.Equal(255, destination[i + 1]);
            Assert.Equal(255, destination[i + 2]);
        }
    }

    [Fact]
    public void ToBgra32_HonoursAStrideWiderThanTheFrame()
    {
        // Driver buffers are commonly padded, and reading them as packed would
        // shear the picture.
        const int width = 4;
        const int height = 4;
        const int stride = 8;

        var source = SolidNv12(width, height, stride, y: 235, u: 128, v: 128);
        var destination = new byte[width * height * 4];

        Nv12Converter.ToBgra32(source, width, height, stride, destination, width, height);

        for (var i = 0; i < destination.Length; i += 4)
        {
            Assert.Equal(255, destination[i]);
        }
    }

    [Fact]
    public void ToBgra32_Downscales()
    {
        var source = SolidNv12(8, 8, 8, y: 235, u: 128, v: 128);
        var destination = new byte[4 * 4 * 4];

        Nv12Converter.ToBgra32(source, 8, 8, 8, destination, 4, 4);

        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal(255, destination[i * 4]));
    }

    [Fact]
    public void ToBgra32_RejectsATruncatedSourceBuffer()
    {
        var destination = new byte[4 * 4 * 4];

        Assert.Throws<ArgumentException>(() =>
            Nv12Converter.ToBgra32(new byte[8], 4, 4, 4, destination, 4, 4));
    }

    [Fact]
    public void ToBgra32_RejectsAStrideNarrowerThanTheFrame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Nv12Converter.ToBgra32(new byte[256], 16, 4, 8, new byte[1024], 16, 4));
    }

    [Theory]
    [InlineData(1920, 1080, 640, 640, 360)]
    [InlineData(1280, 720, 640, 640, 360)]
    [InlineData(640, 480, 640, 640, 480)]
    [InlineData(320, 240, 640, 320, 240)]
    public void PreviewSizeFor_ScalesDownButNeverUp(
        int sourceWidth,
        int sourceHeight,
        int maxWidth,
        int expectedWidth,
        int expectedHeight)
    {
        var (width, height) = Nv12Converter.PreviewSizeFor(sourceWidth, sourceHeight, maxWidth);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }
}

using System.Runtime.CompilerServices;

namespace Screen2VMS.Core.Video;

/// <summary>
/// NV12 to BGRA32, with nearest-neighbour downscaling folded into the same pass.
/// </summary>
/// <remarks>
/// Preview only. The capture pipeline carries NV12 end to end because that is
/// what the H.264 encoder takes; nothing but the on-screen preview ever needs
/// RGB. Downscaling here is what keeps preview off the CPU budget - converting
/// a full 1920x1080 frame 30 times a second in managed code would cost more
/// than the encoder does.
/// </remarks>
public static class Nv12Converter
{
    /// <summary>Bytes per pixel in the BGRA32 destination.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Converts and rescales an NV12 frame into a BGRA32 buffer.
    /// </summary>
    /// <param name="source">NV12 frame: a Y plane of <paramref name="sourceHeight"/> rows followed by an interleaved UV plane of half that.</param>
    /// <param name="sourceWidth">Source width in pixels.</param>
    /// <param name="sourceHeight">Source height in pixels.</param>
    /// <param name="sourceStride">Bytes per row of the Y plane. Equals or exceeds <paramref name="sourceWidth"/>.</param>
    /// <param name="destination">Destination buffer, at least <paramref name="destinationWidth"/> * <paramref name="destinationHeight"/> * 4 bytes.</param>
    /// <param name="destinationWidth">Destination width in pixels.</param>
    /// <param name="destinationHeight">Destination height in pixels.</param>
    public static void ToBgra32(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        Span<byte> destination,
        int destinationWidth,
        int destinationHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source dimensions must be positive.");
        }

        if (destinationWidth <= 0 || destinationHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationWidth), "Destination dimensions must be positive.");
        }

        if (sourceStride < sourceWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStride), "Stride cannot be narrower than the frame.");
        }

        var chromaOffset = sourceStride * sourceHeight;
        var requiredSource = chromaOffset + (sourceStride * ((sourceHeight + 1) / 2));
        if (source.Length < requiredSource)
        {
            throw new ArgumentException(
                $"NV12 frame needs {requiredSource} bytes but only {source.Length} were supplied.",
                nameof(source));
        }

        var requiredDestination = destinationWidth * destinationHeight * BytesPerPixel;
        if (destination.Length < requiredDestination)
        {
            throw new ArgumentException(
                $"Destination needs {requiredDestination} bytes but only {destination.Length} were supplied.",
                nameof(destination));
        }

        // Fixed-point step through the source, so the inner loop has no division.
        var xStep = ((long)sourceWidth << 16) / destinationWidth;
        var yStep = ((long)sourceHeight << 16) / destinationHeight;

        unsafe
        {
            fixed (byte* srcBase = source)
            fixed (byte* dstBase = destination)
            {
                var chroma = srcBase + chromaOffset;
                var dst = dstBase;

                for (var dy = 0; dy < destinationHeight; dy++)
                {
                    var sy = (int)((dy * yStep) >> 16);
                    var lumaRow = srcBase + ((long)sy * sourceStride);
                    var chromaRow = chroma + ((long)(sy >> 1) * sourceStride);

                    var xAccumulator = 0L;
                    for (var dx = 0; dx < destinationWidth; dx++)
                    {
                        var sx = (int)(xAccumulator >> 16);
                        xAccumulator += xStep;

                        var y = lumaRow[sx];

                        // One chroma sample covers a 2x2 luma block, so the
                        // chroma pair for column sx starts at (sx & ~1).
                        var chromaIndex = sx & ~1;
                        var u = chromaRow[chromaIndex];
                        var v = chromaRow[chromaIndex + 1];

                        var c = y - 16;
                        var d = u - 128;
                        var e = v - 128;

                        // BT.601 limited range. Preview fidelity only - nothing
                        // downstream of here sees these pixels.
                        var r = ((298 * c) + (409 * e) + 128) >> 8;
                        var g = ((298 * c) - (100 * d) - (208 * e) + 128) >> 8;
                        var b = ((298 * c) + (516 * d) + 128) >> 8;

                        dst[0] = ClampToByte(b);
                        dst[1] = ClampToByte(g);
                        dst[2] = ClampToByte(r);
                        dst[3] = 255;
                        dst += BytesPerPixel;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Preview dimensions for a source frame: scaled down to fit
    /// <paramref name="maxWidth"/>, never scaled up, and kept even.
    /// </summary>
    public static (int Width, int Height) PreviewSizeFor(int sourceWidth, int sourceHeight, int maxWidth)
    {
        if (sourceWidth <= maxWidth)
        {
            return (sourceWidth, sourceHeight);
        }

        var width = maxWidth;
        var height = (int)Math.Round(sourceHeight * (double)maxWidth / sourceWidth);
        return (MakeEven(width), MakeEven(Math.Max(height, 2)));
    }

    private static int MakeEven(int value) => value & ~1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value) =>
        value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}

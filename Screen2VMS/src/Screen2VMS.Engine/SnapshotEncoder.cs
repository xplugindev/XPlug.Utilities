using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Video;

namespace Screen2VMS.Engine;

/// <summary>
/// Turns a captured frame into a JPEG for the ONVIF snapshot endpoint.
/// </summary>
/// <remarks>
/// Snapshots are requested rarely - a VMS fetches one to draw a thumbnail - so
/// this favours simplicity over speed and is never on the streaming path.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class SnapshotEncoder
{
    /// <summary>JPEG quality, high enough that a thumbnail looks right.</summary>
    private const long Quality = 85L;

    /// <summary>
    /// Encodes a frame snapshot as JPEG, or returns null if the format is not
    /// one the pipeline produces.
    /// </summary>
    internal static byte[]? ToJpeg(CameraFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var bgra = ToBgra32(snapshot);
        if (bgra is null)
        {
            return null;
        }

        using var bitmap = new Bitmap(snapshot.Width, snapshot.Height, PixelFormat.Format32bppRgb);

        var area = new Rectangle(0, 0, snapshot.Width, snapshot.Height);
        var locked = bitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);

        try
        {
            // Copy row by row: the bitmap's own stride is padded to a four-byte
            // boundary and need not match the source.
            var sourceStride = snapshot.Width * Nv12Converter.BytesPerPixel;

            for (var y = 0; y < snapshot.Height; y++)
            {
                Marshal.Copy(
                    bgra,
                    y * sourceStride,
                    locked.Scan0 + (y * locked.Stride),
                    sourceStride);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return Encode(bitmap);
    }

    private static byte[]? ToBgra32(CameraFrameSnapshot snapshot)
    {
        var pixels = new byte[snapshot.Width * snapshot.Height * Nv12Converter.BytesPerPixel];

        switch (snapshot.Format)
        {
            case VideoPixelFormat.Nv12:
                Nv12Converter.ToBgra32(
                    snapshot.Data,
                    snapshot.Width,
                    snapshot.Height,
                    snapshot.Stride,
                    pixels,
                    snapshot.Width,
                    snapshot.Height);
                return pixels;

            case VideoPixelFormat.Bgra32:
                for (var y = 0; y < snapshot.Height; y++)
                {
                    snapshot.Data
                        .AsSpan(y * snapshot.Stride, snapshot.Width * Nv12Converter.BytesPerPixel)
                        .CopyTo(pixels.AsSpan(y * snapshot.Width * Nv12Converter.BytesPerPixel));
                }

                return pixels;

            default:
                return null;
        }
    }

    private static byte[] Encode(Bitmap bitmap)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        using var stream = new MemoryStream();

        if (codec is null)
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }

        using var parameters = new EncoderParameters(1);
        using var quality = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Quality);
        parameters.Param[0] = quality;

        bitmap.Save(stream, codec, parameters);
        return stream.ToArray();
    }
}

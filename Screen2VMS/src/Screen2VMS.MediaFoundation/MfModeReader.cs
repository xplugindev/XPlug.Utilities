using System.Runtime.InteropServices;
using Screen2VMS.Core.Cameras;

namespace Screen2VMS.MediaFoundation;

/// <summary>Reads the capture modes a device advertises on its video stream.</summary>
internal static class MfModeReader
{
    /// <summary>
    /// Every native media type on the first video stream, in the order the
    /// source reader exposes them.
    /// </summary>
    /// <remarks>
    /// The list index is what <see cref="CameraMode.NativeTypeIndex"/> carries,
    /// so the same reader configuration must be used to read modes and to
    /// select one. Types the pipeline cannot interpret are still listed, with
    /// <see cref="VideoPixelFormat.Unknown"/>, so indices stay aligned.
    /// </remarks>
    internal static IReadOnlyList<CameraMode> ReadNativeModes(IMFSourceReader reader)
    {
        var modes = new List<CameraMode>();

        for (var index = 0u; ; index++)
        {
            var hr = reader.GetNativeMediaType(MfConstants.FirstVideoStream, index, out var mediaType);
            if (hr == MfNative.MfENoMoreTypes || hr == MfNative.EInvalidArg || mediaType is null)
            {
                break;
            }

            if (MfNative.Failed(hr))
            {
                break;
            }

            try
            {
                var mode = Describe(mediaType, (int)index);
                if (mode is not null)
                {
                    modes.Add(mode);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(mediaType);
            }
        }

        return modes;
    }

    /// <summary>Turns one media type into a <see cref="CameraMode"/>, or null if it is unusable.</summary>
    internal static CameraMode? Describe(IMFMediaType mediaType, int nativeTypeIndex)
    {
        if (!MfNative.TryGetPackedRatio(mediaType, MfConstants.MtFrameSize, out var width, out var height)
            || width == 0
            || height == 0)
        {
            return null;
        }

        var subtypeKey = MfConstants.MtSubtype;
        var format = MfNative.Succeeded(mediaType.GetGUID(ref subtypeKey, out var subtype))
            ? MfConstants.ToPixelFormat(subtype)
            : VideoPixelFormat.Unknown;

        var nominal = ReadFrameRate(mediaType, MfConstants.MtFrameRate) ?? 0d;
        var min = ReadFrameRate(mediaType, MfConstants.MtFrameRateRangeMin) ?? nominal;
        var max = ReadFrameRate(mediaType, MfConstants.MtFrameRateRangeMax) ?? nominal;

        if (max <= 0)
        {
            // No rate advertised at all. Treat the mode as fixed at whatever the
            // driver decides rather than discarding it.
            min = 0;
            max = 0;
        }
        else if (min <= 0)
        {
            min = max;
        }

        return new CameraMode((int)width, (int)height, min, max, format, nativeTypeIndex);
    }

    /// <summary>Reads a frame-rate attribute, stored as a numerator/denominator pair.</summary>
    private static double? ReadFrameRate(IMFMediaType mediaType, Guid key)
    {
        if (!MfNative.TryGetPackedRatio(mediaType, key, out var numerator, out var denominator)
            || denominator == 0)
        {
            return null;
        }

        return (double)numerator / denominator;
    }

    /// <summary>
    /// Expresses a frame rate as the numerator/denominator pair MF wants,
    /// preserving the NTSC rates exactly.
    /// </summary>
    internal static (uint Numerator, uint Denominator) ToRatio(double frameRate)
    {
        // 29.97 and 59.94 are 30000/1001 and 60000/1001. Rounding them to
        // integers makes the driver reject the media type on some webcams.
        var scaled = frameRate * 1001d;
        var rounded = Math.Round(scaled);
        if (Math.Abs(scaled - rounded) < 0.01 && rounded > 0)
        {
            var numerator = (uint)rounded;
            if (numerator % 1001 != 0)
            {
                return (numerator, 1001);
            }
        }

        return ((uint)Math.Round(frameRate), 1);
    }
}

using System.Runtime.InteropServices;
using System.Text;

namespace Screen2VMS.MediaFoundation;

/// <summary>Media Foundation entry points and the HRESULTs worth naming.</summary>
internal static class MfNative
{
    internal const int SOk = 0;

    /// <summary>Returned by GetNativeMediaType once the mode list is exhausted.</summary>
    internal const int MfENoMoreTypes = unchecked((int)0xC00D36B9);

    internal const int MfEInvalidMediaType = unchecked((int)0xC00D36B4);

    /// <summary>The camera was unplugged while streaming (spec 31).</summary>
    internal const int MfEVideoRecordingDeviceInvalidated = unchecked((int)0xC00DABE1);

    internal const int MfEHardwareMftFailedStartStreaming = unchecked((int)0xC00D3E85);

    /// <summary>Another application already holds the device (spec 32).</summary>
    internal const int ErrorSharingViolation = unchecked((int)0x80070020);

    internal const int ErrorBusy = unchecked((int)0x800700AA);

    /// <summary>
    /// Also what Windows returns when camera access is switched off under
    /// Settings, Privacy and security, Camera - so it needs its own message.
    /// </summary>
    internal const int EAccessDenied = unchecked((int)0x80070005);

    internal const int EInvalidArg = unchecked((int)0x80070057);

    internal static bool Succeeded(int hr) => hr >= 0;

    internal static bool Failed(int hr) => hr < 0;

    // DllImport rather than LibraryImport: the source generator cannot marshal
    // COM interface parameters, which is most of this surface.
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mf.dll", ExactSpelling = true)]
    internal static extern int MFEnumDeviceSources(
        IMFAttributes attributes,
        out IntPtr activateArray,
        out uint count);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    internal static extern int MFCreateSourceReaderFromMediaSource(
        IMFMediaSource mediaSource,
        IMFAttributes? attributes,
        out IMFSourceReader sourceReader);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAlignedMemoryBuffer(int maxLength, int alignment, out IMFMediaBuffer buffer);

    /// <summary>
    /// Enumerates transforms. The type-info arguments are passed as raw
    /// pointers so either can be omitted with <see cref="IntPtr.Zero"/>.
    /// </summary>
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFTEnumEx(
        Guid category,
        uint flags,
        IntPtr inputType,
        IntPtr outputType,
        out IntPtr activateArray,
        out uint count);

    /// <summary>
    /// Reads an attribute that packs two 32-bit values into one 64-bit slot -
    /// how MF stores frame size and frame rate. High word first.
    /// </summary>
    internal static bool TryGetPackedRatio(IMFMediaType mediaType, Guid key, out uint numerator, out uint denominator)
    {
        numerator = 0;
        denominator = 0;

        var hr = mediaType.GetUINT64(ref key, out var packed);
        if (Failed(hr))
        {
            return false;
        }

        numerator = (uint)(packed >> 32);
        denominator = (uint)(packed & 0xFFFFFFFF);
        return true;
    }

    /// <summary>Packs two 32-bit values into the 64-bit form MF expects.</summary>
    internal static ulong PackRatio(uint numerator, uint denominator) =>
        ((ulong)numerator << 32) | denominator;

    /// <summary>Reads a string attribute, returning null when it is absent.</summary>
    internal static string? GetStringAttribute(IMFActivate activate, Guid key)
    {
        var hr = activate.GetStringLength(ref key, out var length);
        if (Failed(hr) || length == 0)
        {
            return null;
        }

        // GetStringLength excludes the terminator; GetString wants room for it.
        var buffer = new StringBuilder((int)length + 1);
        var written = 0u;
        hr = activate.GetString(ref key, buffer, length + 1, ref written);
        return Failed(hr) ? null : buffer.ToString();
    }

    /// <summary>
    /// Turns an HRESULT into the exception the pipeline expects, so a busy
    /// camera surfaces as a retryable condition rather than a hard failure.
    /// </summary>
    internal static Exception ToException(int hr, string operation) => hr switch
    {
        ErrorSharingViolation or ErrorBusy => new Core.Cameras.CameraBusyException(
            "Camera is currently unavailable or being used by another application."),

        EAccessDenied => new Core.Cameras.CameraBusyException(
            "Windows denied access to the camera. Check Settings, Privacy and security, Camera, " +
            "and allow desktop apps to access it."),

        MfEVideoRecordingDeviceInvalidated => new Core.Cameras.CameraException(
            "The camera was disconnected."),

        _ => new Core.Cameras.CameraException(
            $"{operation} failed with HRESULT 0x{hr:X8}.",
            Marshal.GetExceptionForHR(hr) ?? new ExternalException(operation, hr)),
    };

    /// <summary>Throws the mapped exception when <paramref name="hr"/> indicates failure.</summary>
    internal static void ThrowIfFailed(int hr, string operation)
    {
        if (Failed(hr))
        {
            throw ToException(hr, operation);
        }
    }
}

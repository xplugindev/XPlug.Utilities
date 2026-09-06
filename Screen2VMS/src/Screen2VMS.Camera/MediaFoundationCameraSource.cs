using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Screen2VMS.MediaFoundation;
using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Camera;

/// <summary>
/// One webcam, captured through a Media Foundation source reader.
/// </summary>
/// <remarks>
/// <para>
/// The reader is created and used on a single dedicated MTA thread, because
/// Media Foundation objects are not agile and the caller is usually the WPF UI
/// thread. Nothing outside <see cref="CaptureLoop"/> touches a COM object.
/// </para>
/// <para>
/// Output is negotiated to NV12 - the format the H.264 encoder wants in Phase 2
/// - and the reader inserts whatever decoder or converter the camera needs to
/// get there. Cameras that only offer MJPEG at 1080p are handled by that
/// conversion rather than by refusing the mode.
/// </para>
/// </remarks>
internal sealed class MediaFoundationCameraSource : ICameraSource
{
    private const int StopTimeoutMs = 5000;
    private const int SnapshotTimeoutMs = 500;
    private const uint InterlaceModeProgressive = 2;

    private readonly CameraDevice device;
    private readonly ILogger logger;
    private readonly object sinkLock = new();
    private readonly List<IVideoFrameSink> sinks = new();

    private volatile IVideoFrameSink[] sinkSnapshot = Array.Empty<IVideoFrameSink>();
    private volatile CameraState state = CameraState.Stopped;
    private volatile bool stopRequested;
    private SnapshotRequest? pendingSnapshot;

    private Thread? captureThread;
    private CameraSettings settings = CameraSettings.Default;
    private CameraMode? activeMode;
    private long framesCaptured;
    private long framesDropped;
    private double measuredFrameRate;
    private Exception? startFailure;
    private bool disposed;

    internal MediaFoundationCameraSource(CameraDevice device, ILogger logger)
    {
        this.device = device;
        this.logger = logger;
    }

    public event EventHandler<CameraState>? StateChanged;

    public string Id => device.Id;

    public string Name => device.Name;

    public string? Manufacturer => device.Manufacturer;

    public IReadOnlyList<CameraMode> Capabilities => device.Modes;

    public CameraState State => state;

    public CameraMode? ActiveMode => Volatile.Read(ref activeMode);

    public CameraStatistics Statistics => new()
    {
        FramesCaptured = Interlocked.Read(ref framesCaptured),
        FramesDropped = Interlocked.Read(ref framesDropped),
        MeasuredFrameRate = Volatile.Read(ref measuredFrameRate),
    };

    public void Start(CameraSettings cameraSettings)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (state is CameraState.Running or CameraState.Starting)
        {
            return;
        }

        settings = cameraSettings;
        stopRequested = false;
        Interlocked.Exchange(ref framesCaptured, 0);
        Interlocked.Exchange(ref framesDropped, 0);
        Volatile.Write(ref measuredFrameRate, 0);

        SetState(CameraState.Starting);

        using var started = new ManualResetEventSlim(false);
        startFailure = null;

        captureThread = new Thread(() => CaptureLoop(started))
        {
            IsBackground = true,
            Name = $"Screen2VMS.Capture[{device.Name}]",

            // Capture must not be starved by UI work; a dropped frame here is a
            // dropped frame on the wire.
            Priority = ThreadPriority.AboveNormal,
        };

        captureThread.SetApartmentState(ApartmentState.MTA);
        captureThread.Start();
        started.Wait();

        var failure = startFailure;
        if (failure is not null)
        {
            captureThread.Join(StopTimeoutMs);
            captureThread = null;
            SetState(failure is CameraBusyException ? CameraState.Busy : CameraState.Error);
            throw failure;
        }

        SetState(CameraState.Running);

        logger.LogInformation(
            "CameraStarted: {Camera} at {Mode}",
            device.Name,
            ActiveMode?.ToString() ?? "unknown mode");
    }

    public void Stop()
    {
        if (captureThread is null)
        {
            return;
        }

        stopRequested = true;

        // ReadSample blocks, so the loop exits at most one frame after the flag
        // is set rather than immediately.
        if (!captureThread.Join(StopTimeoutMs))
        {
            logger.LogWarning("Capture thread for {Camera} did not stop within {Timeout} ms.", device.Name, StopTimeoutMs);
        }

        captureThread = null;
        Volatile.Write(ref activeMode, null);
        SetState(CameraState.Stopped);

        logger.LogInformation("CameraStopped: {Camera}", device.Name);
    }

    public void AddSink(IVideoFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (sinkLock)
        {
            if (!sinks.Contains(sink))
            {
                sinks.Add(sink);
                sinkSnapshot = sinks.ToArray();
            }
        }
    }

    public void RemoveSink(IVideoFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (sinkLock)
        {
            if (sinks.Remove(sink))
            {
                sinkSnapshot = sinks.ToArray();
            }
        }
    }

    public CameraFrameSnapshot? GetLatestFrame()
    {
        if (state != CameraState.Running)
        {
            return null;
        }

        // Copying every frame just in case would cost more memory bandwidth
        // than the encoder does, so the capture loop only makes a copy when one
        // has actually been asked for.
        var request = new SnapshotRequest();
        Volatile.Write(ref pendingSnapshot, request);

        try
        {
            return request.Completed.Wait(SnapshotTimeoutMs) ? request.Result : null;
        }
        finally
        {
            Interlocked.CompareExchange(ref pendingSnapshot, null, request);
            request.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
    }

    private void SetState(CameraState next)
    {
        if (state == next)
        {
            return;
        }

        state = next;
        StateChanged?.Invoke(this, next);
    }

    private void CaptureLoop(ManualResetEventSlim started)
    {
        MfSession? session = null;
        IMFMediaSource? mediaSource = null;
        IMFSourceReader? reader = null;
        var signalled = false;

        try
        {
            session = MfSession.Start();
            mediaSource = OpenDevice(device.Id);
            reader = CreateReader(mediaSource);

            var negotiated = ConfigureOutput(reader);
            Volatile.Write(ref activeMode, negotiated.Mode);

            started.Set();
            signalled = true;

            PumpFrames(reader, negotiated);
        }
        catch (Exception ex)
        {
            if (!signalled)
            {
                startFailure = ex;
            }
            else
            {
                logger.LogError(ex, "Capture failed for {Camera}.", device.Name);
                SetState(ex is CameraBusyException ? CameraState.Busy : CameraState.Error);
            }
        }
        finally
        {
            if (!signalled)
            {
                started.Set();
            }

            if (reader is not null)
            {
                Marshal.ReleaseComObject(reader);
            }

            if (mediaSource is not null)
            {
                mediaSource.Shutdown();
                Marshal.ReleaseComObject(mediaSource);
            }

            session?.Dispose();
        }
    }

    /// <summary>Activates the media source for one specific symbolic link.</summary>
    private static IMFMediaSource OpenDevice(string symbolicLink)
    {
        MfNative.ThrowIfFailed(MfNative.MFCreateAttributes(out var attributes, 2), "MFCreateAttributes");

        try
        {
            var sourceTypeKey = MfConstants.DevSourceAttributeSourceType;
            var vidcap = MfConstants.DevSourceAttributeSourceTypeVidCap;
            MfNative.ThrowIfFailed(attributes.SetGUID(ref sourceTypeKey, ref vidcap), "SetGUID(SourceType)");

            var linkKey = MfConstants.DevSourceAttributeVidCapSymbolicLink;
            MfNative.ThrowIfFailed(attributes.SetString(ref linkKey, symbolicLink), "SetString(SymbolicLink)");

            MfNative.ThrowIfFailed(
                MfNative.MFEnumDeviceSources(attributes, out var activateArray, out var count),
                "MFEnumDeviceSources");

            try
            {
                if (count == 0 || activateArray == IntPtr.Zero)
                {
                    throw new CameraException("The camera is no longer connected.");
                }

                var slot = Marshal.ReadIntPtr(activateArray);
                var activate = (IMFActivate)Marshal.GetObjectForIUnknown(slot);
                Marshal.Release(slot);

                try
                {
                    var riid = typeof(IMFMediaSource).GUID;
                    var hr = activate.ActivateObject(ref riid, out var sourcePtr);
                    MfNative.ThrowIfFailed(hr, "IMFActivate::ActivateObject");

                    var source = (IMFMediaSource)Marshal.GetObjectForIUnknown(sourcePtr);
                    Marshal.Release(sourcePtr);
                    return source;
                }
                finally
                {
                    Marshal.ReleaseComObject(activate);
                }
            }
            finally
            {
                if (activateArray != IntPtr.Zero)
                {
                    MfNative.CoTaskMemFree(activateArray);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }
    }

    private static IMFSourceReader CreateReader(IMFMediaSource mediaSource)
    {
        MfNative.ThrowIfFailed(MfNative.MFCreateAttributes(out var attributes, 2), "MFCreateAttributes");

        try
        {
            // Lets the reader insert a decoder and a colour converter, which is
            // what makes an MJPEG-only camera able to hand us NV12.
            var advanced = MfConstants.SourceReaderEnableAdvancedVideoProcessing;
            attributes.SetUINT32(ref advanced, 1);

            // Frames are read on the CPU, so there is nothing to gain from DXVA
            // and a GPU surface would only have to be copied back.
            var disableDxva = MfConstants.SourceReaderDisableDxva;
            attributes.SetUINT32(ref disableDxva, 1);

            MfNative.ThrowIfFailed(
                MfNative.MFCreateSourceReaderFromMediaSource(mediaSource, attributes, out var reader),
                "MFCreateSourceReaderFromMediaSource");

            MfNative.ThrowIfFailed(
                reader.SetStreamSelection(MfConstants.FirstVideoStream, true),
                "IMFSourceReader::SetStreamSelection");

            return reader;
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }
    }

    /// <summary>
    /// Picks the closest supported mode and asks the reader for NV12 at that
    /// size and rate, falling back to BGRA and then to the camera's own format.
    /// </summary>
    private NegotiatedFormat ConfigureOutput(IMFSourceReader reader)
    {
        var modes = MfModeReader.ReadNativeModes(reader);
        if (modes.Count == 0)
        {
            throw new CameraException($"'{device.Name}' reported no usable capture modes.");
        }

        var mode = CameraModeSelector.Select(modes, settings)
            ?? throw new CameraException($"No capture mode on '{device.Name}' matched {settings}.");

        var frameRate = CameraModeSelector.ResolveFrameRate(mode, settings);

        if (mode.Width != settings.Width || mode.Height != settings.Height || Math.Abs(frameRate - settings.FrameRate) > 0.01)
        {
            logger.LogInformation(
                "{Camera} does not support {Requested}; using the closest mode {Selected}.",
                device.Name,
                settings,
                mode);
        }

        if (TrySetOutput(reader, mode, frameRate, MfConstants.VideoFormatNv12)
            || TrySetOutput(reader, mode, frameRate, MfConstants.VideoFormatRgb32))
        {
            return ReadBackFormat(reader, mode);
        }

        // Neither conversion was accepted. Take the camera's own format and let
        // the preview deal with it; Phase 2 will reject anything but NV12 when
        // the encoder is attached.
        logger.LogWarning(
            "{Camera} rejected both NV12 and BGRA output; falling back to its native {Format}.",
            device.Name,
            mode.Format);

        SetNativeMode(reader, mode);
        return ReadBackFormat(reader, mode);
    }

    private static bool TrySetOutput(IMFSourceReader reader, CameraMode mode, double frameRate, Guid subtype)
    {
        if (MfNative.Failed(MfNative.MFCreateMediaType(out var mediaType)))
        {
            return false;
        }

        try
        {
            var majorKey = MfConstants.MtMajorType;
            var video = MfConstants.MediaTypeVideo;
            mediaType.SetGUID(ref majorKey, ref video);

            var subtypeKey = MfConstants.MtSubtype;
            mediaType.SetGUID(ref subtypeKey, ref subtype);

            var sizeKey = MfConstants.MtFrameSize;
            mediaType.SetUINT64(ref sizeKey, MfNative.PackRatio((uint)mode.Width, (uint)mode.Height));

            if (frameRate > 0)
            {
                var (numerator, denominator) = MfModeReader.ToRatio(frameRate);
                var rateKey = MfConstants.MtFrameRate;
                mediaType.SetUINT64(ref rateKey, MfNative.PackRatio(numerator, denominator));
            }

            var interlaceKey = MfConstants.MtInterlaceMode;
            mediaType.SetUINT32(ref interlaceKey, InterlaceModeProgressive);

            var hr = reader.SetCurrentMediaType(MfConstants.FirstVideoStream, IntPtr.Zero, mediaType);
            return MfNative.Succeeded(hr);
        }
        finally
        {
            Marshal.ReleaseComObject(mediaType);
        }
    }

    private static void SetNativeMode(IMFSourceReader reader, CameraMode mode)
    {
        var hr = reader.GetNativeMediaType(MfConstants.FirstVideoStream, (uint)mode.NativeTypeIndex, out var nativeType);
        MfNative.ThrowIfFailed(hr, "IMFSourceReader::GetNativeMediaType");

        try
        {
            MfNative.ThrowIfFailed(
                reader.SetCurrentMediaType(MfConstants.FirstVideoStream, IntPtr.Zero, nativeType!),
                "IMFSourceReader::SetCurrentMediaType");
        }
        finally
        {
            if (nativeType is not null)
            {
                Marshal.ReleaseComObject(nativeType);
            }
        }
    }

    /// <summary>Reads back what the reader actually settled on, which may not be what was asked for.</summary>
    private static NegotiatedFormat ReadBackFormat(IMFSourceReader reader, CameraMode requested)
    {
        var hr = reader.GetCurrentMediaType(MfConstants.FirstVideoStream, out var mediaType);
        MfNative.ThrowIfFailed(hr, "IMFSourceReader::GetCurrentMediaType");

        try
        {
            var described = MfModeReader.Describe(mediaType!, requested.NativeTypeIndex)
                ?? throw new CameraException("The camera accepted a media type that could not be read back.");

            return new NegotiatedFormat(described, PackedStride(described.Format, described.Width));
        }
        finally
        {
            if (mediaType is not null)
            {
                Marshal.ReleaseComObject(mediaType);
            }
        }
    }

    /// <summary>Bytes per row when a buffer is tightly packed, used when the buffer reports no pitch of its own.</summary>
    private static int PackedStride(VideoPixelFormat format, int width) => format switch
    {
        VideoPixelFormat.Nv12 or VideoPixelFormat.I420 => width,
        VideoPixelFormat.Yuy2 or VideoPixelFormat.Uyvy => width * 2,
        VideoPixelFormat.Bgr24 => width * 3,
        VideoPixelFormat.Bgra32 => width * 4,
        _ => width,
    };

    private void PumpFrames(IMFSourceReader reader, NegotiatedFormat format)
    {
        var clock = Stopwatch.StartNew();
        var windowStart = clock.Elapsed;
        var windowFrames = 0L;

        while (!stopRequested)
        {
            var hr = reader.ReadSample(
                MfConstants.FirstVideoStream,
                controlFlags: 0,
                out _,
                out var streamFlags,
                out _,
                out var sample);

            if (MfNative.Failed(hr))
            {
                if (hr == MfNative.MfEVideoRecordingDeviceInvalidated)
                {
                    logger.LogWarning("CameraDisconnected: {Camera} was removed while streaming.", device.Name);
                    SetState(CameraState.Disconnected);
                    return;
                }

                throw MfNative.ToException(hr, "IMFSourceReader::ReadSample");
            }

            if ((streamFlags & MfConstants.StreamFlagEndOfStream) != 0)
            {
                logger.LogInformation("Capture stream for {Camera} ended.", device.Name);
                SetState(CameraState.Disconnected);
                return;
            }

            if (sample is null)
            {
                // A stream tick or a gap: no frame this time round, which is
                // normal and not a drop.
                continue;
            }

            try
            {
                DispatchSample(sample, format, clock.Elapsed);
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }

            windowFrames++;
            var elapsed = clock.Elapsed - windowStart;
            if (elapsed >= TimeSpan.FromSeconds(1))
            {
                Volatile.Write(ref measuredFrameRate, windowFrames / elapsed.TotalSeconds);
                windowStart = clock.Elapsed;
                windowFrames = 0;
            }
        }
    }

    private void DispatchSample(IMFSample sample, NegotiatedFormat format, TimeSpan timestamp)
    {
        if (MfNative.Failed(sample.GetBufferByIndex(0, out var buffer)) || buffer is null)
        {
            Interlocked.Increment(ref framesDropped);
            return;
        }

        try
        {
            // Lock2D hands back the driver's own buffer and its real pitch, so
            // nothing is copied. Buffers that do not support it, or that are
            // bottom-up, fall back to the flat lock.
            var twoD = buffer as IMF2DBuffer;
            if (twoD is not null
                && MfNative.Succeeded(twoD.Lock2D(out var scanline0, out var pitch))
                && pitch > 0)
            {
                try
                {
                    var length = BufferLength(format.Mode.Format, pitch, format.Mode.Height);
                    Publish(scanline0, length, pitch, format, timestamp);
                    return;
                }
                finally
                {
                    twoD.Unlock2D();
                }
            }

            if (MfNative.Failed(buffer.Lock(out var data, out _, out var currentLength)))
            {
                Interlocked.Increment(ref framesDropped);
                return;
            }

            try
            {
                Publish(data, currentLength, format.PackedStride, format, timestamp);
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    private static int BufferLength(VideoPixelFormat format, int stride, int height) => format switch
    {
        VideoPixelFormat.Nv12 or VideoPixelFormat.I420 => (stride * height) + (stride * ((height + 1) / 2)),
        _ => stride * height,
    };

    private unsafe void Publish(IntPtr data, int length, int stride, NegotiatedFormat format, TimeSpan timestamp)
    {
        if (data == IntPtr.Zero || length <= 0)
        {
            Interlocked.Increment(ref framesDropped);
            return;
        }

        Interlocked.Increment(ref framesCaptured);

        var span = new ReadOnlySpan<byte>((void*)data, length);
        var frame = new VideoFrame(span, format.Mode.Width, format.Mode.Height, stride, format.Mode.Format, timestamp);

        foreach (var sink in sinkSnapshot)
        {
            try
            {
                sink.OnFrame(in frame);
            }
            catch (Exception ex)
            {
                // One misbehaving consumer must not take capture down with it.
                logger.LogError(ex, "Frame sink {Sink} threw and was ignored.", sink.GetType().Name);
            }
        }

        FulfilSnapshot(span, stride, format, timestamp);
    }

    private void FulfilSnapshot(ReadOnlySpan<byte> data, int stride, NegotiatedFormat format, TimeSpan timestamp)
    {
        var request = Volatile.Read(ref pendingSnapshot);
        if (request is null)
        {
            return;
        }

        request.Result = new CameraFrameSnapshot(
            data.ToArray(),
            format.Mode.Width,
            format.Mode.Height,
            stride,
            format.Mode.Format,
            timestamp);

        Interlocked.CompareExchange(ref pendingSnapshot, null, request);
        request.Completed.Set();
    }

    /// <summary>What the reader agreed to produce, once negotiation is done.</summary>
    private readonly record struct NegotiatedFormat(CameraMode Mode, int PackedStride);

    private sealed class SnapshotRequest : IDisposable
    {
        internal ManualResetEventSlim Completed { get; } = new(false);

        internal CameraFrameSnapshot? Result { get; set; }

        public void Dispose() => Completed.Dispose();
    }
}

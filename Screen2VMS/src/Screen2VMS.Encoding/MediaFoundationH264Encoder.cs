using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Encoding;
using Screen2VMS.Core.Video;
using Screen2VMS.MediaFoundation;

namespace Screen2VMS.Encoding;

/// <summary>
/// H.264 encoder built on a Media Foundation Transform.
/// </summary>
/// <remarks>
/// <para>
/// The MFT is created and driven on one dedicated MTA thread. Capture pushes
/// frames into a short bounded queue and returns immediately, so a slow encode
/// costs a dropped frame rather than a stalled camera.
/// </para>
/// <para>
/// Frames are copied straight into the Media Foundation buffer on the capture
/// thread, which is the one copy the encoder needs anyway. Both threads live in
/// the MTA, so the sample can cross between them without marshalling.
/// </para>
/// </remarks>
public sealed class MediaFoundationH264Encoder : IVideoEncoder
{
    /// <summary>
    /// Frames allowed to wait for the encoder. Deliberately short: buffering
    /// deeper would add latency (spec 34) without ever catching up.
    /// </summary>
    private const int QueueCapacity = 3;

    private const int StopTimeoutMs = 5000;

    private readonly ILogger logger;
    private readonly BlockingCollection<PendingFrame> queue = new(QueueCapacity);
    private readonly ConcurrentBag<PendingFrame> pool = new();

    private Thread? encoderThread;
    private EncoderSettings settings = new();
    private volatile bool running;
    private volatile bool stopRequested;
    private volatile bool keyFrameRequested;
    private Exception? startFailure;

    private long framesEncoded;
    private long keyFramesEncoded;
    private long framesDropped;
    private double measuredBitrateKbps;
    private bool disposed;

    public MediaFoundationH264Encoder(ILogger<MediaFoundationH264Encoder>? logger = null)
    {
        this.logger = logger ?? NullLogger<MediaFoundationH264Encoder>.Instance;
    }

    public event EventHandler<EncodedFrame>? FrameEncoded;

    public VideoCodec Codec => VideoCodec.H264;

    public int Width => settings.Width;

    public int Height => settings.Height;

    public double FrameRate => settings.FrameRate;

    public int BitrateKbps => settings.BitrateKbps;

    public bool IsHardwareAccelerated { get; private set; }

    public bool IsRunning => running;

    public string EncoderName { get; private set; } = "(not started)";

    public byte[]? SequenceParameterSet { get; private set; }

    public byte[]? PictureParameterSet { get; private set; }

    public EncoderStatistics Statistics => new()
    {
        FramesEncoded = Interlocked.Read(ref framesEncoded),
        KeyFramesEncoded = Interlocked.Read(ref keyFramesEncoded),
        FramesDropped = Interlocked.Read(ref framesDropped),
        MeasuredBitrateKbps = Volatile.Read(ref measuredBitrateKbps),
    };

    public void Start(EncoderSettings encoderSettings)
    {
        ArgumentNullException.ThrowIfNull(encoderSettings);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (running)
        {
            return;
        }

        settings = encoderSettings;
        stopRequested = false;
        startFailure = null;
        Interlocked.Exchange(ref framesEncoded, 0);
        Interlocked.Exchange(ref keyFramesEncoded, 0);
        Interlocked.Exchange(ref framesDropped, 0);

        using var started = new ManualResetEventSlim(false);

        encoderThread = new Thread(() => EncodeLoop(started))
        {
            IsBackground = true,
            Name = "Screen2VMS.Encoder",
            Priority = ThreadPriority.AboveNormal,
        };

        encoderThread.SetApartmentState(ApartmentState.MTA);
        encoderThread.Start();
        started.Wait();

        var failure = startFailure;
        if (failure is not null)
        {
            encoderThread.Join(StopTimeoutMs);
            encoderThread = null;
            throw failure;
        }

        running = true;

        logger.LogInformation(
            "EncoderStarted: {Settings} using {Encoder} ({Acceleration})",
            settings,
            EncoderName,
            IsHardwareAccelerated ? "hardware" : "software");
    }

    public void Stop()
    {
        if (encoderThread is null)
        {
            return;
        }

        running = false;
        stopRequested = true;
        queue.CompleteAdding();

        if (!encoderThread.Join(StopTimeoutMs))
        {
            logger.LogWarning("Encoder thread did not stop within {Timeout} ms.", StopTimeoutMs);
        }

        encoderThread = null;
        logger.LogInformation("EncoderStopped");
    }

    public void RequestKeyFrame() => keyFrameRequested = true;

    /// <summary>
    /// Copies a captured frame into a Media Foundation sample and queues it.
    /// </summary>
    /// <remarks>
    /// Runs on the capture thread. When the queue is full the frame is dropped
    /// rather than waited on, because blocking here would stall capture for the
    /// preview and every other consumer too.
    /// </remarks>
    public void OnFrame(in VideoFrame frame)
    {
        if (!running || stopRequested)
        {
            return;
        }

        if (frame.Format != VideoPixelFormat.Nv12)
        {
            // Negotiation is supposed to guarantee NV12; anything else means
            // the camera refused it and the pipeline cannot encode.
            Interlocked.Increment(ref framesDropped);
            return;
        }

        if (queue.Count >= QueueCapacity)
        {
            Interlocked.Increment(ref framesDropped);
            return;
        }

        var required = Nv12FrameSize(frame.Width, frame.Height);
        var pending = RentFrame(required);

        try
        {
            if (!pending.CopyFrom(frame, required))
            {
                Interlocked.Increment(ref framesDropped);
                pool.Add(pending);
                return;
            }

            if (!queue.TryAdd(pending))
            {
                Interlocked.Increment(ref framesDropped);
                pool.Add(pending);
            }
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding raced with this frame during shutdown.
            pool.Add(pending);
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

        while (pool.TryTake(out var pending))
        {
            pending.Dispose();
        }

        queue.Dispose();
    }

    /// <summary>NV12 is one luma plane plus a half-height interleaved chroma plane.</summary>
    private static int Nv12FrameSize(int width, int height) => (width * height) + (width * ((height + 1) / 2));

    private PendingFrame RentFrame(int required)
    {
        while (pool.TryTake(out var pending))
        {
            if (pending.Capacity >= required)
            {
                return pending;
            }

            pending.Dispose();
        }

        return PendingFrame.Create(required);
    }

    private void EncodeLoop(ManualResetEventSlim started)
    {
        MfSession? session = null;
        IMFTransform? transform = null;
        var signalled = false;

        try
        {
            session = MfSession.Start();
            transform = CreateTransform();
            ConfigureTransform(transform);

            transform.ProcessMessage(MfEncoderConstants.MessageNotifyBeginStreaming, IntPtr.Zero);
            transform.ProcessMessage(MfEncoderConstants.MessageNotifyStartOfStream, IntPtr.Zero);

            started.Set();
            signalled = true;

            Pump(transform);
        }
        catch (Exception ex)
        {
            if (!signalled)
            {
                startFailure = ex;
            }
            else
            {
                logger.LogError(ex, "Encoding failed.");
            }
        }
        finally
        {
            if (!signalled)
            {
                started.Set();
            }

            if (transform is not null)
            {
                try
                {
                    transform.ProcessMessage(MfEncoderConstants.MessageNotifyEndOfStream, IntPtr.Zero);
                    transform.ProcessMessage(MfEncoderConstants.MessageCommandDrain, IntPtr.Zero);
                    transform.ProcessMessage(MfEncoderConstants.MessageNotifyEndStreaming, IntPtr.Zero);
                }
                catch (COMException)
                {
                    // Shutting down: a transform that refuses these is harmless.
                }

                Marshal.ReleaseComObject(transform);
            }

            session?.Dispose();
        }
    }

    /// <summary>
    /// Finds a synchronous H.264 encoder MFT.
    /// </summary>
    /// <remarks>
    /// Only synchronous transforms are requested. Hardware encoders usually
    /// present as asynchronous MFTs needing an event-driven pump, which is
    /// scheduled for the hardware-optimisation milestone; the Microsoft
    /// software encoder is always present as the fallback (spec 7).
    /// </remarks>
    private IMFTransform CreateTransform()
    {
        var outputType = new MftRegisterTypeInfo
        {
            MajorType = MfConstants.MediaTypeVideo,
            Subtype = MfConstants.VideoFormatH264,
        };

        var outputTypePtr = Marshal.AllocHGlobal(Marshal.SizeOf<MftRegisterTypeInfo>());

        try
        {
            Marshal.StructureToPtr(outputType, outputTypePtr, fDeleteOld: false);

            var flags = MfEncoderConstants.EnumFlagSyncMft
                | MfEncoderConstants.EnumFlagLocalMft
                | MfEncoderConstants.EnumFlagSortAndFilter;

            MfNative.ThrowIfFailed(
                MfNative.MFTEnumEx(
                    MfEncoderConstants.TransformCategoryVideoEncoder,
                    flags,
                    IntPtr.Zero,
                    outputTypePtr,
                    out var activateArray,
                    out var count),
                "MFTEnumEx");

            if (count == 0 || activateArray == IntPtr.Zero)
            {
                throw new EncoderException(
                    "No H.264 encoder is available on this system. " +
                    "Media Foundation and the Media Feature Pack must be installed.");
            }

            try
            {
                return ActivateFirstUsable(activateArray, count);
            }
            finally
            {
                MfNative.CoTaskMemFree(activateArray);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(outputTypePtr);
        }
    }

    private IMFTransform ActivateFirstUsable(IntPtr activateArray, uint count)
    {
        Exception? lastFailure = null;

        for (var i = 0; i < count; i++)
        {
            var slot = Marshal.ReadIntPtr(activateArray, i * IntPtr.Size);
            if (slot == IntPtr.Zero)
            {
                continue;
            }

            var activate = (IMFActivate)Marshal.GetObjectForIUnknown(slot);
            Marshal.Release(slot);

            try
            {
                var name = MfNative.GetStringAttribute(activate, MfEncoderConstants.TransformFriendlyNameAttribute)
                    ?? "H.264 Encoder MFT";

                var hardwareUrl = MfNative.GetStringAttribute(
                    activate,
                    MfEncoderConstants.TransformEnumHardwareUrlAttribute);

                var riid = typeof(IMFTransform).GUID;
                var hr = activate.ActivateObject(ref riid, out var transformPtr);
                if (MfNative.Failed(hr) || transformPtr == IntPtr.Zero)
                {
                    lastFailure = MfNative.ToException(hr, $"Activating '{name}'");
                    continue;
                }

                var transform = (IMFTransform)Marshal.GetObjectForIUnknown(transformPtr);
                Marshal.Release(transformPtr);

                EncoderName = name;
                IsHardwareAccelerated = !string.IsNullOrEmpty(hardwareUrl);
                return transform;
            }
            finally
            {
                Marshal.ReleaseComObject(activate);
            }
        }

        throw lastFailure ?? new EncoderException("No H.264 encoder MFT could be activated.");
    }

    /// <summary>
    /// Negotiates the encoder's media types and reads back the parameter sets.
    /// </summary>
    /// <remarks>
    /// The output type must be set before the input type. Setting them the
    /// other way round fails with MF_E_TRANSFORM_TYPE_NOT_SET on every H.264
    /// encoder.
    /// </remarks>
    private void ConfigureTransform(IMFTransform transform)
    {
        SetLowLatency(transform);
        SetOutputType(transform);
        SetInputType(transform);
        ConfigureCodecApi(transform);
        ReadParameterSets(transform);
    }

    private void SetLowLatency(IMFTransform transform)
    {
        if (MfNative.Failed(transform.GetAttributes(out var attributes)) || attributes is null)
        {
            return;
        }

        try
        {
            var key = MfEncoderConstants.LowLatency;
            attributes.SetUINT32(ref key, 1);
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }
    }

    private void SetOutputType(IMFTransform transform)
    {
        MfNative.ThrowIfFailed(MfNative.MFCreateMediaType(out var mediaType), "MFCreateMediaType");

        try
        {
            var majorKey = MfConstants.MtMajorType;
            var video = MfConstants.MediaTypeVideo;
            mediaType.SetGUID(ref majorKey, ref video);

            var subtypeKey = MfConstants.MtSubtype;
            var h264 = MfConstants.VideoFormatH264;
            mediaType.SetGUID(ref subtypeKey, ref h264);

            var bitrateKey = MfEncoderConstants.MtAvgBitrate;
            mediaType.SetUINT32(ref bitrateKey, (uint)(settings.BitrateKbps * 1000));

            var sizeKey = MfConstants.MtFrameSize;
            mediaType.SetUINT64(ref sizeKey, MfNative.PackRatio((uint)settings.Width, (uint)settings.Height));

            var (numerator, denominator) = MfModeReader.ToRatio(settings.FrameRate);
            var rateKey = MfConstants.MtFrameRate;
            mediaType.SetUINT64(ref rateKey, MfNative.PackRatio(numerator, denominator));

            var interlaceKey = MfConstants.MtInterlaceMode;
            mediaType.SetUINT32(ref interlaceKey, MfEncoderConstants.InterlaceModeProgressive);

            var aspectKey = MfConstants.MtPixelAspectRatio;
            mediaType.SetUINT64(ref aspectKey, MfNative.PackRatio(1, 1));

            var profileKey = MfEncoderConstants.MtMpeg2Profile;
            mediaType.SetUINT32(ref profileKey, (uint)settings.Profile);

            var keyframeKey = MfEncoderConstants.MtMaxKeyframeSpacing;
            mediaType.SetUINT32(ref keyframeKey, (uint)settings.GopLength);

            var hr = transform.SetOutputType(0, mediaType, 0);
            if (MfNative.Failed(hr) && settings.Profile != MfEncoderConstants.H264ProfileBaseline)
            {
                // Some encoders reject Main at unusual sizes. Baseline is
                // universally accepted and every VMS decodes it (spec 11).
                logger.LogWarning(
                    "Encoder rejected H.264 profile {Profile} (HRESULT 0x{HResult:X8}); falling back to Baseline.",
                    settings.Profile,
                    hr);

                mediaType.SetUINT32(ref profileKey, MfEncoderConstants.H264ProfileBaseline);
                hr = transform.SetOutputType(0, mediaType, 0);
            }

            MfNative.ThrowIfFailed(hr, "IMFTransform::SetOutputType");
        }
        finally
        {
            Marshal.ReleaseComObject(mediaType);
        }
    }

    private void SetInputType(IMFTransform transform)
    {
        MfNative.ThrowIfFailed(MfNative.MFCreateMediaType(out var mediaType), "MFCreateMediaType");

        try
        {
            var majorKey = MfConstants.MtMajorType;
            var video = MfConstants.MediaTypeVideo;
            mediaType.SetGUID(ref majorKey, ref video);

            var subtypeKey = MfConstants.MtSubtype;
            var nv12 = MfConstants.VideoFormatNv12;
            mediaType.SetGUID(ref subtypeKey, ref nv12);

            var sizeKey = MfConstants.MtFrameSize;
            mediaType.SetUINT64(ref sizeKey, MfNative.PackRatio((uint)settings.Width, (uint)settings.Height));

            var (numerator, denominator) = MfModeReader.ToRatio(settings.FrameRate);
            var rateKey = MfConstants.MtFrameRate;
            mediaType.SetUINT64(ref rateKey, MfNative.PackRatio(numerator, denominator));

            var interlaceKey = MfConstants.MtInterlaceMode;
            mediaType.SetUINT32(ref interlaceKey, MfEncoderConstants.InterlaceModeProgressive);

            var aspectKey = MfConstants.MtPixelAspectRatio;
            mediaType.SetUINT64(ref aspectKey, MfNative.PackRatio(1, 1));

            MfNative.ThrowIfFailed(transform.SetInputType(0, mediaType, 0), "IMFTransform::SetInputType");
        }
        finally
        {
            Marshal.ReleaseComObject(mediaType);
        }
    }

    /// <summary>Applies rate control and GOP settings the media type cannot express.</summary>
    private void ConfigureCodecApi(IMFTransform transform)
    {
        if (transform is not ICodecAPI codec)
        {
            return;
        }

        SetCodecValue(codec, MfEncoderConstants.AVEncCommonRateControlMode, MfEncoderConstants.RateControlModeCbr);
        SetCodecValue(codec, MfEncoderConstants.AVEncCommonMeanBitRate, (uint)(settings.BitrateKbps * 1000));
        SetCodecValue(codec, MfEncoderConstants.AVEncMPVGOPSize, (uint)settings.GopLength);
        SetCodecBoolean(codec, MfEncoderConstants.AVEncCommonLowLatency, true);
    }

    private static void SetCodecValue(ICodecAPI codec, Guid property, uint value)
    {
        var variant = MfVariant.FromUInt32(value);
        SetCodecVariant(codec, property, variant);
    }

    private static void SetCodecBoolean(ICodecAPI codec, Guid property, bool value)
    {
        var variant = MfVariant.FromBoolean(value);
        SetCodecVariant(codec, property, variant);
    }

    private static void SetCodecVariant(ICodecAPI codec, Guid property, MfVariant variant)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MfVariant>());

        try
        {
            Marshal.StructureToPtr(variant, pointer, fDeleteOld: false);

            // Not every encoder supports every property. An unsupported one is
            // a missed optimisation, never a reason to fail.
            codec.SetValue(ref property, pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// Reads SPS and PPS from the negotiated output type.
    /// </summary>
    /// <remarks>
    /// MF_MT_MPEG_SEQUENCE_HEADER carries them in Annex-B form. Having them
    /// before the first frame lets the RTSP layer publish a complete SDP as
    /// soon as it starts, rather than waiting for the first IDR.
    /// </remarks>
    private void ReadParameterSets(IMFTransform transform)
    {
        if (MfNative.Failed(transform.GetOutputCurrentType(0, out var mediaType)) || mediaType is null)
        {
            return;
        }

        try
        {
            var key = MfEncoderConstants.MtMpegSequenceHeader;
            if (MfNative.Failed(mediaType.GetBlobSize(ref key, out var size)) || size == 0)
            {
                return;
            }

            var blob = new byte[size];
            var written = 0u;
            if (MfNative.Failed(mediaType.GetBlob(ref key, blob, size, ref written)))
            {
                return;
            }

            var (sps, pps) = H264Bitstream.ExtractParameterSets(blob);
            SequenceParameterSet = sps;
            PictureParameterSet = pps;

            if (sps is not null)
            {
                var (profileIdc, profileIop, levelIdc) = H264Bitstream.ParseProfileLevel(sps);
                logger.LogInformation(
                    "Encoder parameter sets ready: profile-level-id {ProfileLevelId}, SPS {SpsBytes} bytes, PPS {PpsBytes} bytes",
                    H264Bitstream.FormatProfileLevelId(profileIdc, profileIop, levelIdc),
                    sps.Length,
                    pps?.Length ?? 0);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(mediaType);
        }
    }

    private void Pump(IMFTransform transform)
    {
        var clock = Stopwatch.StartNew();
        var windowStart = clock.Elapsed;
        var windowBytes = 0L;

        MfNative.ThrowIfFailed(transform.GetOutputStreamInfo(0, out var outputInfo), "GetOutputStreamInfo");
        var transformAllocates =
            (outputInfo.Flags & (MfEncoderConstants.OutputStreamProvidesSamples
                | MfEncoderConstants.OutputStreamCanProvideSamples)) != 0;

        foreach (var pending in queue.GetConsumingEnumerable())
        {
            try
            {
                if (stopRequested)
                {
                    break;
                }

                if (keyFrameRequested)
                {
                    keyFrameRequested = false;
                    ForceKeyFrame(transform);
                }

                var hr = transform.ProcessInput(0, pending.Sample, 0);
                if (MfNative.Failed(hr))
                {
                    Interlocked.Increment(ref framesDropped);
                    continue;
                }

                windowBytes += DrainOutputs(transform, transformAllocates, outputInfo.Size);

                var elapsed = clock.Elapsed - windowStart;
                if (elapsed >= TimeSpan.FromSeconds(1))
                {
                    Volatile.Write(ref measuredBitrateKbps, windowBytes * 8 / elapsed.TotalSeconds / 1000);
                    windowStart = clock.Elapsed;
                    windowBytes = 0;
                }
            }
            finally
            {
                // Safe to reuse now: the transform has consumed the input and
                // every output it produced from it has been drained.
                pool.Add(pending);
            }
        }
    }

    private static void ForceKeyFrame(IMFTransform transform)
    {
        if (transform is ICodecAPI codec)
        {
            SetCodecValue(codec, MfEncoderConstants.AVEncVideoForceKeyFrame, 1);
        }
    }

    private long DrainOutputs(IMFTransform transform, bool transformAllocates, uint outputSize)
    {
        var bytes = 0L;

        while (true)
        {
            IMFSample? allocated = null;
            var buffers = new MftOutputDataBuffer[1];
            buffers[0].StreamId = 0;

            if (!transformAllocates)
            {
                // The transform expects us to supply the output sample.
                MfNative.ThrowIfFailed(MfNative.MFCreateSample(out allocated), "MFCreateSample");
                MfNative.ThrowIfFailed(
                    MfNative.MFCreateMemoryBuffer((int)Math.Max(outputSize, 1u), out var buffer),
                    "MFCreateMemoryBuffer");

                try
                {
                    AddBufferToSample(allocated, buffer);
                }
                finally
                {
                    Marshal.ReleaseComObject(buffer);
                }

                buffers[0].Sample = allocated;
            }

            var hr = transform.ProcessOutput(0, 1, buffers, out _);

            if (hr == MfEncoderConstants.MfETransformNeedMoreInput)
            {
                ReleaseOutput(buffers, allocated);
                return bytes;
            }

            if (hr == MfEncoderConstants.MfETransformStreamChange)
            {
                // The encoder renegotiated its output; re-read the parameter
                // sets so the SDP stays truthful.
                ReleaseOutput(buffers, allocated);
                ReadParameterSets(transform);
                continue;
            }

            if (MfNative.Failed(hr) || buffers[0].Sample is null)
            {
                ReleaseOutput(buffers, allocated);
                return bytes;
            }

            try
            {
                bytes += PublishSample(buffers[0].Sample!);
            }
            finally
            {
                ReleaseOutput(buffers, allocated);
            }
        }
    }

    private static void ReleaseOutput(MftOutputDataBuffer[] buffers, IMFSample? allocated)
    {
        var sample = buffers[0].Sample;
        buffers[0].Sample = null;

        if (sample is not null && !ReferenceEquals(sample, allocated))
        {
            Marshal.ReleaseComObject(sample);
        }

        if (allocated is not null)
        {
            Marshal.ReleaseComObject(allocated);
        }

        if (buffers[0].Events is not null)
        {
            Marshal.ReleaseComObject(buffers[0].Events!);
            buffers[0].Events = null;
        }
    }

    private static void AddBufferToSample(IMFSample sample, IMFMediaBuffer buffer) =>
        MfNative.ThrowIfFailed(sample.AddBuffer(buffer), "IMFSample::AddBuffer");

    private long PublishSample(IMFSample sample)
    {
        if (MfNative.Failed(sample.ConvertToContiguousBuffer(out var buffer)) || buffer is null)
        {
            return 0;
        }

        try
        {
            if (MfNative.Failed(buffer.Lock(out var data, out _, out var length)) || length <= 0)
            {
                return 0;
            }

            try
            {
                var payload = new byte[length];
                Marshal.Copy(data, payload, 0, length);

                var timestamp = MfNative.Succeeded(sample.GetSampleTime(out var sampleTime))
                    ? TimeSpan.FromTicks(sampleTime)
                    : TimeSpan.Zero;

                var isKeyFrame = H264Bitstream.ContainsKeyFrame(payload);

                if (isKeyFrame)
                {
                    Interlocked.Increment(ref keyFramesEncoded);

                    // Some encoders only emit parameter sets alongside the first
                    // IDR rather than on the output type, so pick them up here
                    // if they are still missing.
                    if (SequenceParameterSet is null || PictureParameterSet is null)
                    {
                        var (sps, pps) = H264Bitstream.ExtractParameterSets(payload);
                        SequenceParameterSet ??= sps;
                        PictureParameterSet ??= pps;
                    }
                }

                Interlocked.Increment(ref framesEncoded);
                FrameEncoded?.Invoke(this, new EncodedFrame(payload, length, isKeyFrame, timestamp));

                return length;
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

    /// <summary>A Media Foundation sample and its buffer, reused across frames.</summary>
    private sealed class PendingFrame : IDisposable
    {
        private PendingFrame(IMFSample sample, IMFMediaBuffer buffer, int capacity)
        {
            Sample = sample;
            Buffer = buffer;
            Capacity = capacity;
        }

        internal IMFSample Sample { get; }

        internal IMFMediaBuffer Buffer { get; }

        internal int Capacity { get; }

        internal static PendingFrame Create(int capacity)
        {
            MfNative.ThrowIfFailed(MfNative.MFCreateSample(out var sample), "MFCreateSample");
            MfNative.ThrowIfFailed(MfNative.MFCreateMemoryBuffer(capacity, out var buffer), "MFCreateMemoryBuffer");

            AddBufferToSample(sample, buffer);
            return new PendingFrame(sample, buffer, capacity);
        }

        /// <summary>
        /// Copies an NV12 frame in, collapsing any row padding.
        /// </summary>
        /// <remarks>
        /// The encoder input type declares a packed frame, so a driver buffer
        /// with a wider stride has to be tightened row by row here.
        /// </remarks>
        internal bool CopyFrom(in VideoFrame frame, int required)
        {
            if (MfNative.Failed(Buffer.Lock(out var destination, out var maxLength, out _)) || maxLength < required)
            {
                Buffer.Unlock();
                return false;
            }

            try
            {
                var packedStride = frame.Width;

                if (frame.Stride == packedStride)
                {
                    Marshal.Copy(ToArray(frame.Data, required), 0, destination, required);
                }
                else
                {
                    CopyPlanes(frame, destination, packedStride);
                }

                Buffer.SetCurrentLength(required);

                // Sample times drive the encoder's rate control and become the
                // RTP timestamps downstream, so they must be real.
                Sample.SetSampleTime(frame.Timestamp.Ticks);
                Sample.SetSampleDuration(0);
                return true;
            }
            finally
            {
                Buffer.Unlock();
            }
        }

        private static byte[] ToArray(ReadOnlySpan<byte> data, int length) =>
            data.Length == length ? data.ToArray() : data[..length].ToArray();

        private static void CopyPlanes(in VideoFrame frame, IntPtr destination, int packedStride)
        {
            var row = new byte[packedStride];
            var offset = 0;

            for (var y = 0; y < frame.Height; y++)
            {
                frame.Data.Slice(y * frame.Stride, packedStride).CopyTo(row);
                Marshal.Copy(row, 0, destination + offset, packedStride);
                offset += packedStride;
            }

            var chromaRows = (frame.Height + 1) / 2;
            var chromaStart = frame.Stride * frame.Height;

            for (var y = 0; y < chromaRows; y++)
            {
                frame.Data.Slice(chromaStart + (y * frame.Stride), packedStride).CopyTo(row);
                Marshal.Copy(row, 0, destination + offset, packedStride);
                offset += packedStride;
            }
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(Buffer);
            Marshal.ReleaseComObject(Sample);
        }
    }
}

/// <summary>An encoder operation failed.</summary>
public sealed class EncoderException : Exception
{
    public EncoderException(string message)
        : base(message)
    {
    }

    public EncoderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

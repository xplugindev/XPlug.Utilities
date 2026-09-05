using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screen2VMS.Core.Cameras;
using Screen2VMS.Core.Video;

namespace Screen2VMS.App.Views;

/// <summary>
/// Renders captured frames into a <see cref="WriteableBitmap"/> for the preview.
/// </summary>
/// <remarks>
/// <para>
/// Preview is a best-effort consumer, not part of the streaming path. It runs
/// at a reduced size and rate and drops frames whenever the UI thread is
/// already busy, so a slow or minimised window can never slow capture down.
/// </para>
/// <para>
/// Conversion happens on the capture thread, into a pooled buffer that the UI
/// thread hands back when it has finished with it. Converting on the UI thread
/// instead would stutter the whole window.
/// </para>
/// </remarks>
public sealed class PreviewSink : IVideoFrameSink, IDisposable
{
    /// <summary>Preview is capped at this width; anything wider is scaled down.</summary>
    private const int MaxPreviewWidth = 640;

    /// <summary>Preview frame rate cap. The stream keeps its own rate.</summary>
    private const int MaxPreviewFps = 25;

    private readonly Dispatcher dispatcher;
    private readonly ConcurrentBag<byte[]> bufferPool = new();
    private readonly long minimumTicksBetweenFrames = TimeSpan.TicksPerSecond / MaxPreviewFps;

    private long lastFrameTicks;
    private int renderPending;
    private WriteableBitmap? bitmap;
    private bool disposed;

    public PreviewSink(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>Raised on the UI thread when <see cref="Image"/> is replaced.</summary>
    public event EventHandler? ImageChanged;

    /// <summary>The preview bitmap. Only valid on the UI thread.</summary>
    public ImageSource? Image => bitmap;

    public void OnFrame(in VideoFrame frame)
    {
        if (disposed)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (now - Volatile.Read(ref lastFrameTicks) < minimumTicksBetweenFrames)
        {
            return;
        }

        // If the UI has not finished the previous frame, skip this one rather
        // than queueing work that is already stale.
        if (Interlocked.CompareExchange(ref renderPending, 1, 0) != 0)
        {
            return;
        }

        Volatile.Write(ref lastFrameTicks, now);

        var (width, height) = Nv12Converter.PreviewSizeFor(frame.Width, frame.Height, MaxPreviewWidth);
        var required = width * height * Nv12Converter.BytesPerPixel;
        var buffer = Rent(required);

        try
        {
            switch (frame.Format)
            {
                case VideoPixelFormat.Nv12:
                    Nv12Converter.ToBgra32(
                        frame.Data,
                        frame.Width,
                        frame.Height,
                        frame.Stride,
                        buffer,
                        width,
                        height);
                    break;

                case VideoPixelFormat.Bgra32:
                    CopyBgra(frame, buffer, width, height);
                    break;

                default:
                    // Negotiation guarantees NV12 or BGRA; anything else means
                    // the camera refused both and preview cannot show it.
                    Return(buffer);
                    Volatile.Write(ref renderPending, 0);
                    return;
            }
        }
        catch
        {
            Return(buffer);
            Volatile.Write(ref renderPending, 0);
            throw;
        }

        dispatcher.BeginInvoke(new Action(() => Present(buffer, width, height)), DispatcherPriority.Render);
    }

    public void Dispose()
    {
        disposed = true;
        bufferPool.Clear();
    }

    /// <summary>Nearest-neighbour downscale of an already-BGRA frame.</summary>
    private static void CopyBgra(in VideoFrame frame, Span<byte> destination, int width, int height)
    {
        var xStep = ((long)frame.Width << 16) / width;
        var yStep = ((long)frame.Height << 16) / height;

        for (var y = 0; y < height; y++)
        {
            var sourceRow = (int)((y * yStep) >> 16) * frame.Stride;
            var destinationRow = y * width * Nv12Converter.BytesPerPixel;

            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceRow + ((int)((x * xStep) >> 16) * Nv12Converter.BytesPerPixel);
                var destinationIndex = destinationRow + (x * Nv12Converter.BytesPerPixel);

                destination[destinationIndex] = frame.Data[sourceIndex];
                destination[destinationIndex + 1] = frame.Data[sourceIndex + 1];
                destination[destinationIndex + 2] = frame.Data[sourceIndex + 2];
                destination[destinationIndex + 3] = 255;
            }
        }
    }

    private void Present(byte[] buffer, int width, int height)
    {
        try
        {
            if (disposed)
            {
                return;
            }

            if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            {
                bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, palette: null);
                ImageChanged?.Invoke(this, EventArgs.Empty);
            }

            bitmap.WritePixels(
                new System.Windows.Int32Rect(0, 0, width, height),
                buffer,
                width * Nv12Converter.BytesPerPixel,
                0);
        }
        finally
        {
            Return(buffer);
            Volatile.Write(ref renderPending, 0);
        }
    }

    private byte[] Rent(int size)
    {
        while (bufferPool.TryTake(out var buffer))
        {
            if (buffer.Length >= size)
            {
                return buffer;
            }

            // Wrong size: the preview was resized, so let this one go.
        }

        return new byte[size];
    }

    private void Return(byte[] buffer)
    {
        // Two in flight is the most this can need: one being converted, one
        // being drawn.
        if (bufferPool.Count < 2)
        {
            bufferPool.Add(buffer);
        }
    }
}

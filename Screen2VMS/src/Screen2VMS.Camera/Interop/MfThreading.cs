using System.Runtime.ExceptionServices;

namespace Screen2VMS.Camera.Interop;

/// <summary>
/// Scopes an MFStartup/MFShutdown pair to a single thread.
/// </summary>
/// <remarks>
/// MFStartup is reference counted per process but also initialises COM on the
/// calling thread, so every thread that touches Media Foundation opens its own
/// session and closes it before exiting.
/// </remarks>
internal sealed class MfSession : IDisposable
{
    private bool disposed;

    private MfSession()
    {
    }

    /// <summary>Starts Media Foundation on the calling thread.</summary>
    internal static MfSession Start()
    {
        MfNative.ThrowIfFailed(
            MfNative.MFStartup(MfConstants.MfVersion, MfConstants.MfStartupLite),
            "MFStartup");

        return new MfSession();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        MfNative.MFShutdown();
    }
}

/// <summary>
/// Runs a delegate on a dedicated MTA thread and waits for it.
/// </summary>
/// <remarks>
/// Media Foundation objects are not agile: one created on the WPF UI thread,
/// which is STA, would be marshalled through a message pump and can deadlock.
/// Enumeration is infrequent and user-initiated, so paying for a thread per
/// call is cheaper than maintaining a pump. Capture does not use this - it owns
/// a long-lived thread of its own.
/// </remarks>
internal static class MtaRunner
{
    internal static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var result = default(T);
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var session = MfSession.Start();
                result = work();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Screen2VMS.MediaFoundation",
        };

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();

        failure?.Throw();
        return result!;
    }
}

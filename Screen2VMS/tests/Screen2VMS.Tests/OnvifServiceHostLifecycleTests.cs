using System.Net;
using System.Net.Sockets;
using Screen2VMS.Core.Onvif;
using Screen2VMS.Onvif;

namespace Screen2VMS.Tests;

/// <summary>
/// Guards the ONVIF host against the shutdown deadlock.
/// </summary>
/// <remarks>
/// <para>
/// The host is started and stopped from a button click, which runs on the WPF
/// dispatcher thread. That thread has a synchronisation context, and blocking
/// on an asynchronous host operation from it deadlocks: the host's
/// continuations are posted back to the very thread that is blocked waiting for
/// them. The application stopped responding and looked, to anyone using it,
/// exactly like a crash.
/// </para>
/// <para>
/// These tests install a single-threaded synchronisation context with the same
/// shape as WPF's and drive the host through it. If anyone reinstates a bare
/// <c>.GetAwaiter().GetResult()</c> on a host call, they hang here instead of
/// in front of a user.
/// </para>
/// </remarks>
public class OnvifServiceHostLifecycleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    /// <summary>
    /// A start and stop that talk to the network finish in a couple of seconds.
    /// </summary>
    /// <remarks>
    /// This budget is the actual assertion. The host guards each asynchronous
    /// call with its own timeout, so a deadlocked implementation does not hang
    /// forever - it sits out those timeouts and then reports success, which is
    /// indistinguishable from working unless the clock is checked. A deadlocked
    /// single blocked stop burns its fifteen-second timeout, and a healthy
    /// start and stop takes about two, so this budget separates them cleanly.
    /// </remarks>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public void StartAndStop_DoNotDeadlockUnderASynchronizationContext()
    {
        var elapsed = RunUnderDispatcherLikeContext(() =>
        {
            var port = FindFreePort();
            using var host = CreateHost(port);

            host.Start(port);
            Assert.True(host.IsRunning);

            host.Stop();
            Assert.False(host.IsRunning);
        });

        AssertPromptly(elapsed, "start and stop");
    }

    [Fact]
    public void Stop_ReleasesThePortSoTheHostCanBeStartedAgain()
    {
        // A stop that leaves the socket bound is what made restarting after
        // Stop fail, and it is invisible unless the port is reused.
        var elapsed = RunUnderDispatcherLikeContext(() =>
        {
            var port = FindFreePort();

            using (var first = CreateHost(port))
            {
                first.Start(port);
                first.Stop();
            }

            using var second = CreateHost(port);
            second.Start(port);
            Assert.True(second.IsRunning);
            second.Stop();
        });

        AssertPromptly(elapsed, "restart on the same port");
    }

    [Fact]
    public void Dispose_IsSafeWithoutAnExplicitStop()
    {
        var elapsed = RunUnderDispatcherLikeContext(() =>
        {
            var port = FindFreePort();
            var host = CreateHost(port);

            host.Start(port);
            host.Dispose();

            // Disposing twice happens on application exit, after the window has
            // already stopped the runtime.
            host.Dispose();
        });

        AssertPromptly(elapsed, "dispose");
    }

    private static void AssertPromptly(TimeSpan elapsed, string what)
    {
        Assert.True(
            elapsed < Budget,
            $"The ONVIF host took {elapsed.TotalSeconds:0.0}s to {what}, over the {Budget.TotalSeconds:0}s budget. " +
            "That is what a blocked synchronisation context looks like: the internal timeouts expire " +
            "one by one and the call then reports success.");
    }

    [Fact]
    public void Start_RefusesToRunWithoutAPassword()
    {
        // Spec 24: no universal default password, so the host must not come up
        // with an empty one.
        var port = FindFreePort();
        var options = new OnvifHostOptions { Port = port, UserName = "admin", Password = string.Empty };
        using var host = new OnvifServiceHost(new StubDeviceContext(), options, NullLoggerFactory());

        Assert.Throws<InvalidOperationException>(() => host.Start(port));
    }

    private static OnvifServiceHost CreateHost(int port) =>
        new(
            new StubDeviceContext(),
            new OnvifHostOptions
            {
                Port = port,
                UserName = "admin",
                Password = "test-password",
                FallbackAddress = "127.0.0.1",
                RtspPort = 8554,
                RtspPath = "/live",
            },
            NullLoggerFactory());

    private static Microsoft.Extensions.Logging.ILoggerFactory NullLoggerFactory() =>
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    /// <summary>An ephemeral port the operating system has just confirmed is free.</summary>
    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a thread whose synchronisation context
    /// only drains while that thread is idle, which is how the WPF dispatcher
    /// behaves.
    /// </summary>
    /// <returns>How long the work took, which is what the tests assert on.</returns>
    private static TimeSpan RunUnderDispatcherLikeContext(Action action)
    {
        Exception? failure = null;
        using var finished = new ManualResetEventSlim(false);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var thread = new Thread(() =>
        {
            var context = new SingleThreadedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "DispatcherLike",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completed = finished.Wait(Patience);
        clock.Stop();

        if (failure is not null)
        {
            throw failure;
        }

        Assert.True(completed, $"The work did not finish within {Patience.TotalSeconds:0}s; it is wedged.");

        return clock.Elapsed;
    }

    /// <summary>
    /// A synchronisation context that queues continuations for one thread and
    /// never runs them on its own, the way a blocked UI thread behaves.
    /// </summary>
    private sealed class SingleThreadedSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            // Deliberately never drained: anything that depends on this running
            // while the caller blocks is the deadlock being tested for.
            queue.TryAdd((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>Minimal device context; these tests exercise the host's lifecycle only.</summary>
    private sealed class StubDeviceContext : IOnvifDeviceContext
    {
        public OnvifDeviceInfo DeviceInfo { get; } = new()
        {
            SerialNumber = "S2V-TEST00000001",
            MacAddress = "02AABBCCDDEE",
        };

        public OnvifVideoConfiguration Video { get; private set; } = new();

        public bool IsStreaming => false;

        public string GetStreamUri(IPAddress client) => "rtsp://127.0.0.1:8554/live";

        public string GetSnapshotUri(IPAddress client) => "http://127.0.0.1:8000/onvif/snapshot";

        public byte[]? CaptureSnapshotJpeg() => null;

        public void RequestSynchronizationPoint()
        {
        }

        public void ApplyVideoConfiguration(OnvifVideoConfiguration configuration) => Video = configuration;
    }
}

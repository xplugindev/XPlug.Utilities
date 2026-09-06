using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rtsp.Messages;
using Screen2VMS.Core.Encoding;
using Screen2VMS.Core.Streaming;
using Screen2VMS.Core.Video;
using SharpRTSPServer;

namespace Screen2VMS.Rtsp;

/// <summary>
/// Serves the encoded H.264 stream over RTSP.
/// </summary>
/// <remarks>
/// <para>
/// A thin adapter over SharpRTSPServer, which handles the RTSP verbs, RTP
/// packetisation, interleaved TCP transport and session keepalive. Screen2VMS
/// supplies encoded frames and the parameter sets, and nothing here ever
/// touches the camera (spec 43).
/// </para>
/// <para>
/// One encode feeds every client (spec 33): frames arrive once and the server
/// fans them out.
/// </para>
/// </remarks>
public sealed class SharpRtspVideoServer : IRtspServer
{
    /// <summary>RTP clock for H.264, fixed by RFC 6184.</summary>
    private const int VideoClockHz = 90000;

    private readonly ILogger logger;
    private readonly ILoggerFactory loggerFactory;
    private readonly object stateLock = new();

    private RTSPServer? server;
    private RTSPStreamSource? streamSource;
    private H264Track? track;
    private string streamId = "live";
    private int clientCount;
    private bool disposed;

    public SharpRtspVideoServer(ILoggerFactory? loggerFactory = null)
    {
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger<SharpRtspVideoServer>();
    }

    public event EventHandler? ClientConnected;

    public event EventHandler? ClientDisconnected;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public string Path { get; private set; } = "/live";

    public int ClientCount => Volatile.Read(ref clientCount);

    /// <summary>Credentials required of RTSP clients, or null for anonymous access.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public void Start(int port, string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (stateLock)
        {
            if (IsRunning)
            {
                return;
            }

            Port = port;
            Path = NormalisePath(path);
            streamId = Path.TrimStart('/');

            try
            {
                server = new RTSPServer(port, UserName, Password, loggerFactory)
                {
                    SessionName = "Screen2VMS",
                };

                // Parameter sets are filled in by the first PushFrame, or
                // earlier if the encoder already knows them.
                track = new H264Track(
                    profileIdc: 0x4D,
                    profileIop: 0x40,
                    level: 0x1F,
                    clock: VideoClockHz);

                streamSource = new RTSPStreamSource(streamId, track, rtspAudioTrack: null);
                server.AddStreamSource(streamSource);
                server.ReceivedRtspMessage += OnRtspMessage;
                server.StartListen();

                IsRunning = true;
                logger.LogInformation("RtspServerStarted: listening on port {Port}, path {Path}", port, Path);
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new RtspServerException(
                    $"Could not start the RTSP server on port {port}. " +
                    "The port may already be in use or blocked by the firewall.",
                    ex);
            }
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            if (!IsRunning)
            {
                return;
            }

            Cleanup();
            IsRunning = false;
            Volatile.Write(ref clientCount, 0);
            logger.LogInformation("RtspServerStopped");
        }
    }

    /// <summary>
    /// Publishes the parameter sets so the SDP is complete before the first
    /// client asks for it.
    /// </summary>
    /// <remarks>
    /// A DESCRIBE that arrives before any frame has been encoded would
    /// otherwise return an SDP with no <c>sprop-parameter-sets</c>, which some
    /// VMS decoders reject outright rather than waiting for in-band sets.
    /// </remarks>
    public void SetParameterSets(byte[] sps, byte[] pps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);

        var current = track;
        if (current is null)
        {
            return;
        }

        // Published again on every key frame, so only act when they actually
        // change - otherwise this rewrites the track and logs once a second.
        if (current.SPS is not null
            && current.PPS is not null
            && current.SPS.AsSpan().SequenceEqual(sps)
            && current.PPS.AsSpan().SequenceEqual(pps))
        {
            return;
        }

        var (profileIdc, profileIop, levelIdc) = H264Bitstream.ParseProfileLevel(sps);

        current.ProfileIdc = profileIdc;
        current.ProfileIop = profileIop;
        current.Level = levelIdc;
        current.SetParameterSets(sps, pps);

        logger.LogInformation(
            "RTSP track ready: profile-level-id {ProfileLevelId}",
            H264Bitstream.FormatProfileLevelId(profileIdc, profileIop, levelIdc));
    }

    public void PushFrame(EncodedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var current = track;
        if (!IsRunning || current is null || !current.IsReady)
        {
            return;
        }

        if (ClientCount == 0)
        {
            // Nothing is watching. Packetising anyway would burn CPU for no one.
            return;
        }

        var units = H264Bitstream.SplitAnnexBToMemory(frame.Data, frame.Length);
        if (units.Count == 0)
        {
            return;
        }

        // RTP timestamps run on a 90 kHz clock derived from the capture
        // timestamp, so playback timing survives a variable frame rate.
        var rtpTimestamp = (uint)(frame.Timestamp.TotalSeconds * VideoClockHz);

        try
        {
            current.FeedInRawSamples(rtpTimestamp, units);
        }
        catch (Exception ex)
        {
            // A client vanishing mid-write must not take the pipeline down.
            logger.LogDebug(ex, "Dropping a frame that could not be sent to RTSP clients.");
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

    private void Cleanup()
    {
        if (server is not null)
        {
            server.ReceivedRtspMessage -= OnRtspMessage;

            try
            {
                server.StopListen();
                server.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ignoring an error while shutting the RTSP server down.");
            }
        }

        streamSource?.Dispose();
        server = null;
        streamSource = null;
        track = null;
    }

    /// <summary>
    /// Tracks sessions so a joining client can be given a key frame at once.
    /// </summary>
    /// <remarks>
    /// Without this a new client sees nothing until the next GOP boundary, up
    /// to a second later, which reads as a broken camera in a VMS live view.
    /// </remarks>
    private void OnRtspMessage(object? sender, RtspMessageEventArgs e)
    {
        switch (e.Message)
        {
            case RtspRequestPlay:
                Interlocked.Increment(ref clientCount);
                logger.LogInformation(
                    "RtspClientConnected: {Client}, {Count} client(s) now streaming",
                    Describe(e),
                    ClientCount);

                ClientConnected?.Invoke(this, EventArgs.Empty);
                break;

            case RtspRequestTeardown:
                if (Interlocked.Decrement(ref clientCount) < 0)
                {
                    Volatile.Write(ref clientCount, 0);
                }

                logger.LogInformation(
                    "RtspClientDisconnected: {Client}, {Count} client(s) remaining",
                    Describe(e),
                    ClientCount);

                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static string Describe(RtspMessageEventArgs e) =>
        e.Connection?.SessionId ?? "unknown session";

    private static string NormalisePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/live";
        }

        return path.StartsWith('/') ? path : "/" + path;
    }
}

/// <summary>The RTSP server could not be started or run.</summary>
public sealed class RtspServerException : Exception
{
    public RtspServerException(string message)
        : base(message)
    {
    }

    public RtspServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

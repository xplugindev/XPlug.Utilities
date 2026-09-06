using Screen2VMS.Core.Encoding;

namespace Screen2VMS.Core.Streaming;

/// <summary>
/// Serves the encoded stream over RTSP (spec 43). Implemented in Phase 3.
/// </summary>
/// <remarks>
/// The server consumes encoded frames and never touches the camera. One encode
/// feeds every client (spec 33).
/// </remarks>
public interface IRtspServer : IDisposable
{
    bool IsRunning { get; }

    int Port { get; }

    string Path { get; }

    int ClientCount { get; }

    /// <summary>Raised when a client connects, so the encoder can be asked for an IDR.</summary>
    event EventHandler? ClientConnected;

    event EventHandler? ClientDisconnected;

    void Start(int port, string path);

    void Stop();

    /// <summary>Publishes one encoded frame to every connected client.</summary>
    void PushFrame(EncodedFrame frame);
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Engine;

/// <summary>
/// Owns one <see cref="Screen2VmsRuntime"/> per configured camera profile, so
/// several cameras can each be published as an independent ONVIF device and
/// RTSP stream from the same process (spec override, see CLAUDE.md
/// "multi-camera").
/// </summary>
/// <remarks>
/// <see cref="Screen2VmsRuntime"/> already has no shared or static state - it
/// takes its ports and identity from the <c>AppConfiguration</c> passed to
/// <see cref="Screen2VmsRuntime.Start"/> - so this class only needs to keep
/// one instance per profile id and forward calls to the right one.
/// </remarks>
public sealed class CameraRuntimeManager : IDisposable
{
    private readonly ICameraSourceService cameraService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ConcurrentDictionary<string, Screen2VmsRuntime> runtimes = new();
    private bool disposed;

    public CameraRuntimeManager(ICameraSourceService cameraService, ILoggerFactory? loggerFactory = null)
    {
        this.cameraService = cameraService;
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>The runtime for a profile, if one has been created.</summary>
    public Screen2VmsRuntime? RuntimeFor(string profileId) =>
        runtimes.TryGetValue(profileId, out var runtime) ? runtime : null;

    /// <summary>The runtime for a profile, creating it on first use.</summary>
    public Screen2VmsRuntime GetOrCreate(string profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        ObjectDisposedException.ThrowIf(disposed, this);

        return runtimes.GetOrAdd(profileId, _ => new Screen2VmsRuntime(cameraService, loggerFactory));
    }

    /// <summary>Stops and disposes a profile's runtime and forgets it.</summary>
    public void Remove(string profileId)
    {
        if (runtimes.TryRemove(profileId, out var runtime))
        {
            runtime.Dispose();
        }
    }

    /// <summary>Stops every running camera.</summary>
    public void StopAll()
    {
        foreach (var runtime in runtimes.Values)
        {
            runtime.Stop();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var runtime in runtimes.Values)
        {
            runtime.Dispose();
        }

        runtimes.Clear();
    }
}

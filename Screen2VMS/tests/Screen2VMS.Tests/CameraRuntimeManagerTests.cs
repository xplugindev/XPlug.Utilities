using Screen2VMS.Core.Cameras;
using Screen2VMS.Engine;

namespace Screen2VMS.Tests;

/// <summary>
/// Covers the bookkeeping <see cref="CameraRuntimeManager"/> does around
/// several <see cref="Screen2VmsRuntime"/> instances. None of these tests
/// call <c>Start</c> on a runtime, so the stub camera service is never
/// actually invoked - only construction, lookup and disposal are exercised.
/// </summary>
public class CameraRuntimeManagerTests
{
    [Fact]
    public void GetOrCreate_ReturnsTheSameRuntimeForTheSameProfileId()
    {
        using var manager = new CameraRuntimeManager(new StubCameraSourceService());

        var first = manager.GetOrCreate("camera-1");
        var second = manager.GetOrCreate("camera-1");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_ReturnsDifferentRuntimesForDifferentProfileIds()
    {
        using var manager = new CameraRuntimeManager(new StubCameraSourceService());

        var first = manager.GetOrCreate("camera-1");
        var second = manager.GetOrCreate("camera-2");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void RuntimeFor_ReturnsNull_WhenNoRuntimeHasBeenCreatedForThatId()
    {
        using var manager = new CameraRuntimeManager(new StubCameraSourceService());

        Assert.Null(manager.RuntimeFor("never-added"));

        var created = manager.GetOrCreate("camera-1");
        Assert.Same(created, manager.RuntimeFor("camera-1"));
    }

    [Fact]
    public void Remove_ForgetsTheRuntime_SoANewOneIsCreatedNextTime()
    {
        using var manager = new CameraRuntimeManager(new StubCameraSourceService());

        var first = manager.GetOrCreate("camera-1");
        manager.Remove("camera-1");

        Assert.Null(manager.RuntimeFor("camera-1"));

        var second = manager.GetOrCreate("camera-1");
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Remove_OfAnUnknownProfileId_IsANoOp()
    {
        using var manager = new CameraRuntimeManager(new StubCameraSourceService());

        manager.Remove("never-added");
    }

    [Fact]
    public void Dispose_MakesFurtherGetOrCreateCallsThrow()
    {
        var manager = new CameraRuntimeManager(new StubCameraSourceService());
        manager.GetOrCreate("camera-1");

        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.GetOrCreate("camera-2"));
    }

    [Fact]
    public void Dispose_IsSafeToCallTwice()
    {
        var manager = new CameraRuntimeManager(new StubCameraSourceService());
        manager.GetOrCreate("camera-1");

        manager.Dispose();
        manager.Dispose();
    }

    /// <summary>Never actually called by these tests - construction never touches the camera service.</summary>
    private sealed class StubCameraSourceService : ICameraSourceService
    {
        public IReadOnlyList<CameraDevice> EnumerateDevices() => throw new NotSupportedException();

        public IReadOnlyList<CameraMode>? GetCapabilities(string deviceId) => throw new NotSupportedException();

        public ICameraSource CreateSource(string deviceId) => throw new NotSupportedException();
    }
}

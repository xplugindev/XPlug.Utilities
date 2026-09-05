using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Tests;

public class CameraModeSelectorTests
{
    private static CameraMode Mode(int width, int height, double fps, VideoPixelFormat format = VideoPixelFormat.Nv12, int index = 0) =>
        new(width, height, fps, fps, format, index);

    [Fact]
    public void Select_ReturnsNull_WhenNoModesAreAvailable()
    {
        Assert.Null(CameraModeSelector.Select(Array.Empty<CameraMode>(), CameraSettings.Default));
    }

    [Fact]
    public void Select_PrefersAnExactMatch()
    {
        var modes = new[]
        {
            Mode(1280, 720, 30),
            Mode(1920, 1080, 30),
            Mode(640, 480, 30),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(1920, selected!.Width);
        Assert.Equal(1080, selected.Height);
    }

    [Fact]
    public void Select_FallsBackToTheClosestResolution_WhenTheRequestIsUnavailable()
    {
        // Spec 9: never fail just because 1920x1080 is missing.
        var modes = new[]
        {
            Mode(640, 480, 30),
            Mode(1280, 720, 30),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(1280, selected!.Width);
        Assert.Equal(720, selected.Height);
    }

    [Fact]
    public void Select_KeepsTheRequestedResolution_EvenWhenAnotherModeMatchesTheFrameRate()
    {
        // A user who asked for 1080p would rather have it at 15 fps than drop
        // to 720p to get 30.
        var modes = new[]
        {
            Mode(1280, 720, 30),
            Mode(1920, 1080, 15),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(1920, selected!.Width);
        Assert.Equal(15, selected.MaxFrameRate);
    }

    [Fact]
    public void Select_PicksTheNearestFrameRate_AmongModesOfTheSameSize()
    {
        var modes = new[]
        {
            Mode(1920, 1080, 5, index: 0),
            Mode(1920, 1080, 30, index: 1),
            Mode(1920, 1080, 60, index: 2),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(1, selected!.NativeTypeIndex);
    }

    [Fact]
    public void Select_UsesFormatOnlyAsATieBreak()
    {
        // Same size, same rate: NV12 wins over MJPEG because it needs no decode.
        var modes = new[]
        {
            Mode(1920, 1080, 30, VideoPixelFormat.Mjpg, index: 0),
            Mode(1920, 1080, 30, VideoPixelFormat.Nv12, index: 1),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(1, selected!.NativeTypeIndex);
    }

    [Fact]
    public void Select_TakesAnMjpegModeOverAWorseResolution()
    {
        // Cheap webcams often only reach 1080p30 in MJPEG, with YUY2 capped at
        // 5 fps. Penalising MJPEG must not cost the resolution.
        var modes = new[]
        {
            Mode(1920, 1080, 30, VideoPixelFormat.Mjpg, index: 0),
            Mode(640, 480, 30, VideoPixelFormat.Nv12, index: 1),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(0, selected!.NativeTypeIndex);
    }

    [Fact]
    public void Select_HonoursAModeThatAdvertisesAFrameRateRange()
    {
        var modes = new[]
        {
            new CameraMode(1920, 1080, 5, 30, VideoPixelFormat.Nv12, 0),
        };

        var selected = CameraModeSelector.Select(modes, CameraSettings.Default);

        Assert.Equal(30, CameraModeSelector.ResolveFrameRate(selected!, CameraSettings.Default));
    }

    [Fact]
    public void ResolveFrameRate_ClampsIntoTheModesRange()
    {
        var mode = new CameraMode(1920, 1080, 5, 15, VideoPixelFormat.Nv12, 0);

        Assert.Equal(15, CameraModeSelector.ResolveFrameRate(mode, CameraSettings.Default));
    }

    [Fact]
    public void SupportsFrameRate_ToleratesRatioRounding()
    {
        // 30000/1001 is 29.97, which must still count as supported.
        var mode = new CameraMode(1920, 1080, 29.97, 29.97, VideoPixelFormat.Nv12, 0);

        Assert.True(mode.SupportsFrameRate(29.97));
        Assert.False(mode.SupportsFrameRate(30));
    }
}

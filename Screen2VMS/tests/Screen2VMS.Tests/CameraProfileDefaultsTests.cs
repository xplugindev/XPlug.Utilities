using Screen2VMS.Core.Configuration;

namespace Screen2VMS.Tests;

public class CameraProfileDefaultsTests
{
    [Fact]
    public void NextPorts_WithNoExistingProfiles_ReturnsTheBasePorts()
    {
        var (rtspPort, onvifPort) = CameraProfileDefaults.NextPorts([]);

        Assert.Equal(CameraProfileDefaults.BaseRtspPort, rtspPort);
        Assert.Equal(CameraProfileDefaults.BaseOnvifPort, onvifPort);
    }

    [Fact]
    public void NextPorts_SkipsPortsAlreadyUsedByAnotherProfile()
    {
        var existing = new[]
        {
            new CameraProfile
            {
                Rtsp = new RtspConfiguration { Port = CameraProfileDefaults.BaseRtspPort },
                Onvif = new OnvifConfiguration { Port = CameraProfileDefaults.BaseOnvifPort },
            },
        };

        var (rtspPort, onvifPort) = CameraProfileDefaults.NextPorts(existing);

        Assert.Equal(CameraProfileDefaults.BaseRtspPort + 1, rtspPort);
        Assert.Equal(CameraProfileDefaults.BaseOnvifPort + 1, onvifPort);
    }

    [Fact]
    public void NextPorts_KeepsRtspAndOnvifOffsetsPaired_EvenIfOnlyOnePortWasCustomized()
    {
        // A profile edited to move only its ONVIF port off the default must
        // still push the *next* profile's offset past both, so two profiles
        // never collide on either port.
        var existing = new[]
        {
            new CameraProfile
            {
                Rtsp = new RtspConfiguration { Port = CameraProfileDefaults.BaseRtspPort },
                Onvif = new OnvifConfiguration { Port = CameraProfileDefaults.BaseOnvifPort + 1 },
            },
        };

        var (rtspPort, onvifPort) = CameraProfileDefaults.NextPorts(existing);

        // Offset 1 is skipped even though its RTSP port (8555) is free,
        // because its ONVIF port (8001) collides with the customized profile.
        Assert.Equal(CameraProfileDefaults.BaseRtspPort + 2, rtspPort);
        Assert.Equal(CameraProfileDefaults.BaseOnvifPort + 2, onvifPort);
    }
}

namespace Screen2VMS.Core.Configuration;

/// <summary>Default port allocation for a newly-added camera profile.</summary>
public static class CameraProfileDefaults
{
    public const int BaseRtspPort = 8554;

    public const int BaseOnvifPort = 8000;

    /// <summary>
    /// The next free RTSP/ONVIF port pair, starting from the base ports and
    /// skipping anything already used by an existing profile.
    /// </summary>
    /// <remarks>
    /// Each candidate offset is checked against both port sets together, so
    /// the RTSP and ONVIF ports of a single profile always share the same
    /// offset (8554/8000, 8555/8001, ...) even if a profile was edited to use
    /// a non-default port for one of the two.
    /// </remarks>
    public static (int RtspPort, int OnvifPort) NextPorts(IEnumerable<CameraProfile> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var usedRtspPorts = new HashSet<int>();
        var usedOnvifPorts = new HashSet<int>();

        foreach (var profile in existing)
        {
            usedRtspPorts.Add(profile.Rtsp.Port);
            usedOnvifPorts.Add(profile.Onvif.Port);
        }

        for (var offset = 0; offset < int.MaxValue; offset++)
        {
            var rtspPort = BaseRtspPort + offset;
            var onvifPort = BaseOnvifPort + offset;

            if (!usedRtspPorts.Contains(rtspPort) && !usedOnvifPorts.Contains(onvifPort))
            {
                return (rtspPort, onvifPort);
            }
        }

        throw new InvalidOperationException("No free port pair was found.");
    }
}

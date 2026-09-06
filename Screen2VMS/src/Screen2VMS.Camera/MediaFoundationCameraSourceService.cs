using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.MediaFoundation;
using Screen2VMS.Core.Cameras;

namespace Screen2VMS.Camera;

/// <summary>
/// Enumerates Windows video capture devices through Media Foundation.
/// </summary>
public sealed class MediaFoundationCameraSourceService : ICameraSourceService
{
    private readonly ILogger<MediaFoundationCameraSourceService> logger;

    public MediaFoundationCameraSourceService(ILogger<MediaFoundationCameraSourceService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MediaFoundationCameraSourceService>.Instance;
    }

    public IReadOnlyList<CameraDevice> EnumerateDevices()
    {
        var devices = MtaRunner.Run(EnumerateCore);

        logger.LogInformation(
            "CameraEnumerated: found {DeviceCount} capture device(s): {DeviceNames}",
            devices.Count,
            string.Join(", ", devices.Select(d => d.Name)));

        return devices;
    }

    public IReadOnlyList<CameraMode>? GetCapabilities(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        return EnumerateDevices()
            .FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            ?.Modes;
    }

    public ICameraSource CreateSource(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        var device = EnumerateDevices()
            .FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new CameraException($"No capture device matches id '{deviceId}'.");

        return new MediaFoundationCameraSource(device, logger);
    }

    private List<CameraDevice> EnumerateCore()
    {
        var devices = new List<CameraDevice>();

        MfNative.ThrowIfFailed(MfNative.MFCreateAttributes(out var attributes, 1), "MFCreateAttributes");

        try
        {
            var sourceTypeKey = MfConstants.DevSourceAttributeSourceType;
            var vidcap = MfConstants.DevSourceAttributeSourceTypeVidCap;
            MfNative.ThrowIfFailed(attributes.SetGUID(ref sourceTypeKey, ref vidcap), "SetGUID(SourceType)");

            MfNative.ThrowIfFailed(
                MfNative.MFEnumDeviceSources(attributes, out var activateArray, out var count),
                "MFEnumDeviceSources");

            if (activateArray == IntPtr.Zero)
            {
                return devices;
            }

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var slot = Marshal.ReadIntPtr(activateArray, i * IntPtr.Size);
                    if (slot == IntPtr.Zero)
                    {
                        continue;
                    }

                    var activate = (IMFActivate)Marshal.GetObjectForIUnknown(slot);

                    // MFEnumDeviceSources handed us ownership of each element;
                    // the runtime callable wrapper holds its own reference now.
                    Marshal.Release(slot);

                    try
                    {
                        var device = DescribeDevice(activate);
                        if (device is not null)
                        {
                            devices.Add(device);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(activate);
                    }
                }
            }
            finally
            {
                MfNative.CoTaskMemFree(activateArray);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }

        return devices;
    }

    private CameraDevice? DescribeDevice(IMFActivate activate)
    {
        var symbolicLink = MfNative.GetStringAttribute(activate, MfConstants.DevSourceAttributeVidCapSymbolicLink);
        if (string.IsNullOrEmpty(symbolicLink))
        {
            // Without a symbolic link there is no stable way to reopen the
            // device later, so it is no use to us.
            return null;
        }

        var name = MfNative.GetStringAttribute(activate, MfConstants.DevSourceAttributeFriendlyName)
            ?? "Unknown Camera";

        var modes = ReadModes(activate, name);

        return new CameraDevice(symbolicLink, name, Manufacturer: null, modes);
    }

    /// <summary>
    /// Reads a device's capture modes, which means briefly opening it.
    /// </summary>
    /// <remarks>
    /// A camera held by Teams or Zoom cannot be opened, so it is listed with no
    /// modes rather than omitted - the user still needs to see it in the list,
    /// and <see cref="GetCapabilities"/> will pick the modes up once it frees.
    /// </remarks>
    private IReadOnlyList<CameraMode> ReadModes(IMFActivate activate, string deviceName)
    {
        var riid = typeof(IMFMediaSource).GUID;
        var hr = activate.ActivateObject(ref riid, out var sourcePtr);
        if (MfNative.Failed(hr) || sourcePtr == IntPtr.Zero)
        {
            logger.LogWarning(
                "Could not open '{Camera}' to read its capture modes (HRESULT 0x{HResult:X8}). " +
                "It is probably in use by another application.",
                deviceName,
                hr);

            return Array.Empty<CameraMode>();
        }

        var source = (IMFMediaSource)Marshal.GetObjectForIUnknown(sourcePtr);
        Marshal.Release(sourcePtr);

        try
        {
            hr = MfNative.MFCreateSourceReaderFromMediaSource(source, attributes: null, out var reader);
            if (MfNative.Failed(hr))
            {
                logger.LogWarning(
                    "Could not create a source reader for '{Camera}' (HRESULT 0x{HResult:X8}).",
                    deviceName,
                    hr);

                return Array.Empty<CameraMode>();
            }

            try
            {
                return MfModeReader.ReadNativeModes(reader);
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        finally
        {
            source.Shutdown();
            Marshal.ReleaseComObject(source);
        }
    }
}

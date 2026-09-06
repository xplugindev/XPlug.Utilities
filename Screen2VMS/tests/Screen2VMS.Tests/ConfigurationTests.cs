using Screen2VMS.Configuration;
using Screen2VMS.Core.Configuration;

namespace Screen2VMS.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly string directory;
    private readonly string configFile;

    public ConfigurationTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "Screen2VMS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        configFile = Path.Combine(directory, "config.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_WritesDefaults_WhenNoFileExists()
    {
        var service = new JsonConfigurationService(configFile);

        var configuration = service.Load();

        Assert.True(File.Exists(configFile));
        Assert.Equal(8554, configuration.Rtsp.Port);
        Assert.Equal(8000, configuration.Onvif.Port);
        Assert.Equal(3702, configuration.Discovery.Port);
        Assert.Equal("/live", configuration.Rtsp.Path);
    }

    [Fact]
    public void Load_GeneratesDeviceIdentityOnce_AndKeepsItAcrossRestarts()
    {
        // Spec 19: a serial or MAC that moves makes the VMS treat Screen2VMS as
        // a different camera, forcing the unit to be added again.
        var first = new JsonConfigurationService(configFile).Load();

        Assert.False(string.IsNullOrWhiteSpace(first.Device.SerialNumber));
        Assert.False(string.IsNullOrWhiteSpace(first.Device.MacAddress));

        var second = new JsonConfigurationService(configFile).Load();

        Assert.Equal(first.Device.SerialNumber, second.Device.SerialNumber);
        Assert.Equal(first.Device.MacAddress, second.Device.MacAddress);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverySection()
    {
        var service = new JsonConfigurationService(configFile);
        var loaded = service.Load();

        service.Save(loaded with
        {
            Camera = loaded.Camera with { DeviceId = @"\\?\usb#vid_046d", Name = "Test Cam", Width = 1280, Height = 720, Fps = 15 },
            Encoder = loaded.Encoder with { BitrateKbps = 6000, Gop = 15 },
            Rtsp = loaded.Rtsp with { Port = 9554, Path = "/main" },
            Onvif = loaded.Onvif with { Port = 8081, Username = "operator" },
            Logging = loaded.Logging with { Level = "Trace" },
        });

        var reloaded = new JsonConfigurationService(configFile).Load();

        Assert.Equal("Test Cam", reloaded.Camera.Name);
        Assert.Equal(1280, reloaded.Camera.Width);
        Assert.Equal(15, reloaded.Camera.Fps);
        Assert.Equal(6000, reloaded.Encoder.BitrateKbps);
        Assert.Equal(9554, reloaded.Rtsp.Port);
        Assert.Equal("/main", reloaded.Rtsp.Path);
        Assert.Equal("operator", reloaded.Onvif.Username);
        Assert.Equal("Trace", reloaded.Logging.Level);
    }

    [Fact]
    public void Load_FallsBackToDefaults_WhenTheFileIsCorrupt()
    {
        // A bad config must not stop the application starting, or the user has
        // no way to fix it from the UI.
        File.WriteAllText(configFile, "{ this is not json");

        var configuration = new JsonConfigurationService(configFile).Load();

        Assert.Equal(8554, configuration.Rtsp.Port);
        Assert.False(string.IsNullOrWhiteSpace(configuration.Device.SerialNumber));
    }

    [Fact]
    public void Update_AppliesAndPersists()
    {
        var service = new JsonConfigurationService(configFile);
        service.Load();

        service.Update(config => config with { Camera = config.Camera with { Name = "Updated" } });

        Assert.Equal("Updated", service.Current.Camera.Name);
        Assert.Equal("Updated", new JsonConfigurationService(configFile).Load().Camera.Name);
    }

    [Fact]
    public void NewMacAddress_IsLocallyAdministeredAndUnicast()
    {
        // Bit 1 set marks the address as locally assigned; bit 0 clear keeps it
        // unicast. Without both, a generated MAC could clash with a real vendor.
        for (var i = 0; i < 50; i++)
        {
            var mac = DeviceIdentity.NewMacAddress();

            Assert.Equal(12, mac.Length);

            var firstOctet = Convert.ToByte(mac[..2], 16);
            Assert.Equal(0x02, firstOctet & 0x02);
            Assert.Equal(0x00, firstOctet & 0x01);
        }
    }

    [Fact]
    public void FormatMacAddress_UsesColonSeparatedOctets()
    {
        Assert.Equal("02:1A:2B:3C:4D:5E", DeviceIdentity.FormatMacAddress("021A2B3C4D5E"));
    }

    [Fact]
    public void NewSerialNumber_IsPrefixedAndUnique()
    {
        var serials = Enumerable.Range(0, 100).Select(_ => DeviceIdentity.NewSerialNumber()).ToList();

        Assert.All(serials, s => Assert.StartsWith("S2V-", s, StringComparison.Ordinal));
        Assert.Equal(serials.Count, serials.Distinct().Count());
    }
}

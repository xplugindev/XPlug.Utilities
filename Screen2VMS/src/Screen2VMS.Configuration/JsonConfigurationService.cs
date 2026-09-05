using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screen2VMS.Core.Configuration;

namespace Screen2VMS.Configuration;

/// <summary>
/// Stores configuration as JSON under ProgramData (spec 56). No database.
/// </summary>
public sealed class JsonConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string filePath;
    private readonly ILogger<JsonConfigurationService> logger;
    private readonly object saveLock = new();

    private AppConfiguration current = new();

    public JsonConfigurationService(
        string? filePath = null,
        ILogger<JsonConfigurationService>? logger = null)
    {
        this.filePath = filePath ?? AppPaths.ConfigurationFile;
        this.logger = logger ?? NullLogger<JsonConfigurationService>.Instance;
    }

    public AppConfiguration Current => current;

    public AppConfiguration Load()
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<AppConfiguration>(json, SerializerOptions);
                if (loaded is not null)
                {
                    current = EnsureIdentity(loaded);

                    // A first run, or an upgrade that added identity fields,
                    // writes the completed file straight back.
                    if (!ReferenceEquals(current, loaded))
                    {
                        Save(current);
                    }

                    return current;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable config must never stop the application
            // starting - it would leave the user with no way to fix it.
            logger.LogWarning(ex, "Could not read {ConfigFile}; falling back to defaults.", filePath);
        }

        current = EnsureIdentity(new AppConfiguration());
        Save(current);
        return current;
    }

    public void Save(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (saveLock)
        {
            current = configuration;

            try
            {
                AppPaths.EnsureCreated();

                // Write beside the target and swap, so an interrupted save
                // cannot leave a half-written config behind.
                var temporaryPath = filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, SerializerOptions));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not write {ConfigFile}. Settings will not persist.", filePath);
            }
        }
    }

    public void Update(Func<AppConfiguration, AppConfiguration> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (saveLock)
        {
            Save(update(current));
        }
    }

    /// <summary>
    /// Fills in the identity values a VMS keys on, generating them once.
    /// </summary>
    /// <remarks>
    /// Returns the original instance untouched when nothing was missing, which
    /// is how <see cref="Load"/> knows whether it needs to write back.
    /// </remarks>
    private static AppConfiguration EnsureIdentity(AppConfiguration configuration)
    {
        var serialNumber = configuration.Device.SerialNumber;
        var macAddress = configuration.Device.MacAddress;

        if (!string.IsNullOrWhiteSpace(serialNumber) && !string.IsNullOrWhiteSpace(macAddress))
        {
            return configuration;
        }

        return configuration with
        {
            Device = configuration.Device with
            {
                SerialNumber = string.IsNullOrWhiteSpace(serialNumber)
                    ? DeviceIdentity.NewSerialNumber()
                    : serialNumber,
                MacAddress = string.IsNullOrWhiteSpace(macAddress)
                    ? DeviceIdentity.NewMacAddress()
                    : macAddress,
            },
        };
    }
}

namespace Screen2VMS.Core.Configuration;

/// <summary>Loads and saves <see cref="AppConfiguration"/> (spec 39).</summary>
public interface IConfigurationService
{
    /// <summary>The configuration currently in effect. Never null.</summary>
    AppConfiguration Current { get; }

    /// <summary>
    /// Reads configuration from disk, falling back to defaults when the file is
    /// missing or unreadable. Never throws on a corrupt file - a bad config must
    /// not stop the application starting.
    /// </summary>
    AppConfiguration Load();

    /// <summary>Writes configuration to disk atomically and updates <see cref="Current"/>.</summary>
    void Save(AppConfiguration configuration);

    /// <summary>Applies <paramref name="update"/> to the current configuration and saves the result.</summary>
    void Update(Func<AppConfiguration, AppConfiguration> update);
}

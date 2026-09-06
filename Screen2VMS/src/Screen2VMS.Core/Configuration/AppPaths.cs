namespace Screen2VMS.Core.Configuration;

/// <summary>
/// Where Screen2VMS keeps state on disk (spec 37, 56).
/// </summary>
/// <remarks>
/// ProgramData rather than AppData because the streaming engine is destined to
/// run as a Windows service later (spec 58) and must not depend on a logged-in
/// user's profile. Screen2VMS runs unelevated, so the first launch creates
/// these directories and the creating account owns them.
/// </remarks>
public static class AppPaths
{
    public const string ProductFolderName = "Screen2VMS";

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProductFolderName);

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "Logs");

    public static string ConfigurationFile { get; } = Path.Combine(RootDirectory, "config.json");

    /// <summary>Creates the directories if they are missing. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

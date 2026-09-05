using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Screen2VMS.Core.Configuration;

namespace Screen2VMS.Logging;

/// <summary>
/// Configures file logging under ProgramData with daily rolling files (spec 37).
/// </summary>
public static class LogSetup
{
    /// <summary>How long rolled log files are kept before Serilog deletes them.</summary>
    private const int RetainedFileCount = 14;

    /// <summary>
    /// Builds a logger factory writing to the log directory.
    /// </summary>
    /// <param name="level">Normal, Debug or Trace, as stored in configuration (spec 49).</param>
    public static ILoggerFactory Create(string level = "Normal")
    {
        AppPaths.EnsureCreated();

        var minimumLevel = ParseLevel(level);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "screen2vms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        if (minimumLevel <= LogEventLevel.Debug)
        {
            // Protocol-level tracing goes to its own file so the main log stays
            // readable when a VMS integration is being debugged (spec 49).
            configuration = configuration.WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "trace-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                shared: true,
                restrictedToMinimumLevel: LogEventLevel.Verbose);
        }

        Log.Logger = configuration.CreateLogger();

        return LoggerFactory.Create(builder => builder
            .AddSerilog(Log.Logger, dispose: true)
            .SetMinimumLevel(ToMicrosoftLevel(minimumLevel)));
    }

    /// <summary>Flushes buffered log events. Call before the process exits.</summary>
    public static void Shutdown() => Log.CloseAndFlush();

    private static LogEventLevel ParseLevel(string level) => level?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        _ => LogEventLevel.Information,
    };

    private static LogLevel ToMicrosoftLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        _ => LogLevel.Information,
    };
}

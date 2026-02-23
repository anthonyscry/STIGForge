using Serilog.Core;
using Serilog.Events;

namespace STIGForge.Infrastructure.Logging;

/// <summary>
/// Centralized logging configuration for STIGForge.
/// Provides a shared LoggingLevelSwitch for runtime log level control
/// and environment-based configuration.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Shared logging level switch that can be modified at runtime.
    /// Initialized to Information level by default.
    /// </summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Information);

    /// <summary>
    /// Configures the logging level switch from the STIGFORGE_LOG_LEVEL environment variable.
    /// Valid values: Debug, Verbose, Warning, Error, Information (default).
    /// </summary>
    public static void ConfigureFromEnvironment()
    {
        var levelValue = Environment.GetEnvironmentVariable("STIGFORGE_LOG_LEVEL");

        var level = levelValue?.ToLowerInvariant() switch
        {
            "debug" => LogEventLevel.Debug,
            "verbose" => LogEventLevel.Verbose,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "information" => LogEventLevel.Information,
            null or "" => LogEventLevel.Information,
            _ => LogEventLevel.Information // Unknown values default to Information
        };

        LevelSwitch.MinimumLevel = level;
    }

    /// <summary>
    /// Gets the current log level name for diagnostic output.
    /// </summary>
    public static string CurrentLevelName => LevelSwitch.MinimumLevel.ToString();
}

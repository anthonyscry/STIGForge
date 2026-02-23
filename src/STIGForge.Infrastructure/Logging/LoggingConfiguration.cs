using Serilog.Core;
using Serilog.Events;
using STIGForge.Infrastructure.Telemetry;

namespace STIGForge.Infrastructure.Logging;

/// <summary>
/// Centralized logging configuration for STIGForge.
/// Provides a shared LoggingLevelSwitch for runtime log level control
/// and environment-based configuration. Also manages the TraceFileListener
/// for distributed tracing output.
/// </summary>
public static class LoggingConfiguration
{
    private static TraceFileListener? _traceFileListener;

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
    /// Initializes the TraceFileListener for distributed tracing output.
    /// Should be called once at application startup.
    /// </summary>
    /// <param name="logsRoot">The root directory where traces.json will be created.</param>
    public static void InitializeTraceListener(string logsRoot)
    {
        _traceFileListener ??= new TraceFileListener(logsRoot);
    }

    /// <summary>
    /// Disposes the TraceFileListener if it was initialized.
    /// Should be called at application shutdown.
    /// </summary>
    public static void Shutdown()
    {
        _traceFileListener?.Dispose();
        _traceFileListener = null;
    }

    /// <summary>
    /// Gets the current log level name for diagnostic output.
    /// </summary>
    public static string CurrentLevelName => LevelSwitch.MinimumLevel.ToString();
}

using System.Diagnostics;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Serializable trace context for propagating W3C trace information across process boundaries.
/// Used to pass trace context to PowerShell scripts and external processes.
/// </summary>
public sealed class TraceContext
{
    /// <summary>
    /// Gets the W3C trace ID as a 32-character hex string.
    /// </summary>
    public string TraceId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the W3C span ID as a 16-character hex string.
    /// </summary>
    public string SpanId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the W3C trace flags (e.g., "01" for sampled).
    /// </summary>
    public string TraceFlags { get; init; } = "00";

    /// <summary>
    /// Gets the current trace context from Activity.Current if available.
    /// </summary>
    /// <returns>A TraceContext populated with current Activity data, or null if no Activity is active.</returns>
    public static TraceContext? GetCurrentContext()
    {
        var activity = Activity.Current;

        if (activity == null)
        {
            return null;
        }

        return new TraceContext
        {
            TraceId = activity.TraceId.ToString(),
            SpanId = activity.SpanId.ToString(),
            TraceFlags = activity.ActivityTraceFlags.ToString("x2")
        };
    }
}

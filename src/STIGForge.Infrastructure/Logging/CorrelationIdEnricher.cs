using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace STIGForge.Infrastructure.Logging;

/// <summary>
/// Serilog enricher that adds correlation IDs to log events.
/// Uses Activity.Current for W3C trace context when available,
/// falling back to a generated CorrelationId otherwise.
/// </summary>
public sealed class CorrelationIdEnricher : ILogEventEnricher
{
    private static readonly AsyncLocal<string?> ScopeCorrelationId = new();

    /// <summary>
    /// Set a correlation ID for the current async scope.
    /// If not set, a stable per-scope ID is generated on first use.
    /// </summary>
    public static void SetCorrelationId(string correlationId) => ScopeCorrelationId.Value = correlationId;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;

        if (activity != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "TraceId", activity.TraceId.ToString()));

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "SpanId", activity.SpanId.ToString()));
        }
        else
        {
            var correlationId = ScopeCorrelationId.Value;
            if (correlationId == null)
            {
                correlationId = Guid.NewGuid().ToString("N");
                ScopeCorrelationId.Value = correlationId;
            }
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "TraceId", correlationId));
        }
    }
}

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
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;

        if (activity != null)
        {
            // Add TraceId for distributed correlation (W3C standard)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "TraceId", activity.TraceId.ToString()));

            // Add SpanId for hierarchical correlation
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "SpanId", activity.SpanId.ToString()));
        }
        else
        {
            // Generate correlation ID if no Activity context exists
            // This ensures every log has some form of correlation identifier
            var correlationId = Guid.NewGuid().ToString("N");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "CorrelationId", correlationId));
        }
    }
}

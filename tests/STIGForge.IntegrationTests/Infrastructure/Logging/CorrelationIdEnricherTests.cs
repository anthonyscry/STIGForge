using System.Diagnostics;
using FluentAssertions;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using STIGForge.Infrastructure.Logging;
using Xunit;

namespace STIGForge.IntegrationTests.Infrastructure.Logging;

public class CorrelationIdEnricherTests
{
    [Fact]
    public void Enrich_WhenActivityExists_AddsTraceIdAndSpanId()
    {
        // Arrange
        using var activity = new Activity("TestActivity");
        activity.Start();

        var enricher = new CorrelationIdEnricher();
        var logEvent = CreateLogEvent();
        var propertyFactory = new SimplePropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        logEvent.Properties.Should().ContainKey("TraceId");
        logEvent.Properties.Should().ContainKey("SpanId");
        logEvent.Properties.Should().NotContainKey("CorrelationId");
    }

    [Fact]
    public void Enrich_WhenNoActivity_AddsCorrelationId()
    {
        // Arrange
        // Ensure no activity is current
        Activity.Current = null;

        var enricher = new CorrelationIdEnricher();
        var logEvent = CreateLogEvent();
        var propertyFactory = new SimplePropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        logEvent.Properties.Should().ContainKey("CorrelationId");
        logEvent.Properties.Should().NotContainKey("TraceId");
        logEvent.Properties.Should().NotContainKey("SpanId");
    }

    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplate(new[] { new TextToken("Test") }),
            Array.Empty<LogEventProperty>());
    }

    private sealed class SimplePropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }
}

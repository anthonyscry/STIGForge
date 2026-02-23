using System.Diagnostics;
using System.Text.Json;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// ActivityListener that writes trace spans to a local JSON file for offline analysis.
/// Each span is written as a single JSON line when the activity stops.
/// Thread-safe for concurrent span completion.
/// </summary>
public sealed class TraceFileListener : IDisposable
{
    private readonly string _tracesPath;
    private readonly object _lock = new();
    private readonly ActivityListener _listener;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of TraceFileListener and registers it with ActivitySource.
    /// </summary>
    /// <param name="logsRoot">The root directory where traces.json will be created.</param>
    public TraceFileListener(string logsRoot)
    {
        _tracesPath = Path.Combine(logsRoot, "traces.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_tracesPath)!);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("STIGForge", StringComparison.Ordinal),
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = WriteSpanToFile
        };

        ActivitySource.AddActivityListener(_listener);
    }

    private void WriteSpanToFile(Activity activity)
    {
        var span = new
        {
            traceId = activity.TraceId.ToString(),
            spanId = activity.SpanId.ToString(),
            parentSpanId = activity.ParentSpanId.ToString(),
            operationName = activity.OperationName,
            kind = activity.Kind.ToString(),
            startTime = activity.StartTimeUtc.ToString("o"),
            durationMs = activity.Duration.TotalMilliseconds,
            status = activity.Status.ToString(),
            statusDescription = activity.StatusDescription,
            tags = activity.TagObjects?.ToDictionary(t => t.Key, t => t.Value),
            events = activity.Events.Select(e => new
            {
                name = e.Name,
                timestamp = e.Timestamp.ToString("o"),
                tags = e.Tags?.ToDictionary(t => t.Key, t => t.Value)
            }).ToList()
        };

        var line = JsonSerializer.Serialize(span);
        lock (_lock)
        {
            File.AppendAllText(_tracesPath, line + Environment.NewLine);
        }
    }

    /// <summary>
    /// Disposes the ActivityListener and unregisters it from ActivitySource.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _listener.Dispose();
            _disposed = true;
        }
    }
}

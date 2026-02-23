using System.Diagnostics.Metrics;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Performance metrics collection service using .NET 8 built-in System.Diagnostics.Metrics.
/// Provides counters and histograms for mission duration, startup time, and rule processing.
/// No external NuGet packages required - uses built-in .NET 8 APIs.
/// </summary>
public static class PerformanceInstrumenter
{
    private static readonly Meter Meter = new("STIGForge.Performance");

    // Counters
    private static readonly Counter<long> MissionsCompletedCounter = Meter.CreateCounter<long>(
        "missions.completed",
        unit: "{missions}",
        description: "Total number of missions completed");

    private static readonly Counter<long> RulesProcessedCounter = Meter.CreateCounter<long>(
        "rules.processed",
        unit: "{rules}",
        description: "Total number of rules processed");

    // Histograms
    private static readonly Histogram<double> MissionDurationHistogram = Meter.CreateHistogram<double>(
        "mission.duration",
        unit: "ms",
        description: "Duration of mission execution in milliseconds");

    private static readonly Histogram<double> StartupDurationHistogram = Meter.CreateHistogram<double>(
        "startup.duration",
        unit: "ms",
        description: "Application startup duration in milliseconds");

    /// <summary>
    /// Records a completed mission with its duration and rule count.
    /// </summary>
    /// <param name="missionType">The type of mission (build, apply, verify, prove).</param>
    /// <param name="ruleCount">The number of rules processed in this mission.</param>
    /// <param name="durationMs">The total mission duration in milliseconds.</param>
    public static void RecordMissionCompleted(string missionType, int ruleCount, double durationMs)
    {
        MissionsCompletedCounter.Add(1, new KeyValuePair<string, object?>("mission.type", missionType));
        RulesProcessedCounter.Add(ruleCount, new KeyValuePair<string, object?>("mission.type", missionType));

        if (durationMs > 0)
        {
            MissionDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("mission.type", missionType));
        }
    }

    /// <summary>
    /// Records application startup duration.
    /// </summary>
    /// <param name="durationMs">The startup duration in milliseconds.</param>
    /// <param name="isColdStart">True if this was a cold start (first run), false for warm starts.</param>
    public static void RecordStartupTime(double durationMs, bool isColdStart)
    {
        if (durationMs > 0)
        {
            StartupDurationHistogram.Record(
                durationMs,
                new KeyValuePair<string, object?>("startup.cold", isColdStart));
        }
    }
}

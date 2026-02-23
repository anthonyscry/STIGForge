using System.Diagnostics;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Service for creating Activity spans for mission lifecycle phases.
/// Uses .NET 8 built-in ActivitySource for W3C-compatible distributed tracing.
/// Consumers must dispose the returned Activity (typically via using pattern).
/// </summary>
public sealed class MissionTracingService
{
    private static readonly ActivitySource ActivitySource = new(ActivitySourceNames.Missions);

    /// <summary>
    /// Starts a root mission span for the entire mission execution.
    /// </summary>
    /// <param name="bundleRoot">The root path of the mission bundle.</param>
    /// <param name="runId">Unique identifier for this mission run.</param>
    /// <returns>An Activity that must be disposed (use using pattern), or null if tracing is disabled.</returns>
    public Activity? StartMissionSpan(string bundleRoot, string runId)
    {
        var activity = ActivitySource.StartActivity("mission", ActivityKind.Server);
        if (activity != null)
        {
            activity.SetTag("bundle.root", bundleRoot);
            activity.SetTag("mission.run_id", runId);
            activity.SetTag("mission.started_at", DateTimeOffset.UtcNow.ToString("o"));
        }
        return activity;
    }

    /// <summary>
    /// Starts a child span for a specific mission phase (build, apply, verify, prove).
    /// </summary>
    /// <param name="phaseName">The name of the phase (use ActivitySourceNames constants).</param>
    /// <param name="bundleRoot">The root path of the mission bundle.</param>
    /// <returns>An Activity that must be disposed (use using pattern), or null if tracing is disabled.</returns>
    public Activity? StartPhaseSpan(string phaseName, string bundleRoot)
    {
        var activity = ActivitySource.StartActivity(phaseName, ActivityKind.Internal);
        if (activity != null)
        {
            activity.SetTag("phase.name", phaseName);
            activity.SetTag("bundle.root", bundleRoot);
        }
        return activity;
    }

    /// <summary>
    /// Adds a named event to an activity span for marking significant points during execution.
    /// </summary>
    /// <param name="activity">The activity to add the event to (null-safe).</param>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="message">Optional message to include with the event.</param>
    public void AddPhaseEvent(Activity? activity, string eventName, string? message = null)
    {
        activity?.AddEvent(new ActivityEvent(eventName, tags: new ActivityTagsCollection
        {
            ["message"] = message ?? string.Empty
        }));
    }

    /// <summary>
    /// Marks the activity as successfully completed.
    /// </summary>
    /// <param name="activity">The activity to update (null-safe).</param>
    public void SetStatusOk(Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Marks the activity as failed with an error description.
    /// </summary>
    /// <param name="activity">The activity to update (null-safe).</param>
    /// <param name="description">Description of the error that occurred.</param>
    public void SetStatusError(Activity? activity, string description)
    {
        activity?.SetStatus(ActivityStatusCode.Error, description);
    }
}

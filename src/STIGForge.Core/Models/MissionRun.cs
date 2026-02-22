using System.Text.Json.Serialization;

namespace STIGForge.Core.Models;

/// <summary>
/// Status of a mission run as a whole.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionRunStatus
{
  /// <summary>Run has been created but not yet started.</summary>
  Pending,
  /// <summary>Run is currently executing.</summary>
  Running,
  /// <summary>Run completed successfully (all required phases passed).</summary>
  Completed,
  /// <summary>Run failed with a blocking failure that halted execution.</summary>
  Failed,
  /// <summary>Run was cancelled before completing.</summary>
  Cancelled
}

/// <summary>
/// Event status for a single timeline event within a run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionEventStatus
{
  /// <summary>The step/phase started.</summary>
  Started,
  /// <summary>The step/phase finished successfully.</summary>
  Finished,
  /// <summary>The step/phase failed with a blocking error.</summary>
  Failed,
  /// <summary>The step/phase was intentionally skipped.</summary>
  Skipped,
  /// <summary>The step/phase is being retried after a prior attempt.</summary>
  Retried
}

/// <summary>
/// Phase marker identifying which high-level mission phase an event belongs to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionPhase
{
  Unknown,
  Build,
  Apply,
  Verify,
  Evidence,
  Export
}

/// <summary>
/// Canonical representation of a single mission execution run.
/// Runs are append-only records; status is derived from the timeline, not mutated in-place.
/// </summary>
public sealed class MissionRun
{
  /// <summary>Stable unique identifier for this run (UUID).</summary>
  public string RunId { get; set; } = string.Empty;

  /// <summary>Human-readable label (bundle ID, profile name, etc.).</summary>
  public string Label { get; set; } = string.Empty;

  /// <summary>Bundle root path this run operates against.</summary>
  public string BundleRoot { get; set; } = string.Empty;

  /// <summary>Overall status of the run.</summary>
  public MissionRunStatus Status { get; set; } = MissionRunStatus.Pending;

  /// <summary>UTC timestamp when the run was created/started.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>UTC timestamp when the run reached a terminal state (null if still running).</summary>
  public DateTimeOffset? FinishedAt { get; set; }

  /// <summary>
  /// SHA-256 fingerprint of the canonical inputs (bundle manifest, profile, overlays)
  /// that produced this run. Stable for identical inputs.
  /// </summary>
  public string? InputFingerprint { get; set; }

  /// <summary>Optional free-form detail (e.g., error message on failure).</summary>
  public string? Detail { get; set; }
}

/// <summary>
/// Append-only, deterministically ordered event record for a mission timeline.
/// Events are immutable once written; reruns produce new events on a new RunId.
/// </summary>
public sealed class MissionTimelineEvent
{
  /// <summary>Stable unique identifier for this event row (UUID).</summary>
  public string EventId { get; set; } = string.Empty;

  /// <summary>Run this event belongs to.</summary>
  public string RunId { get; set; } = string.Empty;

  /// <summary>
  /// Deterministic sequence index within the run.
  /// The pair (RunId, Seq) is unique and defines ordering across repeated reads.
  /// </summary>
  public int Seq { get; set; }

  /// <summary>High-level mission phase this event occurred in.</summary>
  public MissionPhase Phase { get; set; }

  /// <summary>Name of the specific step or operation within the phase.</summary>
  public string StepName { get; set; } = string.Empty;

  /// <summary>Outcome status for this event.</summary>
  public MissionEventStatus Status { get; set; }

  /// <summary>UTC timestamp when the event was recorded.</summary>
  public DateTimeOffset OccurredAt { get; set; }

  /// <summary>Optional free-form human-readable message.</summary>
  public string? Message { get; set; }

  /// <summary>
  /// Optional reference to an evidence artifact linked to this event.
  /// Format: relative path within the bundle root.
  /// </summary>
  public string? EvidencePath { get; set; }

  /// <summary>
  /// Optional SHA-256 checksum of the linked evidence artifact.
  /// </summary>
  public string? EvidenceSha256 { get; set; }
}

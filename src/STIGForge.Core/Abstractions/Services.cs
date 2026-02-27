using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Append-only repository for mission run records and their deterministic timeline events.
/// Implementations must not allow in-place mutation of historical events.
/// </summary>
public interface IMissionRunRepository
{
  /// <summary>
  /// Creates a new mission run record. Fails if a run with the same RunId already exists.
  /// </summary>
  Task CreateRunAsync(MissionRun run, CancellationToken ct);

  /// <summary>
  /// Updates the status and terminal fields of an existing run (status, FinishedAt, Detail).
  /// This is the only permitted mutation path; timeline events remain append-only.
  /// </summary>
  Task UpdateRunStatusAsync(string runId, MissionRunStatus status, DateTimeOffset? finishedAt, string? detail, CancellationToken ct);

  /// <summary>
  /// Returns the most recently created run record, or null if none exist.
  /// </summary>
  Task<MissionRun?> GetLatestRunAsync(CancellationToken ct);

  /// <summary>
  /// Returns the run record with the given ID, or null if not found.
  /// </summary>
  Task<MissionRun?> GetRunAsync(string runId, CancellationToken ct);

  /// <summary>
  /// Returns all run records ordered by CreatedAt descending.
  /// </summary>
  Task<IReadOnlyList<MissionRun>> ListRunsAsync(CancellationToken ct);

  /// <summary>
  /// Appends a timeline event to the run's ledger. Rejects duplicate (RunId, Seq) pairs.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when a duplicate (RunId, Seq) is detected; the database enforces uniqueness.
  /// </exception>
  Task AppendEventAsync(MissionTimelineEvent evt, CancellationToken ct);

  /// <summary>
  /// Returns all timeline events for the given run, ordered by Seq ascending (deterministic).
  /// </summary>
  Task<IReadOnlyList<MissionTimelineEvent>> GetTimelineAsync(string runId, CancellationToken ct);
}

public interface IClock
{
  DateTimeOffset Now { get; }
}

/// <summary>
/// Tamper-evident audit trail for compliance-relevant actions.
/// Each entry is chained to the previous via SHA-256 hash for integrity verification.
/// </summary>
public interface IAuditTrailService
{
  Task RecordAsync(AuditEntry entry, CancellationToken ct);
  Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct);
  Task<bool> VerifyIntegrityAsync(CancellationToken ct);
}

public sealed class AuditEntry
{
  public long Id { get; set; }
  public DateTimeOffset Timestamp { get; set; }
  public string User { get; set; } = string.Empty;
  public string Machine { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string Target { get; set; } = string.Empty;
  public string Result { get; set; } = string.Empty;
  public string Detail { get; set; } = string.Empty;
  public string PreviousHash { get; set; } = string.Empty;
  public string EntryHash { get; set; } = string.Empty;
}

public sealed class AuditQuery
{
  public string? Action { get; set; }
  public string? Target { get; set; }
  public DateTimeOffset? From { get; set; }
  public DateTimeOffset? To { get; set; }
  public int Limit { get; set; } = 100;
}

public interface IHashingService
{
  Task<string> Sha256FileAsync(string path, CancellationToken ct);
  Task<string> Sha256TextAsync(string content, CancellationToken ct);
}

public interface IPathBuilder
{
  string GetAppDataRoot();
  string GetContentPacksRoot();
  string GetPackRoot(string packId);
  string GetBundleRoot(string bundleId);
  string GetLogsRoot();
  string GetImportRoot();
  string GetImportInboxRoot();
  string GetImportIndexPath();
  string GetToolsRoot();
  string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts);
}

public interface IClassificationScopeService
{
  CompiledControls Compile(Profile profile, IReadOnlyList<ControlRecord> controls);
}

public sealed class CompiledControl
{
  public CompiledControl(ControlRecord control, ControlStatus status, string? comment, bool needsReview, string? reviewReason)
  {
    Control = control;
    Status = status;
    Comment = comment;
    NeedsReview = needsReview;
    ReviewReason = reviewReason;
  }

  public ControlRecord Control { get; }
  public ControlStatus Status { get; }
  public string? Comment { get; }
  public bool NeedsReview { get; }
  public string? ReviewReason { get; }
}

public sealed class CompiledControls
{
  public CompiledControls(IReadOnlyList<CompiledControl> controls, IReadOnlyList<CompiledControl> reviewQueue)
  {
    Controls = controls;
    ReviewQueue = reviewQueue;
  }

  public IReadOnlyList<CompiledControl> Controls { get; }
  public IReadOnlyList<CompiledControl> ReviewQueue { get; }
}

/// <summary>
/// Secure credential storage for fleet operations using DPAPI (Windows Data Protection).
/// Credentials are encrypted per-user and stored locally.
/// </summary>
public interface ICredentialStore
{
  void Save(string targetHost, string username, string password);
  (string Username, string Password)? Load(string targetHost);
  bool Remove(string targetHost);
  IReadOnlyList<string> ListHosts();
}

public interface IVerificationWorkflowService
{
  Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct);
}

public interface ILocalWorkflowService
{
  Task<LocalWorkflowResult> RunAsync(LocalWorkflowRequest request, CancellationToken ct);
}

public sealed class LocalWorkflowRequest
{
  public string OutputRoot { get; set; } = string.Empty;

  public string ImportRoot { get; set; } = string.Empty;

  public string ToolRoot { get; set; } = string.Empty;
}

public sealed class LocalWorkflowResult
{
  public LocalWorkflowMission Mission { get; set; } = new();

  public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class VerificationWorkflowRequest
{
  public string OutputRoot { get; set; } = string.Empty;

  public string ConsolidatedToolLabel { get; set; } = "Verification";

  public EvaluateStigWorkflowOptions EvaluateStig { get; set; } = new();

  public ScapWorkflowOptions Scap { get; set; } = new();
}

public sealed class EvaluateStigWorkflowOptions
{
  public bool Enabled { get; set; }

  public string ToolRoot { get; set; } = string.Empty;

  public string Arguments { get; set; } = string.Empty;

  public string? WorkingDirectory { get; set; }
}

public sealed class ScapWorkflowOptions
{
  public bool Enabled { get; set; }

  public string CommandPath { get; set; } = string.Empty;

  public string Arguments { get; set; } = string.Empty;

  public string? WorkingDirectory { get; set; }

  public int TimeoutSeconds { get; set; } = 300;

  public string ToolLabel { get; set; } = "SCAP";
}

public sealed class VerificationWorkflowResult
{
  public DateTimeOffset StartedAt { get; set; }

  public DateTimeOffset FinishedAt { get; set; }

  public string ConsolidatedJsonPath { get; set; } = string.Empty;

  public string ConsolidatedCsvPath { get; set; } = string.Empty;

  public string CoverageSummaryJsonPath { get; set; } = string.Empty;

  public string CoverageSummaryCsvPath { get; set; } = string.Empty;

  public int ConsolidatedResultCount { get; set; }

  public int TotalRuleCount { get; set; }

  public int PassCount { get; set; }

  public int FailCount { get; set; }

  public int NotApplicableCount { get; set; }

  public int NotReviewedCount { get; set; }

  public int ErrorCount { get; set; }

  public int CatICount { get; set; }

  public int CatIICount { get; set; }

  public int CatIIICount { get; set; }

  public IReadOnlyList<VerificationToolRunResult> ToolRuns { get; set; } = Array.Empty<VerificationToolRunResult>();

  public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class VerificationToolRunResult
{
  public string Tool { get; set; } = string.Empty;

  public bool Executed { get; set; }

  public int ExitCode { get; set; }

  public DateTimeOffset StartedAt { get; set; }

  public DateTimeOffset FinishedAt { get; set; }

  public string Output { get; set; } = string.Empty;

  public string Error { get; set; } = string.Empty;
}

public interface IBundleMissionSummaryService
{
  BundleMissionSummary LoadSummary(string bundleRoot);

  /// <summary>
  /// Loads the latest mission run timeline projection for the given bundle.
  /// Returns null if no timeline data is available (repository not configured).
  /// </summary>
  Task<MissionTimelineSummary?> LoadTimelineSummaryAsync(string bundleRoot, CancellationToken ct);

  string NormalizeStatus(string? status);
}

public sealed class BundleMissionSummary
{
  public string BundleRoot { get; set; } = string.Empty;

  public string PackName { get; set; } = "unknown";

  public string ProfileName { get; set; } = "unknown";

  public int TotalControls { get; set; }

  public int AutoControls { get; set; }

  public int ManualControls { get; set; }

  public BundleVerifySummary Verify { get; set; } = new();

  public BundleManualSummary Manual { get; set; } = new();

  public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class BundleVerifySummary
{
  public int ClosedCount { get; set; }

  public int OpenCount { get; set; }

  public int TotalCount { get; set; }

  public int ReportCount { get; set; }

  public int BlockingFailureCount { get; set; }

  public int RecoverableWarningCount { get; set; }

  public int OptionalSkipCount { get; set; }
}

public sealed class BundleManualSummary
{
  public int PassCount { get; set; }

  public int FailCount { get; set; }

  public int NotApplicableCount { get; set; }

  public int OpenCount { get; set; }

  public int AnsweredCount { get; set; }

  public int TotalCount { get; set; }

  public double PercentComplete { get; set; }
}

/// <summary>
/// Timeline projection summary derived from persisted mission run ledger data.
/// Contains the latest run and its ordered timeline events for operator visibility.
/// </summary>
public sealed class MissionTimelineSummary
{
  /// <summary>The latest mission run that produced this timeline, or null if no runs exist.</summary>
  public MissionRun? LatestRun { get; set; }

  /// <summary>Deterministically ordered timeline events for the latest run (Seq ascending).</summary>
  public IReadOnlyList<MissionTimelineEvent> Events { get; set; } = Array.Empty<MissionTimelineEvent>();

  /// <summary>Last phase reached in the latest run (derived from events), or null if no events.</summary>
  public MissionPhase? LastPhase { get; set; }

  /// <summary>The last event step name recorded in the timeline, or null if no events.</summary>
  public string? LastStepName { get; set; }

  /// <summary>The last event status recorded (i.e. whether the last step started/finished/failed/skipped).</summary>
  public MissionEventStatus? LastEventStatus { get; set; }

  /// <summary>
  /// True when the latest run has a blocking failed event with no subsequent finished event
  /// for the same phase+step, indicating the mission is currently in a blocked state.
  /// </summary>
  public bool IsBlocked { get; set; }

  /// <summary>Human-readable next-action message derived from the timeline state.</summary>
  public string NextAction { get; set; } = string.Empty;
}

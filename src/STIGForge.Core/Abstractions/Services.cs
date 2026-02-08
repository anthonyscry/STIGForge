using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

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

namespace STIGForge.Verify;

/// <summary>
/// Normalized verification result schema - common format for SCAP, Evaluate-STIG, and CKL outputs.
/// Maps tool-specific output to canonical schema for unified reporting.
/// </summary>
public sealed class NormalizedVerifyResult
{
  /// <summary>Control identifier (VulnId preferred, fallback to RuleId)</summary>
  public string ControlId { get; set; } = string.Empty;

  /// <summary>VulnId if available (e.g., V-220697)</summary>
  public string? VulnId { get; set; }

  /// <summary>RuleId if available (e.g., SV-220697r569187_rule)</summary>
  public string? RuleId { get; set; }

  /// <summary>Control title/description</summary>
  public string? Title { get; set; }

  /// <summary>Severity (high/medium/low)</summary>
  public string? Severity { get; set; }

  /// <summary>
  /// Verification status: Pass, Fail, NotApplicable, NotReviewed, Informational, Error
  /// Normalized from tool-specific statuses:
  /// - SCAP: pass/fail/notapplicable/notchecked/informational/error
  /// - CKL: NotAFinding/Open/Not_Applicable/Not_Reviewed
  /// - Evaluate-STIG: Compliant/NonCompliant/NotApplicable/NotReviewed
  /// </summary>
  public VerifyStatus Status { get; set; }

  /// <summary>Finding details/evidence text</summary>
  public string? FindingDetails { get; set; }

  /// <summary>Comments/notes from reviewer</summary>
  public string? Comments { get; set; }

  /// <summary>Tool that generated this result (SCAP, Evaluate-STIG, Manual CKL)</summary>
  public string Tool { get; set; } = string.Empty;

  /// <summary>Source file that contained this result</summary>
  public string SourceFile { get; set; } = string.Empty;

  /// <summary>When this verification was performed</summary>
  public DateTimeOffset? VerifiedAt { get; set; }

  /// <summary>Paths to evidence files supporting this result</summary>
  public IReadOnlyList<string> EvidencePaths { get; set; } = Array.Empty<string>();

  /// <summary>Tool-specific metadata (e.g., SCAP check-content-ref, Evaluate-STIG test ID)</summary>
  public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

  /// <summary>
  /// Absolute path to the raw tool output artifact this result was parsed from.
  /// Retained for provenance: links this normalized result back to the original tool output.
  /// </summary>
  public string? RawArtifactPath { get; set; }

  /// <summary>
  /// SCAP benchmark ID this result maps to, populated from ScapMappingManifest.
  /// Null if no mapping manifest was applied or the control is unmapped.
  /// </summary>
  public string? BenchmarkId { get; set; }
}

/// <summary>
/// Normalized verification status enumeration.
/// Provides common vocabulary across different verification tools.
/// </summary>
public enum VerifyStatus
{
  /// <summary>Unknown/unmapped status</summary>
  Unknown = 0,

  /// <summary>Control passed verification (NotAFinding, pass, Compliant)</summary>
  Pass = 1,

  /// <summary>Control failed verification (Open, fail, NonCompliant)</summary>
  Fail = 2,

  /// <summary>Control not applicable to this system (Not_Applicable, notapplicable, NotApplicable)</summary>
  NotApplicable = 3,

  /// <summary>Control not yet reviewed (Not_Reviewed, notchecked, NotReviewed)</summary>
  NotReviewed = 4,

  /// <summary>Informational finding (no pass/fail)</summary>
  Informational = 5,

  /// <summary>Error occurred during verification</summary>
  Error = 6
}

/// <summary>
/// Container for a complete verification run with all results.
/// Supports merging results from multiple tools.
/// </summary>
public sealed class NormalizedVerifyReport
{
  /// <summary>Tool name that generated this report</summary>
  public string Tool { get; set; } = string.Empty;

  /// <summary>Tool version</summary>
  public string ToolVersion { get; set; } = string.Empty;

  /// <summary>When verification started</summary>
  public DateTimeOffset StartedAt { get; set; }

  /// <summary>When verification finished</summary>
  public DateTimeOffset FinishedAt { get; set; }

  /// <summary>Root directory containing verification outputs</summary>
  public string OutputRoot { get; set; } = string.Empty;

  /// <summary>All verification results</summary>
  public IReadOnlyList<NormalizedVerifyResult> Results { get; set; } = Array.Empty<NormalizedVerifyResult>();

  /// <summary>Summary statistics</summary>
  public VerifySummary Summary { get; set; } = new VerifySummary();

  /// <summary>Errors/warnings encountered during verification</summary>
  public IReadOnlyList<string> DiagnosticMessages { get; set; } = Array.Empty<string>();

  /// <summary>
  /// Absolute path to the raw tool output file this report was parsed from.
  /// Report-level provenance for audit traceability.
  /// </summary>
  public string? RawArtifactPath { get; set; }
}

/// <summary>
/// Summary statistics for a verification run.
/// </summary>
public sealed class VerifySummary
{
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int NotApplicableCount { get; set; }
  public int NotReviewedCount { get; set; }
  public int InformationalCount { get; set; }
  public int ErrorCount { get; set; }
  public int TotalCount { get; set; }

  /// <summary>Compliance percentage (Pass / (Pass + Fail + Error)) * 100</summary>
  public double CompliancePercent { get; set; }
}

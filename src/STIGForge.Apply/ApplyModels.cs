using STIGForge.Apply.Lgpo;
using STIGForge.Core.Models;

namespace STIGForge.Apply;

public enum ConvergenceStatus
{
  Converged,
  Diverged,
  Exceeded,
  NotApplicable
}

public sealed class ApplyRequest
{
   public string BundleRoot { get; set; } = string.Empty;
   public HardeningMode? ModeOverride { get; set; }
   public string? ScriptPath { get; set; }
   public string? ScriptArgs { get; set; }
   public string? DscMofPath { get; set; }
   public bool DscVerbose { get; set; }
   public bool SkipSnapshot { get; set; }
   public string? PowerStigModulePath { get; set; }
   public string? PowerStigDataFile { get; set; }
   public string? PowerStigOutputPath { get; set; }
   public bool PowerStigVerbose { get; set; }
   public string? PowerStigDataGeneratedPath { get; set; }
   public bool ResetLcmAfterApply { get; set; } = false;

   /// <summary>Target OS for PowerSTIG composite resource selection. When set, the PowerSTIG
   /// compile step uses the appropriate composite DSC resource (e.g., WindowsServer/WindowsClient)
   /// with the matching OsVersion parameter instead of the generic New-StigDscConfiguration.</summary>
   public OsTarget? OsTarget { get; set; }

   /// <summary>Target role template for PowerSTIG STIG type resolution (MS vs DC for servers).</summary>
   public RoleTemplate? RoleTemplate { get; set; }

   /// <summary>LGPO .pol file path for Group Policy remediation (secondary backend).</summary>
   public string? LgpoPolFilePath { get; set; }

   /// <summary>LGPO scope (Machine or User). Defaults to Machine.</summary>
   public LgpoScope? LgpoScope { get; set; }

   /// <summary>Optional override path to LGPO.exe.</summary>
   public string? LgpoExePath { get; set; }

   public string? AdmxTemplateRootPath { get; set; }

   public string? AdmxPolicyDefinitionsPath { get; set; }

   /// <summary>
   /// Optional mission run ID for timeline and evidence provenance linkage.
   /// When set, apply step evidence metadata will include this run ID.
   /// </summary>
   public string? RunId { get; set; }

   /// <summary>
   /// Optional prior run ID for rerun continuity. When set, the apply run will
   /// link to the prior run via lineage markers and deduplicate unchanged step evidence.
   /// </summary>
   public string? PriorRunId { get; set; }
}

public sealed class ApplyStepOutcome
{
  public string StepName { get; set; } = string.Empty;
  public int ExitCode { get; set; }
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public string StdOutPath { get; set; } = string.Empty;
  public string StdErrPath { get; set; } = string.Empty;

  /// <summary>Run-scoped evidence metadata path written after step completion.</summary>
  public string? EvidenceMetadataPath { get; set; }

  /// <summary>SHA-256 of the primary step artifact (stdout log) for dedupe and lineage.</summary>
  public string? ArtifactSha256 { get; set; }

  /// <summary>
  /// Continuity marker for this step's evidence relative to a prior run.
  /// "retained" = artifact is identical (same SHA-256 as prior run).
  /// "superseded" = new artifact replaces prior run's artifact.
  /// null = no prior run comparison available.
  /// </summary>
  public string? ContinuityMarker { get; set; }
}

public sealed class ApplyResult
{
   public string BundleRoot { get; set; } = string.Empty;
   public HardeningMode Mode { get; set; }
   public string LogPath { get; set; } = string.Empty;
   public IReadOnlyList<ApplyStepOutcome> Steps { get; set; } = Array.Empty<ApplyStepOutcome>();
   public string SnapshotId { get; set; } = string.Empty;
   public string RollbackScriptPath { get; set; } = string.Empty;
   public bool IsMissionComplete { get; set; }
   public bool IntegrityVerified { get; set; }
   public IReadOnlyList<string> BlockingFailures { get; set; } = Array.Empty<string>();
   public IReadOnlyList<string> RecoveryArtifactPaths { get; set; } = Array.Empty<string>();

   /// <summary>The run ID this result belongs to (propagated from ApplyRequest.RunId if set).</summary>
   public string? RunId { get; set; }

   /// <summary>Prior run ID used for continuity markers, if a rerun was detected.</summary>
   public string? PriorRunId { get; set; }

   /// <summary>Number of reboots that occurred during this apply cycle.</summary>
   public int RebootCount { get; set; }

   /// <summary>Convergence status of the apply cycle.</summary>
   public ConvergenceStatus ConvergenceStatus { get; set; } = ConvergenceStatus.NotApplicable;
}

public sealed class PreflightRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? ModulesPath { get; set; }
  public string? PowerStigModulePath { get; set; }
  public bool CheckLgpoConflict { get; set; }
  public string? BundleManifestPath { get; set; }
}

public sealed class PreflightResult
{
  public bool Ok { get; set; }
  public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
  public string Timestamp { get; set; } = string.Empty;
  public int ExitCode { get; set; }
}

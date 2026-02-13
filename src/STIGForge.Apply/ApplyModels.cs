using STIGForge.Core.Models;

namespace STIGForge.Apply;

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
   public string? AdmxSourcePath { get; set; }
   public string? LgpoExePath { get; set; }
   public string? LgpoGpoBackupPath { get; set; }
   public bool LgpoVerbose { get; set; }
 }

public sealed class ApplyStepOutcome
{
  public string StepName { get; set; } = string.Empty;
  public int ExitCode { get; set; }
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public string StdOutPath { get; set; } = string.Empty;
  public string StdErrPath { get; set; } = string.Empty;
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
}

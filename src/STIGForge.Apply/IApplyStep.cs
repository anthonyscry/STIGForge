using STIGForge.Core.Models;

namespace STIGForge.Apply;

/// <summary>
/// A single step in the apply pipeline (PowerSTIG, Script, DSC, ADMX, LGPO, GPO Import).
/// Steps are iterated by ApplyRunner — each decides whether it can execute based on the
/// request, runs its handler, and reports whether a reboot check is warranted afterward.
/// </summary>
public interface IApplyStep
{
  /// <summary>Step identifier used in resume markers and timeline events.</summary>
  string Name { get; }

  /// <summary>True when this step's required request fields are populated.</summary>
  bool CanExecute(ApplyRequest request);

  /// <summary>
  /// True when a reboot detection check should run after this step completes.
  /// Steps that modify system configuration (PowerSTIG, Script, LGPO) set this;
  /// file-copy steps (ADMX, GPO) do not.
  /// </summary>
  bool CanTriggerReboot { get; }

  /// <summary>Executes the step and returns its outcome.</summary>
  Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct);
}

/// <summary>
/// Shared context passed to every apply step. Contains paths and mode
/// derived from the request but common across all steps.
/// </summary>
public sealed class ApplyStepContext
{
  public required string BundleRoot { get; init; }
  public required string ApplyRoot { get; init; }
  public required string LogsDir { get; init; }
  public required string SnapshotsDir { get; init; }
  public required HardeningMode Mode { get; init; }
  public required string RunId { get; init; }
}

using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Steps;

internal sealed class ScriptApplyStep : IApplyStep
{
  private readonly ScriptStepHandler _handler;

  public ScriptApplyStep(IProcessRunner processRunner)
  {
    _handler = new ScriptStepHandler(processRunner);
  }

  public string Name => "apply_script";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.ScriptPath);

  public bool CanTriggerReboot => true;

  public async Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    return await _handler.RunAsync(
      request.ScriptPath!,
      request.ScriptArgs,
      context.BundleRoot,
      context.LogsDir,
      context.SnapshotsDir,
      context.Mode,
      Name,
      ct).ConfigureAwait(false);
  }
}

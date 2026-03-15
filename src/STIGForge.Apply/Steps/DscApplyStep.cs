using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Steps;

internal sealed class DscApplyStep : IApplyStep
{
  private readonly DscStepHandler _handler;

  public DscApplyStep(IProcessRunner processRunner)
  {
    _handler = new DscStepHandler(processRunner);
  }

  public string Name => "apply_dsc";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.DscMofPath);

  public bool CanTriggerReboot => false;

  public async Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    return await _handler.RunAsync(
      request.DscMofPath!,
      context.BundleRoot,
      context.LogsDir,
      context.SnapshotsDir,
      context.Mode,
      request.DscVerbose,
      Name,
      ct).ConfigureAwait(false);
  }
}

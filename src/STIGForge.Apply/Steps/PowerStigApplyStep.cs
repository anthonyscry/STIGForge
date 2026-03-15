using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Steps;

internal sealed class PowerStigApplyStep : IApplyStep
{
  private readonly PowerStigStepHandler _handler;

  public PowerStigApplyStep(IProcessRunner processRunner)
  {
    _handler = new PowerStigStepHandler(processRunner);
  }

  public string Name => "powerstig_compile";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.PowerStigModulePath);

  public bool CanTriggerReboot => true;

  public async Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    var outputPath = string.IsNullOrWhiteSpace(request.PowerStigOutputPath)
      ? Path.Combine(context.ApplyRoot, "Dsc")
      : request.PowerStigOutputPath!;

    PowerStigTarget? target = null;
    if (request.OsTarget.HasValue && request.OsTarget.Value != OsTarget.Unknown)
    {
      target = PowerStigTechnologyMap.Resolve(
        request.OsTarget.Value,
        request.RoleTemplate ?? RoleTemplate.Workstation);
    }

    return await _handler.RunCompileAsync(
      request.PowerStigModulePath!,
      request.PowerStigDataFile,
      outputPath,
      context.BundleRoot,
      context.LogsDir,
      context.SnapshotsDir,
      context.Mode,
      request.PowerStigVerbose,
      Name,
      ct,
      target,
      request.OrgSettingsPath).ConfigureAwait(false);
  }
}

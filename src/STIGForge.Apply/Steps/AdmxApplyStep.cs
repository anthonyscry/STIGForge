using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Steps;

internal sealed class AdmxApplyStep : IApplyStep
{
  private readonly PolicyStepHandler _handler;

  public AdmxApplyStep(ILogger<ApplyRunner> logger, Lgpo.LgpoRunner? lgpoRunner, IProcessRunner processRunner)
  {
    _handler = new PolicyStepHandler(logger, lgpoRunner, processRunner);
  }

  public string Name => "apply_admx_templates";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath);

  public bool CanTriggerReboot => false;

  public Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    var outcome = _handler.RunAdmxImport(request, context.BundleRoot, context.LogsDir, Name);
    return Task.FromResult(outcome);
  }
}

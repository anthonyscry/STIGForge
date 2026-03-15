using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Steps;

internal sealed class GpoImportApplyStep : IApplyStep
{
  private readonly PolicyStepHandler _handler;

  public GpoImportApplyStep(ILogger<ApplyRunner> logger, Lgpo.LgpoRunner? lgpoRunner, IProcessRunner processRunner)
  {
    _handler = new PolicyStepHandler(logger, lgpoRunner, processRunner);
  }

  public string Name => "apply_gpo_import";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.DomainGpoBackupPath)
       && request.RoleTemplate == RoleTemplate.DomainController;

  public bool CanTriggerReboot => false;

  public async Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    return await _handler.RunGpoImportAsync(request, context.LogsDir, Name, ct).ConfigureAwait(false);
  }
}

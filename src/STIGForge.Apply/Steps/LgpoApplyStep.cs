using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Steps;

internal sealed class LgpoApplyStep : IApplyStep
{
  private readonly PolicyStepHandler _handler;

  public LgpoApplyStep(ILogger<ApplyRunner> logger, Lgpo.LgpoRunner? lgpoRunner, IProcessRunner processRunner)
  {
    _handler = new PolicyStepHandler(logger, lgpoRunner, processRunner);
  }

  public string Name => "apply_lgpo";

  public bool CanExecute(ApplyRequest request)
    => !string.IsNullOrWhiteSpace(request.LgpoPolFilePath) && _handler.CanRunLgpo;

  public bool CanTriggerReboot => true;

  public async Task<ApplyStepOutcome> ExecuteAsync(ApplyRequest request, ApplyStepContext context, CancellationToken ct)
  {
    return await _handler.RunLgpoAsync(request, context.LogsDir, Name, ct).ConfigureAwait(false);
  }
}

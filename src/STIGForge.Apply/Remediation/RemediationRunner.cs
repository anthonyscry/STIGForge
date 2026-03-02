using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Remediation;

/// <summary>
/// Orchestrates per-rule remediation handlers against a set of controls.
/// </summary>
public sealed class RemediationRunner
{
    private readonly IReadOnlyDictionary<string, IRemediationHandler> _handlers;
    private readonly ILogger<RemediationRunner>? _logger;

    public RemediationRunner(IEnumerable<IRemediationHandler> handlers, ILogger<RemediationRunner>? logger = null)
    {
        _handlers = handlers.ToDictionary(h => h.RuleId, h => h, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<RemediationRunResult> RunAsync(
        IReadOnlyList<ControlRecord> controls,
        RemediationContext baseContext,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<RemediationResult>();
        var success = 0;
        var failed = 0;
        var changed = 0;
        var skipped = 0;

        foreach (var control in controls)
        {
            var ruleId = control.ExternalIds.RuleId;
            if (string.IsNullOrWhiteSpace(ruleId) || !_handlers.TryGetValue(ruleId, out var handler))
            {
                skipped++;
                continue;
            }

            var context = new RemediationContext
            {
                BundleRoot = baseContext.BundleRoot,
                Mode = baseContext.Mode,
                DryRun = baseContext.DryRun,
                Control = control
            };

            try
            {
                RemediationResult result;
                if (baseContext.DryRun)
                {
                    result = await handler.TestAsync(context, ct).ConfigureAwait(false);
                    result.Detail = "[DRY-RUN] " + (result.Detail ?? "Test completed");
                }
                else
                {
                    result = await handler.ApplyAsync(context, ct).ConfigureAwait(false);
                }

                results.Add(result);
                if (result.Success)
                {
                    success++;
                }
                else
                {
                    failed++;
                }

                if (result.Changed)
                {
                    changed++;
                }

                _logger?.LogInformation(
                    "Remediation {RuleId}: {Status} (changed={Changed})",
                    ruleId,
                    result.Success ? "OK" : "FAIL",
                    result.Changed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Remediation handler failed for {RuleId}", ruleId);
                results.Add(new RemediationResult
                {
                    RuleId = ruleId,
                    HandlerCategory = handler.Category,
                    Success = false,
                    Changed = false,
                    ErrorMessage = ex.Message
                });
                failed++;
            }
        }

        return new RemediationRunResult
        {
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            TotalHandled = success + failed,
            SuccessCount = success,
            FailedCount = failed,
            ChangedCount = changed,
            SkippedCount = skipped,
            Results = results
        };
    }

    public IReadOnlyList<string> GetSupportedRuleIds() => _handlers.Keys.ToList();

    public bool HasHandler(string ruleId) => _handlers.ContainsKey(ruleId);
}

using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Security;

/// <summary>
/// Orchestrates all registered security feature services (WDAC, BitLocker, Firewall).
/// </summary>
public sealed class SecurityFeatureRunner
{
    private readonly IReadOnlyList<ISecurityFeatureService> _services;
    private readonly ILogger<SecurityFeatureRunner>? _logger;

    public SecurityFeatureRunner(IEnumerable<ISecurityFeatureService> services, ILogger<SecurityFeatureRunner>? logger = null)
    {
        _services = services.ToList();
        _logger = logger;
    }

    public async Task<SecurityRunResult> RunAllAsync(SecurityFeatureRequest request, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<SecurityFeatureResult>();
        var success = 0;
        var failed = 0;
        var changed = 0;

        foreach (var service in _services)
        {
            try
            {
                SecurityFeatureResult result;
                if (request.DryRun)
                    result = await service.TestAsync(request, ct).ConfigureAwait(false);
                else
                    result = await service.ApplyAsync(request, ct).ConfigureAwait(false);

                results.Add(result);
                if (result.Success)
                    success++;
                else
                    failed++;

                if (result.Changed)
                    changed++;

                _logger?.LogInformation(
                    "Security feature {Feature}: {Status} (changed={Changed})",
                    service.FeatureName,
                    result.Success ? "OK" : "FAIL",
                    result.Changed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Security feature {Feature} failed", service.FeatureName);
                results.Add(new SecurityFeatureResult
                {
                    FeatureName = service.FeatureName,
                    Success = false,
                    Changed = false,
                    ErrorMessage = ex.Message,
                    Diagnostics = new[] { ex.ToString() }
                });
                failed++;
            }
        }

        return new SecurityRunResult
        {
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            TotalFeatures = _services.Count,
            SuccessCount = success,
            FailedCount = failed,
            ChangedCount = changed,
            Results = results
        };
    }
}

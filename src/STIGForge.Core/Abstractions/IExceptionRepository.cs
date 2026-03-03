using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persists and retrieves control exceptions (waivers/deviations) that temporarily
/// exempt specific rules from compliance enforcement within a bundle.
/// </summary>
public interface IExceptionRepository
{
  Task SaveAsync(ControlException exception, CancellationToken ct);
  Task<ControlException?> GetAsync(string exceptionId, CancellationToken ct);
  Task<IReadOnlyList<ControlException>> ListByBundleAsync(string bundleRoot, CancellationToken ct);
  Task<IReadOnlyList<ControlException>> ListActiveByRuleAsync(string bundleRoot, string ruleId, CancellationToken ct);
  Task RevokeAsync(string exceptionId, string revokedBy, DateTimeOffset revokedAt, CancellationToken ct);
}

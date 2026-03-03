using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persists and retrieves release-check records that track quarterly STIG update
/// evaluations against a baseline content pack.
/// </summary>
public interface IReleaseCheckRepository
{
  Task SaveAsync(ReleaseCheck check, CancellationToken ct);
  Task<ReleaseCheck?> GetAsync(string checkId, CancellationToken ct);
  Task<IReadOnlyList<ReleaseCheck>> ListByBaselineAsync(string baselinePackId, int limit, CancellationToken ct);
  Task<ReleaseCheck?> GetLatestAsync(string baselinePackId, CancellationToken ct);
}

using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persists and retrieves drift detection snapshots that track configuration changes
/// between verification runs for individual rules.
/// </summary>
public interface IDriftRepository
{
  Task SaveAsync(DriftSnapshot snapshot, CancellationToken ct);
  Task<IReadOnlyList<DriftSnapshot>> GetDriftHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct);
  Task<DriftSnapshot?> GetLatestSnapshotAsync(string bundleRoot, string ruleId, CancellationToken ct);
}

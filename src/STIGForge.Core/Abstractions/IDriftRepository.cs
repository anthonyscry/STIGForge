using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IDriftRepository
{
  Task SaveAsync(DriftSnapshot snapshot, CancellationToken ct);
  Task<IReadOnlyList<DriftSnapshot>> GetDriftHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct);
  Task<DriftSnapshot?> GetLatestSnapshotAsync(string bundleRoot, string ruleId, CancellationToken ct);
}

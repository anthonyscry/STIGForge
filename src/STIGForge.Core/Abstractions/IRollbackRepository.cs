using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IRollbackRepository
{
  Task SaveAsync(RollbackSnapshot snapshot, CancellationToken ct);
  Task<RollbackSnapshot?> GetAsync(string snapshotId, CancellationToken ct);
  Task<IReadOnlyList<RollbackSnapshot>> ListByBundleAsync(string bundleRoot, int limit, CancellationToken ct);
}

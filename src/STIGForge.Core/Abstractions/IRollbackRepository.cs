using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persists and retrieves rollback snapshots that capture pre-hardening system state
/// for safe reversion of applied security configurations.
/// </summary>
public interface IRollbackRepository
{
  Task SaveAsync(RollbackSnapshot snapshot, CancellationToken ct);
  Task<RollbackSnapshot?> GetAsync(string snapshotId, CancellationToken ct);
  Task<IReadOnlyList<RollbackSnapshot>> ListByBundleAsync(string bundleRoot, int limit, CancellationToken ct);
}

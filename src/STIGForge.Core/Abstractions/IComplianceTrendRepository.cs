using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Persists and retrieves point-in-time compliance snapshots for trend analysis,
/// enabling tracking of pass/fail/NA counts over successive hardening cycles.
/// </summary>
public interface IComplianceTrendRepository
{
  Task SaveSnapshotAsync(ComplianceSnapshot snapshot, CancellationToken ct);
  Task<IReadOnlyList<ComplianceSnapshot>> GetSnapshotsAsync(string bundleRoot, int limit, CancellationToken ct);
  Task<ComplianceSnapshot?> GetLatestSnapshotAsync(string bundleRoot, CancellationToken ct);
}

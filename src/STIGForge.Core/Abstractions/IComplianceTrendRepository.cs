using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IComplianceTrendRepository
{
  Task SaveSnapshotAsync(ComplianceSnapshot snapshot, CancellationToken ct);
  Task<IReadOnlyList<ComplianceSnapshot>> GetSnapshotsAsync(string bundleRoot, int limit, CancellationToken ct);
  Task<ComplianceSnapshot?> GetLatestSnapshotAsync(string bundleRoot, CancellationToken ct);
}

using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteComplianceTrendRepository : IComplianceTrendRepository
{
  private readonly string _cs;

  public SqliteComplianceTrendRepository(string connectionString)
  {
    if (string.IsNullOrEmpty(connectionString)) throw new ArgumentException("Value cannot be null or empty.", nameof(connectionString));
    _cs = connectionString;
  }

  public async Task SaveSnapshotAsync(ComplianceSnapshot snapshot, CancellationToken ct)
  {
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

    const string sql = @"
INSERT INTO compliance_snapshots(snapshot_id, bundle_root, run_id, pack_id, captured_at,
  pass_count, fail_count, error_count, not_applicable_count, not_reviewed_count,
  total_count, compliance_percent, tool)
VALUES(@SnapshotId, @BundleRoot, @RunId, @PackId, @CapturedAt,
  @PassCount, @FailCount, @ErrorCount, @NotApplicableCount, @NotReviewedCount,
  @TotalCount, @CompliancePercent, @Tool)
ON CONFLICT(snapshot_id) DO UPDATE SET
  compliance_percent=excluded.compliance_percent,
  pass_count=excluded.pass_count,
  fail_count=excluded.fail_count;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        snapshot.SnapshotId,
        snapshot.BundleRoot,
        snapshot.RunId,
        snapshot.PackId,
        CapturedAt = snapshot.CapturedAt.ToString("o"),
        snapshot.PassCount,
        snapshot.FailCount,
        snapshot.ErrorCount,
        snapshot.NotApplicableCount,
        snapshot.NotReviewedCount,
        snapshot.TotalCount,
        snapshot.CompliancePercent,
        snapshot.Tool
      },
      cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<ComplianceSnapshot>> GetSnapshotsAsync(string bundleRoot, int limit, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));

    const string sql = @"
SELECT snapshot_id SnapshotId, bundle_root BundleRoot, run_id RunId, pack_id PackId,
  captured_at CapturedAt, pass_count PassCount, fail_count FailCount, error_count ErrorCount,
  not_applicable_count NotApplicableCount, not_reviewed_count NotReviewedCount,
  total_count TotalCount, compliance_percent CompliancePercent, tool Tool
FROM compliance_snapshots
WHERE bundle_root=@bundleRoot
ORDER BY captured_at DESC
LIMIT @limit;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<SnapshotRow>(
      new CommandDefinition(sql, new { bundleRoot, limit }, cancellationToken: ct))
      .ConfigureAwait(false);
    return rows.Select(MapSnapshot).ToList();
  }

  public async Task<ComplianceSnapshot?> GetLatestSnapshotAsync(string bundleRoot, CancellationToken ct)
  {
    var list = await GetSnapshotsAsync(bundleRoot, 1, ct).ConfigureAwait(false);
    return list.Count > 0 ? list[0] : null;
  }

  private static ComplianceSnapshot MapSnapshot(SnapshotRow r) => new()
  {
    SnapshotId = r.SnapshotId,
    BundleRoot = r.BundleRoot,
    RunId = r.RunId,
    PackId = r.PackId,
    CapturedAt = DateTimeOffset.Parse(r.CapturedAt, null, global::System.Globalization.DateTimeStyles.RoundtripKind),
    PassCount = r.PassCount,
    FailCount = r.FailCount,
    ErrorCount = r.ErrorCount,
    NotApplicableCount = r.NotApplicableCount,
    NotReviewedCount = r.NotReviewedCount,
    TotalCount = r.TotalCount,
    CompliancePercent = r.CompliancePercent,
    Tool = r.Tool
  };

  private sealed class SnapshotRow
  {
    public string SnapshotId { get; set; } = string.Empty;
    public string BundleRoot { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string? PackId { get; set; }
    public string CapturedAt { get; set; } = string.Empty;
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public int ErrorCount { get; set; }
    public int NotApplicableCount { get; set; }
    public int NotReviewedCount { get; set; }
    public int TotalCount { get; set; }
    public double CompliancePercent { get; set; }
    public string Tool { get; set; } = string.Empty;
  }
}

using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteDriftRepository : IDriftRepository
{
  private readonly string _cs;

  public SqliteDriftRepository(DbConnectionString connectionString)
  {
    ArgumentNullException.ThrowIfNull(connectionString);
    if (string.IsNullOrEmpty(connectionString.Value)) throw new ArgumentException("Value cannot be null or empty.", nameof(connectionString));
    _cs = connectionString.Value;
  }

  public async Task SaveAsync(DriftSnapshot snapshot, CancellationToken ct)
  {
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

    const string sql = @"
INSERT INTO drift_snapshots(snapshot_id, bundle_root, rule_id, previous_state, current_state, change_type, detected_at)
VALUES(@SnapshotId, @BundleRoot, @RuleId, @PreviousState, @CurrentState, @ChangeType, @DetectedAt)
ON CONFLICT(snapshot_id) DO UPDATE SET
  previous_state=excluded.previous_state,
  current_state=excluded.current_state,
  change_type=excluded.change_type,
  detected_at=excluded.detected_at;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        snapshot.SnapshotId,
        snapshot.BundleRoot,
        snapshot.RuleId,
        snapshot.PreviousState,
        snapshot.CurrentState,
        snapshot.ChangeType,
        DetectedAt = snapshot.DetectedAt.ToString("o")
      },
      cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task SaveBatchAsync(IReadOnlyList<DriftSnapshot> snapshots, CancellationToken ct)
  {
    if (snapshots is null) throw new ArgumentNullException(nameof(snapshots));
    if (snapshots.Count == 0)
      return;

    const string sql = @"
INSERT INTO drift_snapshots(snapshot_id, bundle_root, rule_id, previous_state, current_state, change_type, detected_at)
VALUES(@SnapshotId, @BundleRoot, @RuleId, @PreviousState, @CurrentState, @ChangeType, @DetectedAt)
ON CONFLICT(snapshot_id) DO UPDATE SET
  previous_state=excluded.previous_state,
  current_state=excluded.current_state,
  change_type=excluded.change_type,
  detected_at=excluded.detected_at;";

    var parameters = snapshots.Select(snapshot => new
    {
      snapshot.SnapshotId,
      snapshot.BundleRoot,
      snapshot.RuleId,
      snapshot.PreviousState,
      snapshot.CurrentState,
      snapshot.ChangeType,
      DetectedAt = snapshot.DetectedAt.ToString("o")
    }).ToList();

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    using var tx = conn.BeginTransaction();
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      parameters,
      transaction: tx,
      cancellationToken: ct)).ConfigureAwait(false);
    tx.Commit();
  }

  public async Task<IReadOnlyList<DriftSnapshot>> GetDriftHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (limit < 1)
      limit = 100;

    var sql = @"
SELECT snapshot_id SnapshotId, bundle_root BundleRoot, rule_id RuleId,
  previous_state PreviousState, current_state CurrentState,
  change_type ChangeType, detected_at DetectedAt
FROM drift_snapshots
WHERE bundle_root=@bundleRoot";

    if (!string.IsNullOrWhiteSpace(ruleId))
      sql += " AND rule_id=@ruleId";

    sql += " ORDER BY detected_at DESC LIMIT @limit;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<DriftSnapshotRow>(new CommandDefinition(
      sql,
      new { bundleRoot, ruleId, limit },
      cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapSnapshot).ToList();
  }

  public async Task<DriftSnapshot?> GetLatestSnapshotAsync(string bundleRoot, string ruleId, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("Value cannot be null or empty.", nameof(ruleId));

    var history = await GetDriftHistoryAsync(bundleRoot, ruleId, 1, ct).ConfigureAwait(false);
    return history.Count > 0 ? history[0] : null;
  }

  public async Task<IReadOnlyList<DriftSnapshot>> GetLatestByRuleAsync(string bundleRoot, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));

    const string sql = @"
WITH ranked AS (
  SELECT snapshot_id SnapshotId, bundle_root BundleRoot, rule_id RuleId,
    previous_state PreviousState, current_state CurrentState,
    change_type ChangeType, detected_at DetectedAt,
    ROW_NUMBER() OVER (PARTITION BY rule_id ORDER BY detected_at DESC, snapshot_id DESC) AS row_num
  FROM drift_snapshots
  WHERE bundle_root=@bundleRoot
)
SELECT SnapshotId, BundleRoot, RuleId, PreviousState, CurrentState, ChangeType, DetectedAt
FROM ranked
WHERE row_num=1
ORDER BY RuleId COLLATE NOCASE;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<DriftSnapshotRow>(
      new CommandDefinition(sql, new { bundleRoot }, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapSnapshot).ToList();
  }

  private static readonly global::System.Globalization.DateTimeStyles RoundtripStyle =
    global::System.Globalization.DateTimeStyles.RoundtripKind;

  private static DriftSnapshot MapSnapshot(DriftSnapshotRow row) => new()
  {
    SnapshotId = row.SnapshotId,
    BundleRoot = row.BundleRoot,
    RuleId = row.RuleId,
    PreviousState = row.PreviousState,
    CurrentState = row.CurrentState,
    ChangeType = row.ChangeType,
    DetectedAt = DateTimeOffset.Parse(row.DetectedAt, null, RoundtripStyle)
  };

  private sealed class DriftSnapshotRow
  {
    public string SnapshotId { get; set; } = string.Empty;
    public string BundleRoot { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string? PreviousState { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string DetectedAt { get; set; } = string.Empty;
  }
}

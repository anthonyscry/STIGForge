using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteRollbackRepository : IRollbackRepository
{
  private readonly string _cs;
  private static readonly JsonSerializerOptions J = new();

  public SqliteRollbackRepository(DbConnectionString connectionString)
  {
    ArgumentNullException.ThrowIfNull(connectionString);
    if (string.IsNullOrWhiteSpace(connectionString.Value)) throw new ArgumentException("Value cannot be null or empty.", nameof(connectionString));
    _cs = connectionString.Value;
  }

  public async Task SaveAsync(RollbackSnapshot snapshot, CancellationToken ct)
  {
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

    const string sql = @"
INSERT INTO rollback_snapshots(snapshot_id, bundle_root, description, created_at,
  registry_keys_json, file_paths_json, service_states_json, gpo_settings_json, rollback_script_path)
VALUES(@SnapshotId, @BundleRoot, @Description, @CreatedAt,
  @RegistryKeysJson, @FilePathsJson, @ServiceStatesJson, @GpoSettingsJson, @RollbackScriptPath)
ON CONFLICT(snapshot_id) DO UPDATE SET
  bundle_root=excluded.bundle_root,
  description=excluded.description,
  created_at=excluded.created_at,
  registry_keys_json=excluded.registry_keys_json,
  file_paths_json=excluded.file_paths_json,
  service_states_json=excluded.service_states_json,
  gpo_settings_json=excluded.gpo_settings_json,
  rollback_script_path=excluded.rollback_script_path;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        snapshot.SnapshotId,
        snapshot.BundleRoot,
        snapshot.Description,
        CreatedAt = snapshot.CreatedAt.ToString("o"),
        RegistryKeysJson = JsonSerializer.Serialize(snapshot.RegistryKeys, J),
        FilePathsJson = JsonSerializer.Serialize(snapshot.FilePaths, J),
        ServiceStatesJson = JsonSerializer.Serialize(snapshot.ServiceStates, J),
        GpoSettingsJson = JsonSerializer.Serialize(snapshot.GpoSettings, J),
        snapshot.RollbackScriptPath
      },
      cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<RollbackSnapshot?> GetAsync(string snapshotId, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(snapshotId)) throw new ArgumentException("Value cannot be null or empty.", nameof(snapshotId));

    const string sql = @"
SELECT snapshot_id SnapshotId, bundle_root BundleRoot, description Description, created_at CreatedAt,
  registry_keys_json RegistryKeysJson, file_paths_json FilePathsJson,
  service_states_json ServiceStatesJson, gpo_settings_json GpoSettingsJson,
  rollback_script_path RollbackScriptPath
FROM rollback_snapshots
WHERE snapshot_id=@snapshotId;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var row = await conn.QuerySingleOrDefaultAsync<RollbackSnapshotRow>(
      new CommandDefinition(sql, new { snapshotId }, cancellationToken: ct)).ConfigureAwait(false);
    return row is null ? null : MapSnapshot(row);
  }

  public async Task<IReadOnlyList<RollbackSnapshot>> ListByBundleAsync(string bundleRoot, int limit, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot)) throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));
    if (limit < 1)
      limit = 100;

    const string sql = @"
SELECT snapshot_id SnapshotId, bundle_root BundleRoot, description Description, created_at CreatedAt,
  registry_keys_json RegistryKeysJson, file_paths_json FilePathsJson,
  service_states_json ServiceStatesJson, gpo_settings_json GpoSettingsJson,
  rollback_script_path RollbackScriptPath
FROM rollback_snapshots
WHERE bundle_root=@bundleRoot
ORDER BY created_at DESC
LIMIT @limit;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<RollbackSnapshotRow>(new CommandDefinition(
      sql,
      new { bundleRoot, limit },
      cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapSnapshot).ToList();
  }

  private static readonly global::System.Globalization.DateTimeStyles RoundtripStyle =
    global::System.Globalization.DateTimeStyles.RoundtripKind;

  private static RollbackSnapshot MapSnapshot(RollbackSnapshotRow row) => new()
  {
    SnapshotId = row.SnapshotId,
    BundleRoot = row.BundleRoot,
    Description = row.Description,
    CreatedAt = DateTimeOffset.Parse(row.CreatedAt, null, RoundtripStyle),
    RegistryKeys = JsonSerializer.Deserialize<List<RollbackRegistryKeyState>>(row.RegistryKeysJson ?? "[]", J)
      ?? new List<RollbackRegistryKeyState>(),
    FilePaths = JsonSerializer.Deserialize<List<RollbackFilePathState>>(row.FilePathsJson ?? "[]", J)
      ?? new List<RollbackFilePathState>(),
    ServiceStates = JsonSerializer.Deserialize<List<RollbackServiceState>>(row.ServiceStatesJson ?? "[]", J)
      ?? new List<RollbackServiceState>(),
    GpoSettings = JsonSerializer.Deserialize<List<RollbackGpoSettingState>>(row.GpoSettingsJson ?? "[]", J)
      ?? new List<RollbackGpoSettingState>(),
    RollbackScriptPath = row.RollbackScriptPath ?? string.Empty
  };

  private sealed class RollbackSnapshotRow
  {
    public string SnapshotId { get; set; } = string.Empty;
    public string BundleRoot { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? RegistryKeysJson { get; set; }
    public string? FilePathsJson { get; set; }
    public string? ServiceStatesJson { get; set; }
    public string? GpoSettingsJson { get; set; }
    public string? RollbackScriptPath { get; set; }
  }
}

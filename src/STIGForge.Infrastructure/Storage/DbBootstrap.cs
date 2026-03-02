using Dapper;
using Microsoft.Data.Sqlite;

namespace STIGForge.Infrastructure.Storage;

/// <summary>
/// Dapper type handler for DateTimeOffset — SQLite stores as TEXT, Dapper
/// cannot natively cast TEXT → DateTimeOffset without this handler.
/// </summary>
internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
  public override void SetValue(global::System.Data.IDbDataParameter parameter, DateTimeOffset value)
    => parameter.Value = value.ToString("o");

  public override DateTimeOffset Parse(object value)
    => DateTimeOffset.Parse((string)value, null, global::System.Globalization.DateTimeStyles.RoundtripKind);
}

internal sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
{
  public override void SetValue(global::System.Data.IDbDataParameter parameter, DateTimeOffset? value)
    => parameter.Value = value?.ToString("o");

  public override DateTimeOffset? Parse(object value)
    => value is string s && !string.IsNullOrWhiteSpace(s)
      ? DateTimeOffset.Parse(s, null, global::System.Globalization.DateTimeStyles.RoundtripKind)
      : null;
}

public static class DbBootstrap
{
  private static bool _handlersRegistered;

  public static void EnsureCreated(string connectionString)
  {
    if (!_handlersRegistered)
    {
      SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
      SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
      _handlersRegistered = true;
    }

    using var conn = new SqliteConnection(connectionString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS mission_runs (
  run_id TEXT PRIMARY KEY,
  label TEXT NOT NULL,
  bundle_root TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL,
  finished_at TEXT NULL,
  input_fingerprint TEXT NULL,
  detail TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_mission_runs_created_at ON mission_runs(created_at DESC);

CREATE TABLE IF NOT EXISTS mission_timeline (
  event_id TEXT PRIMARY KEY,
  run_id TEXT NOT NULL REFERENCES mission_runs(run_id),
  seq INTEGER NOT NULL,
  phase TEXT NOT NULL,
  step_name TEXT NOT NULL,
  status TEXT NOT NULL,
  occurred_at TEXT NOT NULL,
  message TEXT NULL,
  evidence_path TEXT NULL,
  evidence_sha256 TEXT NULL,
  UNIQUE(run_id, seq)
);
CREATE INDEX IF NOT EXISTS ix_mission_timeline_run_id ON mission_timeline(run_id, seq);

CREATE TABLE IF NOT EXISTS content_packs (
  pack_id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  imported_at TEXT NOT NULL,
  release_date TEXT NULL,
  source_label TEXT NOT NULL,
  hash_algorithm TEXT NOT NULL,
  manifest_sha256 TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS profiles (
  profile_id TEXT PRIMARY KEY,
  json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS overlays (
  overlay_id TEXT PRIMARY KEY,
  json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS controls (
  pack_id TEXT NOT NULL,
  control_id TEXT NOT NULL,
  json TEXT NOT NULL,
  PRIMARY KEY (pack_id, control_id)
);

CREATE TABLE IF NOT EXISTS audit_trail (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  timestamp TEXT NOT NULL,
  user TEXT NOT NULL,
  machine TEXT NOT NULL,
  action TEXT NOT NULL,
  target TEXT NOT NULL,
  result TEXT NOT NULL,
  detail TEXT NOT NULL,
  previous_hash TEXT NOT NULL,
  entry_hash TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS compliance_snapshots (
  snapshot_id TEXT PRIMARY KEY,
  bundle_root TEXT NOT NULL,
  run_id TEXT NULL,
  pack_id TEXT NULL,
  captured_at TEXT NOT NULL,
  pass_count INTEGER NOT NULL,
  fail_count INTEGER NOT NULL,
  error_count INTEGER NOT NULL,
  not_applicable_count INTEGER NOT NULL,
  not_reviewed_count INTEGER NOT NULL,
  total_count INTEGER NOT NULL,
  compliance_percent REAL NOT NULL,
  tool TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_compliance_snapshots_bundle ON compliance_snapshots(bundle_root, captured_at DESC);

CREATE TABLE IF NOT EXISTS control_exceptions (
  exception_id TEXT PRIMARY KEY,
  bundle_root TEXT NOT NULL,
  rule_id TEXT NOT NULL,
  vuln_id TEXT NULL,
  exception_type TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'Active',
  risk_level TEXT NOT NULL,
  approved_by TEXT NOT NULL,
  justification TEXT NULL,
  justification_doc TEXT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NULL,
  revoked_at TEXT NULL,
  revoked_by TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_control_exceptions_bundle ON control_exceptions(bundle_root, rule_id);

CREATE TABLE IF NOT EXISTS release_checks (
  check_id TEXT PRIMARY KEY,
  checked_at TEXT NOT NULL,
  baseline_pack_id TEXT NOT NULL,
  target_pack_id TEXT NULL,
  status TEXT NOT NULL,
  summary_json TEXT NULL,
  release_notes_path TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_release_checks_baseline ON release_checks(baseline_pack_id, checked_at DESC);
";
    cmd.ExecuteNonQuery();

    // Migrations: Add new columns to content_packs if they don't exist
    try
    {
      using var alterCmd = conn.CreateCommand();
      alterCmd.CommandText = @"
ALTER TABLE content_packs ADD COLUMN benchmark_ids_json TEXT NOT NULL DEFAULT '[]';
";
      alterCmd.ExecuteNonQuery();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
    {
      // Column already exists, ignore
    }

    try
    {
      using var alterCmd = conn.CreateCommand();
      alterCmd.CommandText = @"
ALTER TABLE content_packs ADD COLUMN applicability_tags_json TEXT NOT NULL DEFAULT '[]';
";
      alterCmd.ExecuteNonQuery();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
    {
      // Column already exists, ignore
    }

    try
    {
      using var alterCmd = conn.CreateCommand();
      alterCmd.CommandText = @"
ALTER TABLE content_packs ADD COLUMN version TEXT NOT NULL DEFAULT '';
";
      alterCmd.ExecuteNonQuery();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
    {
      // Column already exists, ignore
    }

    try
    {
      using var alterCmd = conn.CreateCommand();
      alterCmd.CommandText = @"
ALTER TABLE content_packs ADD COLUMN release TEXT NOT NULL DEFAULT '';
";
      alterCmd.ExecuteNonQuery();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column name"))
    {
      // Column already exists, ignore
    }
  }
}

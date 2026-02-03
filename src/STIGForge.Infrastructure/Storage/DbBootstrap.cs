using Microsoft.Data.Sqlite;

namespace STIGForge.Infrastructure.Storage;

public static class DbBootstrap
{
  public static void EnsureCreated(string connectionString)
  {
    using var conn = new SqliteConnection(connectionString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS content_packs (
  pack_id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  imported_at TEXT NOT NULL,
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
";
    cmd.ExecuteNonQuery();
  }
}

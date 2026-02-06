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
";
    cmd.ExecuteNonQuery();
  }
}

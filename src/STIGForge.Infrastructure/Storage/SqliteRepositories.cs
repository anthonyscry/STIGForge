using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteContentPackRepository : IContentPackRepository
{
  private readonly string _cs;
  public SqliteContentPackRepository(string connectionString) => _cs = connectionString;

  public async Task SaveAsync(ContentPack pack, CancellationToken ct)
  {
    const string sql = @"INSERT INTO content_packs(pack_id,name,imported_at,release_date,source_label,hash_algorithm,manifest_sha256)
VALUES(@PackId,@Name,@ImportedAt,@ReleaseDate,@SourceLabel,@HashAlgorithm,@ManifestSha256)
ON CONFLICT(pack_id) DO UPDATE SET
name=excluded.name,
imported_at=excluded.imported_at,
release_date=excluded.release_date,
source_label=excluded.source_label,
hash_algorithm=excluded.hash_algorithm,
manifest_sha256=excluded.manifest_sha256;";
    using var conn = new SqliteConnection(_cs);
    await conn.ExecuteAsync(new CommandDefinition(sql, pack, cancellationToken: ct));
  }

  public async Task<ContentPack?> GetAsync(string packId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    return await conn.QuerySingleOrDefaultAsync<ContentPack>(new CommandDefinition(
      "SELECT pack_id PackId, name Name, imported_at ImportedAt, release_date ReleaseDate, source_label SourceLabel, hash_algorithm HashAlgorithm, manifest_sha256 ManifestSha256 FROM content_packs WHERE pack_id=@packId",
      new { packId }, cancellationToken: ct));
  }

  public async Task<IReadOnlyList<ContentPack>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var rows = await conn.QueryAsync<ContentPack>(new CommandDefinition(
      "SELECT pack_id PackId, name Name, imported_at ImportedAt, release_date ReleaseDate, source_label SourceLabel, hash_algorithm HashAlgorithm, manifest_sha256 ManifestSha256 FROM content_packs ORDER BY imported_at DESC",
      cancellationToken: ct));
    return rows.ToList();
  }
}

public sealed class SqliteJsonProfileRepository : IProfileRepository
{
  private readonly string _cs;
  private static readonly JsonSerializerOptions J = new();

  public SqliteJsonProfileRepository(string connectionString) => _cs = connectionString;

  public async Task SaveAsync(Profile profile, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(profile, J);
    const string sql = @"INSERT INTO profiles(profile_id,json) VALUES(@id,@json)
ON CONFLICT(profile_id) DO UPDATE SET json=excluded.json;";
    using var conn = new SqliteConnection(_cs);
    await conn.ExecuteAsync(new CommandDefinition(sql, new { id = profile.ProfileId, json }, cancellationToken: ct));
  }

  public async Task<Profile?> GetAsync(string profileId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
      "SELECT json FROM profiles WHERE profile_id=@profileId", new { profileId }, cancellationToken: ct));
    return json is null ? null : JsonSerializer.Deserialize<Profile>(json, J);
  }

  public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM profiles", cancellationToken: ct));
    return jsons.Select(j => JsonSerializer.Deserialize<Profile>(j, J)!).ToList();
  }
}

public sealed class SqliteJsonOverlayRepository : IOverlayRepository
{
  private readonly string _cs;
  private static readonly JsonSerializerOptions J = new();

  public SqliteJsonOverlayRepository(string connectionString) => _cs = connectionString;

  public async Task SaveAsync(Overlay overlay, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(overlay, J);
    const string sql = @"INSERT INTO overlays(overlay_id,json) VALUES(@id,@json)
ON CONFLICT(overlay_id) DO UPDATE SET json=excluded.json;";
    using var conn = new SqliteConnection(_cs);
    await conn.ExecuteAsync(new CommandDefinition(sql, new { id = overlay.OverlayId, json }, cancellationToken: ct));
  }

  public async Task<Overlay?> GetAsync(string overlayId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
      "SELECT json FROM overlays WHERE overlay_id=@overlayId", new { overlayId }, cancellationToken: ct));
    return json is null ? null : JsonSerializer.Deserialize<Overlay>(json, J);
  }

  public async Task<IReadOnlyList<Overlay>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM overlays", cancellationToken: ct));
    return jsons.Select(j => JsonSerializer.Deserialize<Overlay>(j, J)!).ToList();
  }
}

public sealed class SqliteJsonControlRepository : IControlRepository
{
  private readonly string _cs;
  private static readonly JsonSerializerOptions J = new();

  public SqliteJsonControlRepository(string connectionString) => _cs = connectionString;

  public async Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct)
  {
    const string sql = @"INSERT INTO controls(pack_id,control_id,json) VALUES(@packId,@controlId,@json)
ON CONFLICT(pack_id,control_id) DO UPDATE SET json=excluded.json;";
    using var conn = new SqliteConnection(_cs);
    conn.Open();
    using var tx = conn.BeginTransaction();
    foreach (var c in controls)
    {
      var json = JsonSerializer.Serialize(c, J);
      await conn.ExecuteAsync(new CommandDefinition(sql,
        new { packId, controlId = c.ControlId, json },
        transaction: tx, cancellationToken: ct));
    }
    tx.Commit();
  }

  public async Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM controls WHERE pack_id=@packId", new { packId }, cancellationToken: ct));
    return jsons.Select(j => JsonSerializer.Deserialize<ControlRecord>(j, J)!).ToList();
  }

  public async Task<bool> VerifySchemaAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct);
    
    // Verify controls table exists with required columns
    var tableInfo = await conn.QueryAsync<(string name, string type)>(new CommandDefinition(
      "PRAGMA table_info(controls)", cancellationToken: ct));
    
    var columns = tableInfo.Select(c => c.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    
    // Required columns: pack_id, control_id, json
    var required = new[] { "pack_id", "control_id", "json" };
    return required.All(col => columns.Contains(col));
  }
}

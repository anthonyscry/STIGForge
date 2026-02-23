using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteContentPackRepository : IContentPackRepository
{
  private readonly string _cs;
  private static readonly JsonSerializerOptions J = new();
  public SqliteContentPackRepository(string connectionString) => _cs = connectionString;

  public async Task SaveAsync(ContentPack pack, CancellationToken ct)
  {
    const string sql = @"INSERT INTO content_packs(pack_id,name,imported_at,release_date,source_label,hash_algorithm,manifest_sha256,benchmark_ids_json,applicability_tags_json,version,release)
VALUES(@PackId,@Name,@ImportedAt,@ReleaseDate,@SourceLabel,@HashAlgorithm,@ManifestSha256,@BenchmarkIdsJson,@ApplicabilityTagsJson,@Version,@Release)
ON CONFLICT(pack_id) DO UPDATE SET
name=excluded.name,
imported_at=excluded.imported_at,
release_date=excluded.release_date,
source_label=excluded.source_label,
hash_algorithm=excluded.hash_algorithm,
manifest_sha256=excluded.manifest_sha256,
benchmark_ids_json=excluded.benchmark_ids_json,
applicability_tags_json=excluded.applicability_tags_json,
version=excluded.version,
release=excluded.release;";
    using var conn = new SqliteConnection(_cs);
    var parameters = new
    {
      pack.PackId,
      pack.Name,
      pack.ImportedAt,
      pack.ReleaseDate,
      pack.SourceLabel,
      pack.HashAlgorithm,
      pack.ManifestSha256,
      BenchmarkIdsJson = JsonSerializer.Serialize(pack.BenchmarkIds, J),
      ApplicabilityTagsJson = JsonSerializer.Serialize(pack.ApplicabilityTags, J),
      pack.Version,
      pack.Release
    };
    await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<ContentPack?> GetAsync(string packId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
      "SELECT pack_id PackId, name Name, imported_at ImportedAt, release_date ReleaseDate, source_label SourceLabel, hash_algorithm HashAlgorithm, manifest_sha256 ManifestSha256, benchmark_ids_json BenchmarkIdsJson, applicability_tags_json ApplicabilityTagsJson, version Version, release Release FROM content_packs WHERE pack_id=@packId",
      new { packId }, cancellationToken: ct)).ConfigureAwait(false);

    if (row is null) return null;

    return new ContentPack
    {
      PackId = row.PackId,
      Name = row.Name,
      ImportedAt = row.ImportedAt is DateTimeOffset dto ? dto : DateTimeOffset.Parse(row.ImportedAt.ToString()!),
      ReleaseDate = row.ReleaseDate is DateTimeOffset rd ? rd : row.ReleaseDate != null ? DateTimeOffset.Parse(row.ReleaseDate.ToString()!) : null,
      SourceLabel = row.SourceLabel,
      HashAlgorithm = row.HashAlgorithm,
      ManifestSha256 = row.ManifestSha256,
      BenchmarkIds = JsonSerializer.Deserialize<List<string>>(row.BenchmarkIdsJson ?? "[]", J) ?? new List<string>(),
      ApplicabilityTags = JsonSerializer.Deserialize<List<string>>(row.ApplicabilityTagsJson ?? "[]", J) ?? new List<string>(),
      Version = row.Version ?? string.Empty,
      Release = row.Release ?? string.Empty
    };
  }

  public async Task<IReadOnlyList<ContentPack>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var rows = await conn.QueryAsync(new CommandDefinition(
      "SELECT pack_id PackId, name Name, imported_at ImportedAt, release_date ReleaseDate, source_label SourceLabel, hash_algorithm HashAlgorithm, manifest_sha256 ManifestSha256, benchmark_ids_json BenchmarkIdsJson, applicability_tags_json ApplicabilityTagsJson, version Version, release Release FROM content_packs ORDER BY imported_at DESC",
      cancellationToken: ct)).ConfigureAwait(false);

    return rows.Select(row => new ContentPack
    {
      PackId = row.PackId,
      Name = row.Name,
      ImportedAt = row.ImportedAt is DateTimeOffset dto ? dto : DateTimeOffset.Parse(row.ImportedAt.ToString()!),
      ReleaseDate = row.ReleaseDate is DateTimeOffset rd ? rd : row.ReleaseDate != null ? DateTimeOffset.Parse(row.ReleaseDate.ToString()!) : null,
      SourceLabel = row.SourceLabel,
      HashAlgorithm = row.HashAlgorithm,
      ManifestSha256 = row.ManifestSha256,
      BenchmarkIds = JsonSerializer.Deserialize<List<string>>(row.BenchmarkIdsJson ?? "[]", J) ?? new List<string>(),
      ApplicabilityTags = JsonSerializer.Deserialize<List<string>>(row.ApplicabilityTagsJson ?? "[]", J) ?? new List<string>(),
      Version = row.Version ?? string.Empty,
      Release = row.Release ?? string.Empty
    }).ToList();
  }

  public async Task DeleteAsync(string packId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    using var tx = conn.BeginTransaction();
    await conn.ExecuteAsync(new CommandDefinition("DELETE FROM controls WHERE pack_id=@packId", new { packId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition("DELETE FROM content_packs WHERE pack_id=@packId", new { packId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    tx.Commit();
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
    await conn.ExecuteAsync(new CommandDefinition(sql, new { id = profile.ProfileId, json }, cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<Profile?> GetAsync(string profileId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
      "SELECT json FROM profiles WHERE profile_id=@profileId", new { profileId }, cancellationToken: ct)).ConfigureAwait(false);
    return json is null ? null : JsonSerializer.Deserialize<Profile>(json, J);
  }

  public async Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM profiles", cancellationToken: ct)).ConfigureAwait(false);
    return jsons.Select(j => JsonSerializer.Deserialize<Profile>(j, J)!).ToList();
  }

  public async Task DeleteAsync(string profileId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    await conn.ExecuteAsync(new CommandDefinition("DELETE FROM profiles WHERE profile_id=@profileId", new { profileId }, cancellationToken: ct)).ConfigureAwait(false);
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
    await conn.ExecuteAsync(new CommandDefinition(sql, new { id = overlay.OverlayId, json }, cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<Overlay?> GetAsync(string overlayId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
      "SELECT json FROM overlays WHERE overlay_id=@overlayId", new { overlayId }, cancellationToken: ct)).ConfigureAwait(false);
    return json is null ? null : JsonSerializer.Deserialize<Overlay>(json, J);
  }

  public async Task<IReadOnlyList<Overlay>> ListAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM overlays", cancellationToken: ct)).ConfigureAwait(false);
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
        transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }
    tx.Commit();
  }

  public async Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    var jsons = await conn.QueryAsync<string>(new CommandDefinition(
      "SELECT json FROM controls WHERE pack_id=@packId", new { packId }, cancellationToken: ct)).ConfigureAwait(false);
    return jsons.Select(j => JsonSerializer.Deserialize<ControlRecord>(j, J)!).ToList();
  }

  public async Task<bool> VerifySchemaAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    
    // Verify controls table exists with required columns
    var tableInfo = await conn.QueryAsync<(string name, string type)>(new CommandDefinition(
      "PRAGMA table_info(controls)", cancellationToken: ct)).ConfigureAwait(false);
    
    var columns = tableInfo.Select(c => c.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    
    // Required columns: pack_id, control_id, json
    var required = new[] { "pack_id", "control_id", "json" };
    return required.All(col => columns.Contains(col));
  }
}

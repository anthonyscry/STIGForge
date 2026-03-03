using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

public sealed class SqliteReleaseCheckRepository : IReleaseCheckRepository
{
  private readonly string _cs;

  public SqliteReleaseCheckRepository(DbConnectionString connectionString)
  {
    ArgumentNullException.ThrowIfNull(connectionString);
    if (string.IsNullOrEmpty(connectionString.Value)) throw new ArgumentException("Value cannot be null or empty.", nameof(connectionString));
    _cs = connectionString.Value;
  }

  public async Task SaveAsync(ReleaseCheck check, CancellationToken ct)
  {
    if (check is null) throw new ArgumentNullException(nameof(check));

    const string sql = @"
INSERT INTO release_checks(check_id, checked_at, baseline_pack_id, target_pack_id, status, summary_json, release_notes_path)
VALUES(@CheckId, @CheckedAt, @BaselinePackId, @TargetPackId, @Status, @SummaryJson, @ReleaseNotesPath)
ON CONFLICT(check_id) DO UPDATE SET
  checked_at=excluded.checked_at,
  baseline_pack_id=excluded.baseline_pack_id,
  target_pack_id=excluded.target_pack_id,
  status=excluded.status,
  summary_json=excluded.summary_json,
  release_notes_path=excluded.release_notes_path;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        check.CheckId,
        CheckedAt = check.CheckedAt.ToString("o"),
        check.BaselinePackId,
        check.TargetPackId,
        check.Status,
        check.SummaryJson,
        check.ReleaseNotesPath
      },
      cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task<ReleaseCheck?> GetAsync(string checkId, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(checkId)) throw new ArgumentException("Value cannot be null or empty.", nameof(checkId));

    const string sql = @"
SELECT check_id CheckId, checked_at CheckedAt, baseline_pack_id BaselinePackId,
  target_pack_id TargetPackId, status Status, summary_json SummaryJson,
  release_notes_path ReleaseNotesPath
FROM release_checks
WHERE check_id=@checkId;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var row = await conn.QuerySingleOrDefaultAsync<ReleaseCheckRow>(
      new CommandDefinition(sql, new { checkId }, cancellationToken: ct)).ConfigureAwait(false);
    return row is null ? null : MapReleaseCheck(row);
  }

  public async Task<IReadOnlyList<ReleaseCheck>> ListByBaselineAsync(string baselinePackId, int limit, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(baselinePackId)) throw new ArgumentException("Value cannot be null or empty.", nameof(baselinePackId));
    if (limit < 1)
      limit = 10;

    const string sql = @"
SELECT check_id CheckId, checked_at CheckedAt, baseline_pack_id BaselinePackId,
  target_pack_id TargetPackId, status Status, summary_json SummaryJson,
  release_notes_path ReleaseNotesPath
FROM release_checks
WHERE baseline_pack_id=@baselinePackId
ORDER BY checked_at DESC
LIMIT @limit;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.QueryAsync<ReleaseCheckRow>(
      new CommandDefinition(sql, new { baselinePackId, limit }, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapReleaseCheck).ToList();
  }

  public async Task<ReleaseCheck?> GetLatestAsync(string baselinePackId, CancellationToken ct)
  {
    var list = await ListByBaselineAsync(baselinePackId, 1, ct).ConfigureAwait(false);
    return list.Count > 0 ? list[0] : null;
  }

  private static readonly global::System.Globalization.DateTimeStyles RoundtripStyle =
    global::System.Globalization.DateTimeStyles.RoundtripKind;

  private static ReleaseCheck MapReleaseCheck(ReleaseCheckRow row) => new()
  {
    CheckId = row.CheckId,
    CheckedAt = DateTimeOffset.Parse(row.CheckedAt, null, RoundtripStyle),
    BaselinePackId = row.BaselinePackId,
    TargetPackId = row.TargetPackId,
    Status = row.Status,
    SummaryJson = row.SummaryJson,
    ReleaseNotesPath = row.ReleaseNotesPath
  };

  private sealed class ReleaseCheckRow
  {
    public string CheckId { get; set; } = string.Empty;
    public string CheckedAt { get; set; } = string.Empty;
    public string BaselinePackId { get; set; } = string.Empty;
    public string? TargetPackId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SummaryJson { get; set; }
    public string? ReleaseNotesPath { get; set; }
  }
}

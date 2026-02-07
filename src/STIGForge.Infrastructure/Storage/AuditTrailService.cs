using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Storage;

/// <summary>
/// SQLite-backed audit trail with SHA-256 chained hashing for tamper evidence.
/// Each new entry's hash includes the previous entry's hash, forming an immutable chain.
/// </summary>
public sealed class AuditTrailService : IAuditTrailService
{
  private readonly string _connectionString;
  private readonly IClock _clock;

  public AuditTrailService(string connectionString, IClock clock)
  {
    _connectionString = connectionString;
    _clock = clock;
  }

  public async Task RecordAsync(AuditEntry entry, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(entry.User))
      entry.User = Environment.UserName;
    if (string.IsNullOrWhiteSpace(entry.Machine))
      entry.Machine = Environment.MachineName;
    if (entry.Timestamp == default)
      entry.Timestamp = _clock.Now;

    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    // Get previous hash for chaining
    var previousHash = await conn.QueryFirstOrDefaultAsync<string>(
      "SELECT entry_hash FROM audit_trail ORDER BY id DESC LIMIT 1"
    ).ConfigureAwait(false) ?? "genesis";

    entry.PreviousHash = previousHash;
    entry.EntryHash = ComputeEntryHash(entry);

    await conn.ExecuteAsync(
      @"INSERT INTO audit_trail (timestamp, user, machine, action, target, result, detail, previous_hash, entry_hash)
        VALUES (@Timestamp, @User, @Machine, @Action, @Target, @Result, @Detail, @PreviousHash, @EntryHash)",
      entry
    ).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var sb = new StringBuilder("SELECT id as Id, timestamp as Timestamp, user as User, machine as Machine, action as Action, target as Target, result as Result, detail as Detail, previous_hash as PreviousHash, entry_hash as EntryHash FROM audit_trail WHERE 1=1");
    var parameters = new DynamicParameters();

    if (!string.IsNullOrWhiteSpace(query.Action))
    {
      sb.Append(" AND action = @Action");
      parameters.Add("Action", query.Action);
    }
    if (!string.IsNullOrWhiteSpace(query.Target))
    {
      sb.Append(" AND target LIKE @Target");
      parameters.Add("Target", "%" + query.Target + "%");
    }
    if (query.From.HasValue)
    {
      sb.Append(" AND timestamp >= @From");
      parameters.Add("From", query.From.Value.ToString("o"));
    }
    if (query.To.HasValue)
    {
      sb.Append(" AND timestamp <= @To");
      parameters.Add("To", query.To.Value.ToString("o"));
    }

    sb.Append(" ORDER BY id DESC LIMIT @Limit");
    parameters.Add("Limit", query.Limit > 0 ? query.Limit : 100);

    var results = await conn.QueryAsync<AuditEntry>(sb.ToString(), parameters).ConfigureAwait(false);
    return results.ToList();
  }

  public async Task<bool> VerifyIntegrityAsync(CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var entries = (await conn.QueryAsync<AuditEntry>(
      "SELECT id as Id, timestamp as Timestamp, user as User, machine as Machine, action as Action, target as Target, result as Result, detail as Detail, previous_hash as PreviousHash, entry_hash as EntryHash FROM audit_trail ORDER BY id ASC"
    ).ConfigureAwait(false)).ToList();

    if (entries.Count == 0) return true;

    string expectedPrevious = "genesis";
    foreach (var entry in entries)
    {
      if (entry.PreviousHash != expectedPrevious)
        return false;

      var computed = ComputeEntryHash(entry);
      if (entry.EntryHash != computed)
        return false;

      expectedPrevious = entry.EntryHash;
    }

    return true;
  }

  public static string ComputeEntryHash(AuditEntry entry)
  {
    var payload = string.Join("|",
      entry.Timestamp.ToString("o"),
      entry.User,
      entry.Machine,
      entry.Action,
      entry.Target,
      entry.Result,
      entry.Detail,
      entry.PreviousHash);

    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
  }
}

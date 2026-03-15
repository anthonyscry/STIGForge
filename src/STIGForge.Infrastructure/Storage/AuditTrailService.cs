using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using IsolationLevel = System.Data.IsolationLevel;

namespace STIGForge.Infrastructure.Storage;

/// <summary>
/// SQLite-backed audit trail with SHA-256 chained hashing for tamper evidence.
/// Each new entry's hash includes the previous entry's hash, forming an immutable chain.
/// </summary>
public sealed class AuditTrailService : IAuditTrailService
{
  private readonly string _connectionString;
  private readonly IClock _clock;

  public AuditTrailService(DbConnectionString connectionString, IClock clock)
  {
    ArgumentNullException.ThrowIfNull(connectionString);
    _connectionString = connectionString.Value;
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

    // BEGIN IMMEDIATE acquires a write lock upfront, serialising concurrent
    // RecordAsync calls so the read-then-insert for chain hashing is atomic.
    using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

    // Get previous hash for chaining
    var previousHash = await conn.QueryFirstOrDefaultAsync<string>(
      new CommandDefinition(
        "SELECT entry_hash FROM audit_trail ORDER BY id DESC LIMIT 1",
        transaction: tx,
        cancellationToken: ct)
    ).ConfigureAwait(false) ?? "genesis";

    entry.PreviousHash = previousHash;
    entry.EntryHash = ComputeEntryHash(entry);

    await conn.ExecuteAsync(
      new CommandDefinition(
        @"INSERT INTO audit_trail (timestamp, user, machine, action, target, result, detail, previous_hash, entry_hash)
          VALUES (@Timestamp, @User, @Machine, @Action, @Target, @Result, @Detail, @PreviousHash, @EntryHash)",
        entry,
        transaction: tx,
        cancellationToken: ct)
    ).ConfigureAwait(false);

    tx.Commit();
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

    const int batchSize = 1000;
    long lastId = 0;
    string expectedPrevious = "genesis";

    while (true)
    {
      ct.ThrowIfCancellationRequested();
      var entries = (await conn.QueryAsync<AuditEntry>(new CommandDefinition(
        "SELECT id as Id, timestamp as Timestamp, user as User, machine as Machine, action as Action, target as Target, result as Result, detail as Detail, previous_hash as PreviousHash, entry_hash as EntryHash FROM audit_trail WHERE id > @lastId ORDER BY id ASC LIMIT @batchSize",
        new { lastId, batchSize }, cancellationToken: ct)
      ).ConfigureAwait(false)).ToList();

      if (entries.Count == 0) break;

      foreach (var entry in entries)
      {
        if (entry.PreviousHash != expectedPrevious)
          return false;

        var computed = ComputeEntryHash(entry);
        if (entry.EntryHash != computed)
          return false;

        expectedPrevious = entry.EntryHash;
        lastId = entry.Id;
      }
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

    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(bytes).ToLowerInvariant();
  }

  /// <summary>
  /// Writes the latest chain hash to the Windows Application Event Log as an
  /// external trust anchor. This prevents full-chain recomputation attacks: an
  /// attacker who rewrites the SQLite DB cannot alter the Event Log entry without
  /// separate privileged access.
  /// </summary>
  public async Task WriteChainAnchorToEventLogAsync(string missionLabel, CancellationToken ct)
  {
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    var latestHash = await conn.QueryFirstOrDefaultAsync<string>(
      new CommandDefinition(
        "SELECT entry_hash FROM audit_trail ORDER BY id DESC LIMIT 1",
        cancellationToken: ct)
    ).ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(latestHash))
      return;

    var message = $"STIGForge audit chain anchor | Mission: {missionLabel} | LatestHash: {latestHash} | Timestamp: {_clock.Now:o}";

    if (OperatingSystem.IsWindows())
    {
      const string sourceName = "STIGForge";
      const string logName = "Application";
      if (!EventLog.SourceExists(sourceName))
        EventLog.CreateEventSource(sourceName, logName);
      EventLog.WriteEntry(sourceName, message, EventLogEntryType.Information, 1701);
    }
    else
    {
      var dbPath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(_connectionString).DataSource;
      var anchorPath = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "audit_anchor.log");
      await File.AppendAllTextAsync(anchorPath, message + Environment.NewLine, ct).ConfigureAwait(false);
    }
  }
}

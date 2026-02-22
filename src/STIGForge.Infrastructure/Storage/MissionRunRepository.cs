using Dapper;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Infrastructure.Storage;

/// <summary>
/// SQLite-backed append-only mission run ledger using Dapper.
/// Timeline events are immutable once written; runs may only have their status updated.
/// Ordering is always deterministic by seq ascending for timeline reads.
/// </summary>
public sealed class MissionRunRepository : IMissionRunRepository
{
  private readonly string _cs;

  public MissionRunRepository(string connectionString)
  {
    ArgumentException.ThrowIfNullOrEmpty(connectionString);
    _cs = connectionString;
  }

  // ---------- Run operations ----------

  public async Task CreateRunAsync(MissionRun run, CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(run);

    const string sql = @"
INSERT INTO mission_runs(run_id, label, bundle_root, status, created_at, finished_at, input_fingerprint, detail)
VALUES(@RunId, @Label, @BundleRoot, @Status, @CreatedAt, @FinishedAt, @InputFingerprint, @Detail);";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        run.RunId,
        run.Label,
        run.BundleRoot,
        Status = run.Status.ToString(),
        CreatedAt = run.CreatedAt.ToString("o"),
        FinishedAt = run.FinishedAt?.ToString("o"),
        run.InputFingerprint,
        run.Detail
      },
      cancellationToken: ct)).ConfigureAwait(false);
  }

  public async Task UpdateRunStatusAsync(
    string runId,
    MissionRunStatus status,
    DateTimeOffset? finishedAt,
    string? detail,
    CancellationToken ct)
  {
    ArgumentException.ThrowIfNullOrEmpty(runId);

    const string sql = @"
UPDATE mission_runs
SET status=@Status, finished_at=@FinishedAt, detail=@Detail
WHERE run_id=@RunId;";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    var rows = await conn.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        RunId = runId,
        Status = status.ToString(),
        FinishedAt = finishedAt?.ToString("o"),
        Detail = detail
      },
      cancellationToken: ct)).ConfigureAwait(false);

    if (rows == 0)
      throw new InvalidOperationException($"Mission run '{runId}' not found; cannot update status.");
  }

  public async Task<MissionRun?> GetLatestRunAsync(CancellationToken ct)
  {
    const string sql = @"
SELECT run_id RunId, label Label, bundle_root BundleRoot, status Status,
       created_at CreatedAt, finished_at FinishedAt, input_fingerprint InputFingerprint, detail Detail
FROM mission_runs
ORDER BY created_at DESC
LIMIT 1;";

    using var conn = new SqliteConnection(_cs);
    var row = await conn.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    return row is null ? null : MapRun(row);
  }

  public async Task<MissionRun?> GetRunAsync(string runId, CancellationToken ct)
  {
    ArgumentException.ThrowIfNullOrEmpty(runId);

    const string sql = @"
SELECT run_id RunId, label Label, bundle_root BundleRoot, status Status,
       created_at CreatedAt, finished_at FinishedAt, input_fingerprint InputFingerprint, detail Detail
FROM mission_runs
WHERE run_id=@runId;";

    using var conn = new SqliteConnection(_cs);
    var row = await conn.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(sql, new { runId }, cancellationToken: ct)).ConfigureAwait(false);
    return row is null ? null : MapRun(row);
  }

  public async Task<IReadOnlyList<MissionRun>> ListRunsAsync(CancellationToken ct)
  {
    const string sql = @"
SELECT run_id RunId, label Label, bundle_root BundleRoot, status Status,
       created_at CreatedAt, finished_at FinishedAt, input_fingerprint InputFingerprint, detail Detail
FROM mission_runs
ORDER BY created_at DESC;";

    using var conn = new SqliteConnection(_cs);
    var rows = await conn.QueryAsync<RunRow>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapRun).ToList();
  }

  // ---------- Timeline operations ----------

  public async Task AppendEventAsync(MissionTimelineEvent evt, CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(evt);

    const string sql = @"
INSERT INTO mission_timeline(event_id, run_id, seq, phase, step_name, status, occurred_at, message, evidence_path, evidence_sha256)
VALUES(@EventId, @RunId, @Seq, @Phase, @StepName, @Status, @OccurredAt, @Message, @EvidencePath, @EvidenceSha256);";

    using var conn = new SqliteConnection(_cs);
    await conn.OpenAsync(ct).ConfigureAwait(false);

    try
    {
      await conn.ExecuteAsync(new CommandDefinition(
        sql,
        new
        {
          evt.EventId,
          evt.RunId,
          evt.Seq,
          Phase = evt.Phase.ToString(),
          evt.StepName,
          Status = evt.Status.ToString(),
          OccurredAt = evt.OccurredAt.ToString("o"),
          evt.Message,
          evt.EvidencePath,
          evt.EvidenceSha256
        },
        cancellationToken: ct)).ConfigureAwait(false);
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT */)
    {
      throw new InvalidOperationException(
        $"Duplicate sequence index {evt.Seq} for run '{evt.RunId}'. Timeline events are append-only.", ex);
    }
  }

  public async Task<IReadOnlyList<MissionTimelineEvent>> GetTimelineAsync(string runId, CancellationToken ct)
  {
    ArgumentException.ThrowIfNullOrEmpty(runId);

    const string sql = @"
SELECT event_id EventId, run_id RunId, seq Seq, phase Phase, step_name StepName,
       status Status, occurred_at OccurredAt, message Message,
       evidence_path EvidencePath, evidence_sha256 EvidenceSha256
FROM mission_timeline
WHERE run_id=@runId
ORDER BY seq ASC;";

    using var conn = new SqliteConnection(_cs);
    var rows = await conn.QueryAsync<EventRow>(new CommandDefinition(sql, new { runId }, cancellationToken: ct)).ConfigureAwait(false);
    return rows.Select(MapEvent).ToList();
  }

  // ---------- Private mapping ----------

  private static readonly global::System.Globalization.DateTimeStyles RoundtripStyle =
    global::System.Globalization.DateTimeStyles.RoundtripKind;

  private static MissionRun MapRun(RunRow r) => new()
  {
    RunId = r.RunId,
    Label = r.Label,
    BundleRoot = r.BundleRoot,
    Status = Enum.Parse<MissionRunStatus>(r.Status),
    CreatedAt = DateTimeOffset.Parse(r.CreatedAt, null, RoundtripStyle),
    FinishedAt = r.FinishedAt is null ? null : DateTimeOffset.Parse(r.FinishedAt, null, RoundtripStyle),
    InputFingerprint = r.InputFingerprint,
    Detail = r.Detail
  };

  private static MissionTimelineEvent MapEvent(EventRow e) => new()
  {
    EventId = e.EventId,
    RunId = e.RunId,
    Seq = e.Seq,
    Phase = Enum.Parse<MissionPhase>(e.Phase),
    StepName = e.StepName,
    Status = Enum.Parse<MissionEventStatus>(e.Status),
    OccurredAt = DateTimeOffset.Parse(e.OccurredAt, null, RoundtripStyle),
    Message = e.Message,
    EvidencePath = e.EvidencePath,
    EvidenceSha256 = e.EvidenceSha256
  };

  // ---------- Dapper projection DTOs ----------

  private sealed class RunRow
  {
    public string RunId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string BundleRoot { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? FinishedAt { get; set; }
    public string? InputFingerprint { get; set; }
    public string? Detail { get; set; }
  }

  private sealed class EventRow
  {
    public string EventId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OccurredAt { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? EvidencePath { get; set; }
    public string? EvidenceSha256 { get; set; }
  }
}

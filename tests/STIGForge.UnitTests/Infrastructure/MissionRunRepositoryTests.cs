using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

/// <summary>
/// Integration-style unit tests for MissionRunRepository.
/// Uses an in-memory SQLite database (Data Source=:memory:) per test.
/// </summary>
public class MissionRunRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly IMissionRunRepository _repo;

  public MissionRunRepositoryTests()
  {
    // Each test gets its own in-memory SQLite database
    var dbFile = Path.Combine(Path.GetTempPath(), $"missionrun_test_{Guid.NewGuid():N}.db");
    _cs = $"Data Source={dbFile}";
    DbBootstrap.EnsureCreated(_cs);
    _repo = new MissionRunRepository(_cs);
  }

  public void Dispose()
  {
    // Clean up temp database file
    try
    {
      var path = _cs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase);
      if (File.Exists(path)) File.Delete(path);
    }
    catch { /* best effort */ }
  }

  // ---------- DbBootstrap schema verification ----------

  [Fact]
  public void EnsureCreated_CreatesSchemaWithoutError()
  {
    // The constructor already called EnsureCreated; calling again should be idempotent
    var act = () => DbBootstrap.EnsureCreated(_cs);
    act.Should().NotThrow();
  }

  // ---------- Run lifecycle ----------

  [Fact]
  public async Task CreateRun_ThenGetRun_ReturnsCorrectData()
  {
    var run = BuildRun("run-1");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    var result = await _repo.GetRunAsync("run-1", CancellationToken.None);

    result.Should().NotBeNull();
    result!.RunId.Should().Be("run-1");
    result.Label.Should().Be(run.Label);
    result.BundleRoot.Should().Be(run.BundleRoot);
    result.Status.Should().Be(MissionRunStatus.Pending);
    result.InputFingerprint.Should().Be(run.InputFingerprint);
  }

  [Fact]
  public async Task GetRun_NonExistentId_ReturnsNull()
  {
    var result = await _repo.GetRunAsync("does-not-exist", CancellationToken.None);
    result.Should().BeNull();
  }

  [Fact]
  public async Task UpdateRunStatus_ChangesStatusAndFinishedAt()
  {
    var run = BuildRun("run-upd");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    var finishedAt = DateTimeOffset.UtcNow.AddMinutes(5);
    await _repo.UpdateRunStatusAsync("run-upd", MissionRunStatus.Completed, finishedAt, "done", CancellationToken.None);

    var result = await _repo.GetRunAsync("run-upd", CancellationToken.None);
    result!.Status.Should().Be(MissionRunStatus.Completed);
    result.FinishedAt.Should().BeCloseTo(finishedAt, TimeSpan.FromSeconds(1));
    result.Detail.Should().Be("done");
  }

  [Fact]
  public async Task UpdateRunStatus_NonExistentRun_Throws()
  {
    var act = () => _repo.UpdateRunStatusAsync("ghost-run", MissionRunStatus.Failed, null, null, CancellationToken.None);
    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ghost-run*");
  }

  [Fact]
  public async Task GetLatestRun_WithMultipleRuns_ReturnsNewest()
  {
    var older = BuildRun("run-old", DateTimeOffset.UtcNow.AddHours(-2));
    var newer = BuildRun("run-new", DateTimeOffset.UtcNow);
    await _repo.CreateRunAsync(older, CancellationToken.None);
    await _repo.CreateRunAsync(newer, CancellationToken.None);

    var result = await _repo.GetLatestRunAsync(CancellationToken.None);
    result!.RunId.Should().Be("run-new");
  }

  [Fact]
  public async Task GetLatestRun_EmptyRepository_ReturnsNull()
  {
    var result = await _repo.GetLatestRunAsync(CancellationToken.None);
    result.Should().BeNull();
  }

  [Fact]
  public async Task ListRuns_ReturnsAllRunsOrderedByCreatedAtDesc()
  {
    await _repo.CreateRunAsync(BuildRun("a", DateTimeOffset.UtcNow.AddHours(-3)), CancellationToken.None);
    await _repo.CreateRunAsync(BuildRun("b", DateTimeOffset.UtcNow.AddHours(-1)), CancellationToken.None);
    await _repo.CreateRunAsync(BuildRun("c", DateTimeOffset.UtcNow.AddHours(-2)), CancellationToken.None);

    var runs = await _repo.ListRunsAsync(CancellationToken.None);

    runs.Should().HaveCount(3);
    runs.Select(r => r.RunId).Should().ContainInOrder("b", "c", "a");
  }

  // ---------- Timeline append and query ----------

  [Fact]
  public async Task AppendEvent_ThenGetTimeline_ReturnsDeterministicOrder()
  {
    var run = BuildRun("run-tl");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    await _repo.AppendEventAsync(BuildEvent("run-tl", 2, MissionPhase.Apply, "apply-finish"), CancellationToken.None);
    await _repo.AppendEventAsync(BuildEvent("run-tl", 1, MissionPhase.Build, "build-start"), CancellationToken.None);
    await _repo.AppendEventAsync(BuildEvent("run-tl", 3, MissionPhase.Verify, "verify-start"), CancellationToken.None);

    var timeline = await _repo.GetTimelineAsync("run-tl", CancellationToken.None);

    timeline.Should().HaveCount(3);
    // Must be ordered by seq ASC regardless of insertion order
    timeline[0].Seq.Should().Be(1);
    timeline[1].Seq.Should().Be(2);
    timeline[2].Seq.Should().Be(3);
  }

  [Fact]
  public async Task AppendEvent_DuplicateSeq_ThrowsInvalidOperationException()
  {
    var run = BuildRun("run-dup");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    await _repo.AppendEventAsync(BuildEvent("run-dup", 1, MissionPhase.Build, "step"), CancellationToken.None);

    var act = () => _repo.AppendEventAsync(BuildEvent("run-dup", 1, MissionPhase.Apply, "step2"), CancellationToken.None);
    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate sequence index 1*");
  }

  [Fact]
  public async Task AppendEvent_AllEventStatuses_RoundTrip()
  {
    var run = BuildRun("run-statuses");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    var statuses = Enum.GetValues<MissionEventStatus>();
    for (var i = 0; i < statuses.Length; i++)
    {
      var evt = BuildEvent("run-statuses", i + 1, MissionPhase.Build, $"step-{i}");
      evt.Status = statuses[i];
      await _repo.AppendEventAsync(evt, CancellationToken.None);
    }

    var timeline = await _repo.GetTimelineAsync("run-statuses", CancellationToken.None);
    timeline.Select(e => e.Status).Should().BeEquivalentTo(statuses);
  }

  [Fact]
  public async Task AppendEvent_WithEvidenceReference_RoundTrips()
  {
    var run = BuildRun("run-ev");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    var evt = BuildEvent("run-ev", 1, MissionPhase.Apply, "apply-step");
    evt.EvidencePath = @"Apply\apply_run.json";
    evt.EvidenceSha256 = "abc123";

    await _repo.AppendEventAsync(evt, CancellationToken.None);
    var timeline = await _repo.GetTimelineAsync("run-ev", CancellationToken.None);

    timeline[0].EvidencePath.Should().Be(@"Apply\apply_run.json");
    timeline[0].EvidenceSha256.Should().Be("abc123");
  }

  [Fact]
  public async Task GetTimeline_EmptyRun_ReturnsEmptyList()
  {
    var run = BuildRun("run-empty");
    await _repo.CreateRunAsync(run, CancellationToken.None);

    var timeline = await _repo.GetTimelineAsync("run-empty", CancellationToken.None);
    timeline.Should().BeEmpty();
  }

  [Fact]
  public async Task GetTimeline_SameInputAcrossRepeatedReads_IsIdentical()
  {
    var run = BuildRun("run-repeat");
    await _repo.CreateRunAsync(run, CancellationToken.None);
    for (var i = 1; i <= 5; i++)
      await _repo.AppendEventAsync(BuildEvent("run-repeat", i, MissionPhase.Build, $"step-{i}"), CancellationToken.None);

    var first = await _repo.GetTimelineAsync("run-repeat", CancellationToken.None);
    var second = await _repo.GetTimelineAsync("run-repeat", CancellationToken.None);

    first.Select(e => e.Seq).Should().ContainInOrder(second.Select(e => e.Seq));
  }

  // ---------- DI registration smoke test ----------

  [Fact]
  public void CliHostFactory_CanResolveMissionRunRepository()
  {
    // Use a temp root to avoid touching the real .stigforge directory
    var tempRoot = Path.Combine(Path.GetTempPath(), $"sf_test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempRoot);
    try
    {
      using var host = STIGForge.Cli.CliHostFactory.BuildHost(() =>
        new STIGForge.Infrastructure.Paths.PathBuilder(tempRoot, tempRoot));

      var repo = host.Services.GetService(typeof(IMissionRunRepository));
      repo.Should().NotBeNull("CliHostFactory must register IMissionRunRepository");
    }
    finally
    {
      try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
    }
  }

  // ---------- Helpers ----------

  private static MissionRun BuildRun(string id, DateTimeOffset? createdAt = null) => new()
  {
    RunId = id,
    Label = $"Test run {id}",
    BundleRoot = @"C:\Bundles\test",
    Status = MissionRunStatus.Pending,
    CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    InputFingerprint = "fp-" + id
  };

  private static MissionTimelineEvent BuildEvent(
    string runId, int seq, MissionPhase phase, string stepName) => new()
  {
    EventId = Guid.NewGuid().ToString(),
    RunId = runId,
    Seq = seq,
    Phase = phase,
    StepName = stepName,
    Status = MissionEventStatus.Started,
    OccurredAt = DateTimeOffset.UtcNow
  };
}

using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class DriftDetectionServiceTests
{
  [Fact]
  public async Task CheckAsync_FirstRun_EstablishesBaselineForAllRules()
  {
    var repo = new InMemoryDriftRepository();
    var now = new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero);
    var service = new DriftDetectionService(repo, clock: new TestClock(now));

    var result = await service.CheckAsync(
      "bundle-a",
      new Dictionary<string, string>
      {
        ["SV-1001"] = "Pass",
        ["SV-1002"] = "Fail"
      },
      autoRemediate: false,
      ct: CancellationToken.None);

    result.DriftEvents.Should().HaveCount(2);
    result.DriftEvents.Should().OnlyContain(e => e.ChangeType == DriftChangeTypes.BaselineEstablished);
    result.DriftEvents.Should().OnlyContain(e => e.DetectedAt == now);

    var history = await repo.GetDriftHistoryAsync("bundle-a", null, 10, CancellationToken.None);
    history.Should().HaveCount(2);
  }

  [Fact]
  public async Task CheckAsync_StateChangesAndMissingRules_AreRecorded()
  {
    var repo = new InMemoryDriftRepository();
    await repo.SaveAsync(new DriftSnapshot
    {
      SnapshotId = "a",
      BundleRoot = "bundle-a",
      RuleId = "SV-1001",
      PreviousState = null,
      CurrentState = "Pass",
      ChangeType = DriftChangeTypes.BaselineEstablished,
      DetectedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero)
    }, CancellationToken.None);
    await repo.SaveAsync(new DriftSnapshot
    {
      SnapshotId = "b",
      BundleRoot = "bundle-a",
      RuleId = "SV-1002",
      PreviousState = null,
      CurrentState = "Pass",
      ChangeType = DriftChangeTypes.BaselineEstablished,
      DetectedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero)
    }, CancellationToken.None);

    var service = new DriftDetectionService(repo, clock: new TestClock(new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)));
    var result = await service.CheckAsync(
      "bundle-a",
      new Dictionary<string, string>
      {
        ["SV-1001"] = "Fail"
      },
      autoRemediate: false,
      ct: CancellationToken.None);

    result.DriftEvents.Should().HaveCount(2);
    result.DriftEvents.Should().ContainSingle(e =>
      e.RuleId == "SV-1001"
      && e.ChangeType == DriftChangeTypes.StateChanged
      && e.PreviousState == "Pass"
      && e.CurrentState == "Fail");
    result.DriftEvents.Should().ContainSingle(e =>
      e.RuleId == "SV-1002"
      && e.ChangeType == DriftChangeTypes.MissingInCurrentScan
      && e.PreviousState == "Pass"
      && e.CurrentState == "Missing");
  }

  [Fact]
  public async Task CheckAsync_AutoRemediate_InvokesMatchingHandler()
  {
    var repo = new InMemoryDriftRepository();
    await repo.SaveAsync(new DriftSnapshot
    {
      SnapshotId = "baseline",
      BundleRoot = "bundle-a",
      RuleId = "SV-1001",
      PreviousState = null,
      CurrentState = "Pass",
      ChangeType = DriftChangeTypes.BaselineEstablished,
      DetectedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero)
    }, CancellationToken.None);

    var handler = new TestRemediationHandler("SV-1001");
    var service = new DriftDetectionService(repo, new[] { handler }, new TestClock(new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)));

    var result = await service.CheckAsync(
      "bundle-a",
      new Dictionary<string, string>
      {
        ["SV-1001"] = "Fail"
      },
      autoRemediate: true,
      ct: CancellationToken.None);

    result.AutoRemediatedRuleIds.Should().ContainSingle("SV-1001");
    handler.ApplyCalls.Should().Be(1);
  }

  [Fact]
  public async Task CheckBundleAsync_LoadsConsolidatedResultsFromBundle()
  {
    var bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-drift-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Verify", "run-a"));

    try
    {
      var jsonPath = Path.Combine(bundleRoot, "Verify", "run-a", "consolidated-results.json");
      File.WriteAllText(jsonPath, """
{
  "results": [
    { "ruleId": "SV-1001", "status": "NotAFinding" },
    { "ruleId": "SV-1002", "status": "Open" }
  ]
}
""");

      var repo = new InMemoryDriftRepository();
      var service = new DriftDetectionService(repo, clock: new TestClock(new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)));

      var result = await service.CheckBundleAsync(bundleRoot, autoRemediate: false, ct: CancellationToken.None);

      result.CurrentRuleCount.Should().Be(2);
      result.DriftEvents.Should().HaveCount(2);
      result.DriftEvents.Should().Contain(e => e.RuleId == "SV-1001" && e.CurrentState == "Pass");
      result.DriftEvents.Should().Contain(e => e.RuleId == "SV-1002" && e.CurrentState == "Open");
    }
    finally
    {
      try { Directory.Delete(bundleRoot, true); } catch { }
    }
  }

  [Fact]
  public async Task SchedulePeriodicChecks_TriggersChecks()
  {
    var repo = new InMemoryDriftRepository();
    var service = new DriftDetectionService(repo, clock: new TestClock(new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)));
    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    using var schedule = service.SchedulePeriodicChecks(
      bundleRoot: "bundle-a",
      interval: TimeSpan.FromMilliseconds(30),
      currentComplianceStateProvider: _ => Task.FromResult<IReadOnlyDictionary<string, string>>(
        new Dictionary<string, string>
        {
          ["SV-1001"] = "Pass"
        }),
      onCompleted: _ => completion.TrySetResult(true));

    var finishedTask = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2)));
    finishedTask.Should().Be(completion.Task);
    var completed = await completion.Task;
    completed.Should().BeTrue();
  }

  private sealed class TestClock : IClock
  {
    public TestClock(DateTimeOffset now)
    {
      Now = now;
    }

    public DateTimeOffset Now { get; }
  }

  private sealed class TestRemediationHandler : IRemediationHandler
  {
    public TestRemediationHandler(string ruleId)
    {
      RuleId = ruleId;
    }

    public string RuleId { get; }

    public string Category => "Test";

    public string Description => "Test remediation handler";

    public int ApplyCalls { get; private set; }

    public Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
      return Task.FromResult(new RemediationResult
      {
        RuleId = RuleId,
        HandlerCategory = Category,
        Success = true,
        Changed = false
      });
    }

    public Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct)
    {
      ApplyCalls++;
      return Task.FromResult(new RemediationResult
      {
        RuleId = RuleId,
        HandlerCategory = Category,
        Success = true,
        Changed = true,
        Detail = "Applied"
      });
    }
  }

  private sealed class InMemoryDriftRepository : IDriftRepository
  {
    private readonly List<DriftSnapshot> _snapshots = new();
    private readonly object _sync = new();

    public Task SaveAsync(DriftSnapshot snapshot, CancellationToken ct)
    {
      lock (_sync)
      {
        var existing = _snapshots.FindIndex(s => string.Equals(s.SnapshotId, snapshot.SnapshotId, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
          _snapshots[existing] = snapshot;
        else
          _snapshots.Add(snapshot);
      }

      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DriftSnapshot>> GetDriftHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct)
    {
      lock (_sync)
      {
        var query = _snapshots
          .Where(s => string.Equals(s.BundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(ruleId))
          query = query.Where(s => string.Equals(s.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

        var rows = query
          .OrderByDescending(s => s.DetectedAt)
          .ThenByDescending(s => s.SnapshotId, StringComparer.OrdinalIgnoreCase)
          .Take(limit)
          .ToList();

        return Task.FromResult<IReadOnlyList<DriftSnapshot>>(rows);
      }
    }

    public async Task<DriftSnapshot?> GetLatestSnapshotAsync(string bundleRoot, string ruleId, CancellationToken ct)
    {
      var rows = await GetDriftHistoryAsync(bundleRoot, ruleId, 1, ct);
      return rows.Count > 0 ? rows[0] : null;
    }
  }
}

using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class DriftDetectionServiceTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static Mock<IDriftRepository> BuildEmptyRepo()
    {
        var repo = new Mock<IDriftRepository>();
        repo.Setup(r => r.GetLatestByRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DriftSnapshot>)[]);
        repo.Setup(r => r.SaveBatchAsync(It.IsAny<IReadOnlyList<DriftSnapshot>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static Mock<IDriftRepository> BuildRepoWithSnapshot(string ruleId, string currentState)
    {
        var repo = new Mock<IDriftRepository>();
        repo.Setup(r => r.GetLatestByRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DriftSnapshot>)[new DriftSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                BundleRoot = "/bundle",
                RuleId = ruleId,
                CurrentState = currentState,
                ChangeType = DriftChangeTypes.BaselineEstablished,
                DetectedAt = FixedTime.AddDays(-1)
            }]);
        repo.Setup(r => r.SaveBatchAsync(It.IsAny<IReadOnlyList<DriftSnapshot>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    // ── Case-insensitive key matching ──────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_StateKeyIsCaseInsensitive_MatchesRegardlessOfCase()
    {
        // Baseline has "sv-100" (lower), current state has "SV-100" (upper) — same rule
        var repo = BuildRepoWithSnapshot("sv-100", "Pass");
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        // Current state uses upper-case key — NormalizeStateMap should treat as same key
        var currentState = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SV-100"] = "Pass"
        };

        var result = await svc.CheckAsync("/bundle", currentState, false, CancellationToken.None);

        result.DriftEvents.Should().BeEmpty(
            because: "'SV-100' and 'sv-100' are the same rule — no drift should be detected");
    }

    // ── Baseline established ───────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_NewRule_DetectedAsBaselineEstablished()
    {
        var repo = BuildEmptyRepo();
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        var currentState = new Dictionary<string, string> { ["SV-200"] = "Pass" };

        var result = await svc.CheckAsync("/bundle", currentState, false, CancellationToken.None);

        result.DriftEvents.Should().ContainSingle();
        result.DriftEvents[0].ChangeType.Should().Be(DriftChangeTypes.BaselineEstablished);
        result.DriftEvents[0].RuleId.Should().Be("SV-200");
    }

    // ── State changed ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_ChangedState_DetectedAsStateChanged()
    {
        var repo = BuildRepoWithSnapshot("SV-300", "Pass");
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        var currentState = new Dictionary<string, string> { ["SV-300"] = "Fail" };

        var result = await svc.CheckAsync("/bundle", currentState, false, CancellationToken.None);

        result.DriftEvents.Should().ContainSingle();
        result.DriftEvents[0].ChangeType.Should().Be(DriftChangeTypes.StateChanged);
        result.DriftEvents[0].PreviousState.Should().Be("Pass");
        result.DriftEvents[0].CurrentState.Should().Be("Fail");
    }

    // ── Missing rule ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_MissingRule_DetectedAsMissing()
    {
        var repo = BuildRepoWithSnapshot("SV-400", "Pass");
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        // SV-400 is in baseline but NOT in current state
        var currentState = new Dictionary<string, string>();

        var result = await svc.CheckAsync("/bundle", currentState, false, CancellationToken.None);

        result.DriftEvents.Should().ContainSingle();
        result.DriftEvents[0].ChangeType.Should().Be(DriftChangeTypes.MissingInCurrentScan);
        result.DriftEvents[0].RuleId.Should().Be("SV-400");
        result.DriftEvents[0].CurrentState.Should().Be("Missing");
    }

    // ── Empty current state ────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_EmptyCurrentState_AllPreviousMarkedMissing()
    {
        var repo = new Mock<IDriftRepository>();
        repo.Setup(r => r.GetLatestByRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DriftSnapshot>)[
                new DriftSnapshot { SnapshotId = "1", BundleRoot = "/b", RuleId = "SV-001", CurrentState = "Pass", ChangeType = DriftChangeTypes.BaselineEstablished, DetectedAt = FixedTime.AddDays(-1) },
                new DriftSnapshot { SnapshotId = "2", BundleRoot = "/b", RuleId = "SV-002", CurrentState = "Fail", ChangeType = DriftChangeTypes.BaselineEstablished, DetectedAt = FixedTime.AddDays(-1) }
            ]);
        repo.Setup(r => r.SaveBatchAsync(It.IsAny<IReadOnlyList<DriftSnapshot>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        var result = await svc.CheckAsync("/b", new Dictionary<string, string>(), false, CancellationToken.None);

        result.DriftEvents.Should().HaveCount(2);
        result.DriftEvents.Should().AllSatisfy(e =>
            e.ChangeType.Should().Be(DriftChangeTypes.MissingInCurrentScan));
    }

    // ── PeriodicDriftScheduler disposal ───────────────────────────────────────

    [Fact]
    public void PeriodicScheduler_Dispose_DoesNotThrow()
    {
        var repo = BuildEmptyRepo();
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        IDisposable? scheduler = null;
        var act = () =>
        {
            scheduler = svc.SchedulePeriodicChecks(
                "/bundle",
                TimeSpan.FromHours(1),
                _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
            scheduler.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void PeriodicScheduler_Dispose_Idempotent()
    {
        var repo = BuildEmptyRepo();
        var clock = new TestClock(FixedTime);
        var svc = new DriftDetectionService(repo.Object, clock: clock);

        var scheduler = svc.SchedulePeriodicChecks(
            "/bundle",
            TimeSpan.FromHours(1),
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));

        var act = () =>
        {
            scheduler.Dispose();
            scheduler.Dispose(); // second dispose must not throw
        };

        act.Should().NotThrow(because: "Dispose must be idempotent");
    }
}

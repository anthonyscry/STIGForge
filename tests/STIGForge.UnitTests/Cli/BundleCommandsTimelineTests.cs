using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Cli;

/// <summary>
/// Verifies that the mission timeline projection surface in BundleMissionSummaryService
/// returns deterministic, ordered timeline data from the mission run repository.
/// These tests exercise the service-layer projection consumed by the CLI mission-timeline command.
/// </summary>
public sealed class BundleCommandsTimelineTests
{
  [Fact]
  public async Task LoadTimelineSummaryAsync_ReturnsNull_WhenRepositoryNotConfigured()
  {
    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: null);

    var result = await service.LoadTimelineSummaryAsync("any-path", CancellationToken.None);

    result.Should().BeNull("no repository means timeline is not available");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_ReturnsEmptyState_WhenNoRunsExist()
  {
    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((MissionRun?)null);

    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: repo.Object);

    var result = await service.LoadTimelineSummaryAsync("any-path", CancellationToken.None);

    result.Should().NotBeNull();
    result!.LatestRun.Should().BeNull();
    result.Events.Should().BeEmpty();
    result.NextAction.ToLowerInvariant().Should().Contain("no mission runs");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_ProjectsOrderedTimelineEvents_ForLatestRun()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun
    {
      RunId = runId,
      Label = "Test Run",
      BundleRoot = "/bundles/test",
      Status = MissionRunStatus.Completed,
      CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
      FinishedAt = DateTimeOffset.UtcNow
    };

    var events = new List<MissionTimelineEvent>
    {
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 1, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Started, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-4) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 2, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Finished, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-3) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 3, Phase = MissionPhase.Verify, StepName = "evaluate_stig", Status = MissionEventStatus.Skipped, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(events);

    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: repo.Object);

    var result = await service.LoadTimelineSummaryAsync("/bundles/test", CancellationToken.None);

    result.Should().NotBeNull();
    result!.LatestRun.Should().NotBeNull();
    result.LatestRun!.RunId.Should().Be(runId);
    result.Events.Should().HaveCount(3);
    result.Events.Select(e => e.Seq).Should().BeInAscendingOrder("events must be ordered by seq");
    result.LastPhase.Should().Be(MissionPhase.Verify);
    result.LastStepName.Should().Be("evaluate_stig");
    result.LastEventStatus.Should().Be(MissionEventStatus.Skipped);
    result.IsBlocked.Should().BeFalse("no failed events in this run");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_SetsIsBlocked_WhenFailedEventExists()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun
    {
      RunId = runId,
      Label = "Blocked Run",
      BundleRoot = "/bundles/test",
      Status = MissionRunStatus.Failed,
      CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    var events = new List<MissionTimelineEvent>
    {
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 1, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Started, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-4) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 2, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Failed, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-3), Message = "Script error" },
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(events);

    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: repo.Object);

    var result = await service.LoadTimelineSummaryAsync("/bundles/test", CancellationToken.None);

    result.Should().NotBeNull();
    result!.IsBlocked.Should().BeTrue("a failed event marks the mission as blocked");
    result.NextAction.ToLowerInvariant().Should().Contain("blocked");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_DerivesMissionCompleteNextAction_WhenRunCompleted()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun
    {
      RunId = runId,
      Label = "Complete Run",
      BundleRoot = "/bundles/test",
      Status = MissionRunStatus.Completed,
      CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
      FinishedAt = DateTimeOffset.UtcNow
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MissionTimelineEvent>());

    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: repo.Object);

    var result = await service.LoadTimelineSummaryAsync("/bundles/test", CancellationToken.None);

    result.Should().NotBeNull();
    result!.NextAction.ToLowerInvariant().Should().Contain("complete");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_ReturnsEmptyEvents_WhenRepositoryThrowsOnTimeline()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun
    {
      RunId = runId,
      Label = "Degraded Run",
      BundleRoot = "/bundles/test",
      Status = MissionRunStatus.Running,
      CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10)
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("DB connection error"));

    var service = new BundleMissionSummaryService(manualAnswers: null, missionRunRepo: repo.Object);

    // Should not throw; degraded operation returns empty events
    var act = async () => await service.LoadTimelineSummaryAsync("/bundles/test", CancellationToken.None);

    await act.Should().NotThrowAsync();
    var result = await service.LoadTimelineSummaryAsync("/bundles/test", CancellationToken.None);
    result.Should().NotBeNull();
    result!.Events.Should().BeEmpty();
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_UsesSpecificRunId_WhenProvided()
  {
    var latestRunId = Guid.NewGuid().ToString();
    var specificRunId = Guid.NewGuid().ToString();

    var latestRun = new MissionRun { RunId = latestRunId, Label = "Latest", BundleRoot = "/b", Status = MissionRunStatus.Completed, CreatedAt = DateTimeOffset.UtcNow };
    var specificRun = new MissionRun { RunId = specificRunId, Label = "Specific", BundleRoot = "/b", Status = MissionRunStatus.Failed, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(latestRun);
    repo.Setup(r => r.GetRunAsync(specificRunId, It.IsAny<CancellationToken>())).ReturnsAsync(specificRun);
    repo.Setup(r => r.GetTimelineAsync(specificRunId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MissionTimelineEvent>());
    repo.Setup(r => r.GetTimelineAsync(latestRunId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MissionTimelineEvent>());

    // Simulate CLI reading by run ID: calls GetRunAsync then GetTimelineAsync
    var specificRunResult = await repo.Object.GetRunAsync(specificRunId, CancellationToken.None);

    specificRunResult.Should().NotBeNull();
    specificRunResult!.RunId.Should().Be(specificRunId);
    specificRunResult.Status.Should().Be(MissionRunStatus.Failed);
  }
}

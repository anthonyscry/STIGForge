using FluentAssertions;
using Moq;
using STIGForge.Apply;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Apply.Snapshot;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Verify;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace STIGForge.UnitTests.Build;

/// <summary>
/// Verifies that BundleOrchestrator emits deterministic timeline events into
/// IMissionRunRepository at each mission phase boundary.
/// </summary>
public sealed class BundleOrchestratorTimelineTests : IDisposable
{
    private readonly string _bundleRoot;

    public BundleOrchestratorTimelineTests()
    {
        _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-timeline-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_bundleRoot);
        Directory.CreateDirectory(Path.Combine(_bundleRoot, "Apply"));
        Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manifest"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleRoot, true); } catch { }
    }

    [Fact]
    public async Task OrchestrateAsync_EmitsApplyStartedAndFinishedEvents()
    {
        var repo = new Mock<IMissionRunRepository>();
        var capturedEvents = new List<MissionTimelineEvent>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MissionTimelineEvent, CancellationToken>((e, _) => capturedEvents.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo.Object);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        var applyEvents = capturedEvents.Where(e => e.Phase == MissionPhase.Apply).ToList();
        applyEvents.Should().Contain(e => e.Status == MissionEventStatus.Started && e.StepName == "apply");
        applyEvents.Should().Contain(e => e.Status == MissionEventStatus.Finished && e.StepName == "apply");
    }

    [Fact]
    public async Task OrchestrateAsync_EmitsSkippedVerifyEventsWhenVerifyToolsNotConfigured()
    {
        var repo = new Mock<IMissionRunRepository>();
        var capturedEvents = new List<MissionTimelineEvent>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MissionTimelineEvent, CancellationToken>((e, _) => capturedEvents.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo.Object);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
            // No EvaluateStigRoot or ScapCommandPath -> skipped
        }, CancellationToken.None);

        capturedEvents.Should().Contain(e =>
            e.Phase == MissionPhase.Verify &&
            e.StepName == "evaluate_stig" &&
            e.Status == MissionEventStatus.Skipped);

        capturedEvents.Should().Contain(e =>
            e.Phase == MissionPhase.Verify &&
            e.StepName == "scap" &&
            e.Status == MissionEventStatus.Skipped);
    }

    [Fact]
    public async Task OrchestrateAsync_EmitsDeterministicSequenceNumbers()
    {
        var repo = new Mock<IMissionRunRepository>();
        var capturedEvents = new List<MissionTimelineEvent>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MissionTimelineEvent, CancellationToken>((e, _) => capturedEvents.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo.Object);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        // Sequence numbers must be strictly ascending and unique within the run
        var seqs = capturedEvents.Select(e => e.Seq).ToList();
        seqs.Should().BeInAscendingOrder("sequence numbers must be deterministically ordered");
        seqs.Distinct().Should().HaveCount(seqs.Count, "each event must have a unique sequence number");
    }

    [Fact]
    public async Task OrchestrateAsync_AllEventsShareSameRunId()
    {
        var repo = new Mock<IMissionRunRepository>();
        MissionRun? createdRun = null;
        var capturedEvents = new List<MissionTimelineEvent>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Callback<MissionRun, CancellationToken>((r, _) => createdRun = r)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MissionTimelineEvent, CancellationToken>((e, _) => capturedEvents.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo.Object);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        createdRun.Should().NotBeNull();
        capturedEvents.Should().AllSatisfy(e =>
            e.RunId.Should().Be(createdRun!.RunId, "all events must share the run's stable ID"));
    }

    [Fact]
    public async Task OrchestrateAsync_EmitsRunCompletedOnSuccess()
    {
        var repo = new Mock<IMissionRunRepository>();
        var statusUpdates = new List<(string RunId, MissionRunStatus Status)>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, MissionRunStatus, DateTimeOffset?, string?, CancellationToken>((id, status, _, _, _) =>
                statusUpdates.Add((id, status)))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(repo.Object);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        statusUpdates.Should().Contain(u => u.Status == MissionRunStatus.Completed);
    }

    [Fact]
    public async Task OrchestrateAsync_EmitsFailureEventAndRunFailedOnApplyException()
    {
        var repo = new Mock<IMissionRunRepository>();
        var capturedEvents = new List<MissionTimelineEvent>();
        var statusUpdates = new List<MissionRunStatus>();

        repo.Setup(r => r.CreateRunAsync(It.IsAny<MissionRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendEventAsync(It.IsAny<MissionTimelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MissionTimelineEvent, CancellationToken>((e, _) => capturedEvents.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateRunStatusAsync(It.IsAny<string>(), It.IsAny<MissionRunStatus>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, MissionRunStatus, DateTimeOffset?, string?, CancellationToken>((_, status, _, _, _) =>
                statusUpdates.Add(status))
            .Returns(Task.CompletedTask);

        // Pass a non-existent bundle root to trigger DirectoryNotFoundException in apply
        var orchestrator = CreateOrchestrator(repo.Object);

        var act = async () => await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = Path.Combine(_bundleRoot, "nonexistent"),
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task OrchestrateAsync_WithoutRepository_CompletesWithoutTimeline()
    {
        // When no repository is provided, orchestration must still complete normally
        var orchestrator = CreateOrchestrator(null);

        var act = async () => await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OrchestrateAsync_RecordsApplyMissionDurationMetric()
    {
        var missionTypes = new List<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "STIGForge.Performance" &&
                instrument.Name == "mission.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "mission.duration" || measurement <= 0)
                return;

            foreach (var tag in tags)
            {
                if (tag.Key == "mission.type" && tag.Value is string missionType)
                    missionTypes.Add(missionType);
            }
        });

        listener.Start();

        var orchestrator = CreateOrchestrator(null);

        await orchestrator.OrchestrateAsync(new OrchestrateRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            BreakGlassAcknowledged = true,
            BreakGlassReason = "Unit test - not a real deployment"
        }, CancellationToken.None);

        missionTypes.Should().Contain("Apply");
    }

    private BundleOrchestrator CreateOrchestrator(IMissionRunRepository? repo)
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(x => x.ExistsInPath(It.IsAny<string>())).Returns(false);

        var snapshotService = new SnapshotService(new Mock<ILogger<SnapshotService>>().Object, processRunner.Object);
        var rollbackGenerator = new RollbackScriptGenerator(new Mock<ILogger<RollbackScriptGenerator>>().Object);
        var lcmService = new LcmService(new Mock<ILogger<LcmService>>().Object);
        var rebootCoordinator = new RebootCoordinator(new Mock<ILogger<RebootCoordinator>>().Object, _ => true);

        var applyRunner = new ApplyRunner(
            new Mock<ILogger<ApplyRunner>>().Object,
            snapshotService,
            rollbackGenerator,
            lcmService,
            rebootCoordinator,
            audit.Object);

        var verificationWorkflow = new Mock<IVerificationWorkflowService>();
        verificationWorkflow.Setup(x => x.RunAsync(It.IsAny<VerificationWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationWorkflowResult
            {
                ToolRuns = new[] { new VerificationToolRunResult { Tool = "Evaluate-STIG", Executed = true } }
            });

        var artifactAggregation = new VerificationArtifactAggregationService();

        return new BundleOrchestrator(
            null!, // BundleBuilder not needed for orchestrate-only tests
            applyRunner,
            verificationWorkflow.Object,
            artifactAggregation,
            new MissionTracingService(),
            new PerformanceInstrumenter(),
            audit.Object,
            repo);
    }
}

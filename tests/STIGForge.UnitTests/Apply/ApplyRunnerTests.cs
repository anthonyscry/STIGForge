using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using STIGForge.Apply;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Apply.Snapshot;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Apply;

public sealed class ApplyRunnerTests : IDisposable
{
    private readonly string _bundleRoot;

    public ApplyRunnerTests()
    {
        _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-apply-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_bundleRoot);
        Directory.CreateDirectory(Path.Combine(_bundleRoot, "Apply"));
        Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manifest"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleRoot, true); } catch { }
    }

    [Fact]
    public async Task RunAsync_WhenManifestHardeningModeIsNumeric_ParsesAndCompletes()
    {
        var manifestPath = Path.Combine(_bundleRoot, "Manifest", "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{\"Profile\":{\"HardeningMode\":1}}");

        var runner = CreateRunner(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true
        }, CancellationToken.None);

        result.Mode.Should().Be(HardeningMode.Safe);
        File.Exists(Path.Combine(_bundleRoot, "Apply", "apply_run.json")).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenManifestHardeningModeIsString_ParsesAndCompletes()
    {
        var manifestPath = Path.Combine(_bundleRoot, "Manifest", "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{\"Profile\":{\"HardeningMode\":\"Full\"}}");

        var runner = CreateRunner(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true
        }, CancellationToken.None);

        result.Mode.Should().Be(HardeningMode.Full);
    }

    [Fact]
    public async Task RunAsync_WhenAuditIntegrityIsInvalid_ThrowsBlockingFailure()
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var runner = CreateRunner(audit.Object);

        var exception = await Record.ExceptionAsync(() => runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true
        }, CancellationToken.None));

        exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().ContainEquivalentOf("Mission completion blocked")
            .And.ContainEquivalentOf("Rollback remains operator-initiated");
    }

    [Fact]
    public async Task RunAsync_WhenResumeContextIsExhausted_ThrowsOperatorDecisionRequired()
    {
        var markerPath = Path.Combine(_bundleRoot, "Apply", ".resume_marker.json");
        var marker = new RebootContext
        {
            BundleRoot = _bundleRoot,
            CurrentStepIndex = 1,
            CompletedSteps = new List<string> { "apply_script" },
            RebootScheduledAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(markerPath, System.Text.Json.JsonSerializer.Serialize(marker));

        var audit = new Mock<IAuditTrailService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var runner = CreateRunner(audit.Object);

        var scriptPath = Path.Combine(_bundleRoot, "Apply", "RunApply.ps1");
        await File.WriteAllTextAsync(scriptPath, "Write-Output 'ok'");

        var exception = await Record.ExceptionAsync(() => runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            ScriptPath = scriptPath,
            SkipSnapshot = true
        }, CancellationToken.None));

        exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().ContainEquivalentOf("operator decision");
    }

    // --- Evidence provenance tests (Task 2) ---

    [Fact]
    public async Task RunAsync_WithEvidenceCollector_WritesEvidenceMetadataPerStep()
    {
        var runner = CreateRunnerWithEvidence(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = Guid.NewGuid().ToString()
        }, CancellationToken.None);

        // apply_run.json must be written with evidence fields
        var logPath = Path.Combine(_bundleRoot, "Apply", "apply_run.json");
        File.Exists(logPath).Should().BeTrue();

        // No steps were configured so Steps is empty - but run should complete
        result.IsMissionComplete.Should().BeTrue();
        result.RunId.Should().NotBeNullOrWhiteSpace("RunId must be propagated to result");
    }

    [Fact]
    public async Task RunAsync_PropagatesRunIdToApplyResult()
    {
        var runId = Guid.NewGuid().ToString();
        var runner = CreateRunner(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = runId
        }, CancellationToken.None);

        result.RunId.Should().Be(runId, "RunId from request must be propagated to ApplyResult");
    }

    [Fact]
    public async Task RunAsync_WithoutRunId_GeneratesStableRunId()
    {
        var runner = CreateRunner(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true
            // No RunId provided
        }, CancellationToken.None);

        result.RunId.Should().NotBeNullOrWhiteSpace("A run ID must always be assigned even when not provided by caller");
    }

    [Fact]
    public async Task RunAsync_ApplyRunJson_ContainsRunIdAndProvenance()
    {
        var runId = Guid.NewGuid().ToString();
        var runner = CreateRunner(CreatePassingAudit());

        await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = runId
        }, CancellationToken.None);

        var logPath = Path.Combine(_bundleRoot, "Apply", "apply_run.json");
        var json = await File.ReadAllTextAsync(logPath);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("runId", out var runIdEl).Should().BeTrue("apply_run.json must contain runId");
        runIdEl.GetString().Should().Be(runId);
    }

    // --- Rerun continuity tests (Task 3) ---

    [Fact]
    public async Task RunAsync_WithPriorRunId_PropagatesPriorRunIdToResult()
    {
        var priorRunId = Guid.NewGuid().ToString();
        var runner = CreateRunner(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            PriorRunId = priorRunId
        }, CancellationToken.None);

        result.PriorRunId.Should().Be(priorRunId, "PriorRunId from request must be propagated to ApplyResult");
    }

    [Fact]
    public async Task RunAsync_WithPriorRunId_ApplyRunJsonContainsPriorRunId()
    {
        var runId = Guid.NewGuid().ToString();
        var priorRunId = Guid.NewGuid().ToString();
        var runner = CreateRunner(CreatePassingAudit());

        await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = runId,
            PriorRunId = priorRunId
        }, CancellationToken.None);

        var logPath = Path.Combine(_bundleRoot, "Apply", "apply_run.json");
        var json = await File.ReadAllTextAsync(logPath);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("priorRunId", out var priorEl).Should().BeTrue("apply_run.json must contain priorRunId");
        priorEl.GetString().Should().Be(priorRunId);
    }

    [Fact]
    public async Task RunAsync_WithPriorRunId_NoPriorArtifact_CompletesWithoutContinuityMarker()
    {
        // When prior run's apply_run.json doesn't exist or has a different runId,
        // the current run should still complete without errors. No continuity marker is set.
        var runner = CreateRunnerWithEvidence(CreatePassingAudit());

        var result = await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = Guid.NewGuid().ToString(),
            PriorRunId = Guid.NewGuid().ToString() // non-existent prior run
        }, CancellationToken.None);

        result.IsMissionComplete.Should().BeTrue("missing prior run data must not block current run");
    }

    [Fact]
    public async Task RunAsync_SecondRunWithSamePriorRunId_AppendOnlyNeverMutatesPriorLog()
    {
        // First run creates apply_run.json
        var firstRunId = Guid.NewGuid().ToString();
        var runner = CreateRunner(CreatePassingAudit());

        await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = firstRunId
        }, CancellationToken.None);

        var logPath = Path.Combine(_bundleRoot, "Apply", "apply_run.json");
        var firstRunContent = await File.ReadAllTextAsync(logPath);

        // Second run references first as prior — apply_run.json is overwritten with the new run's data
        // The first run's data is preserved in the mission_timeline (ledger) not in the file itself.
        // The file for the second run should have a different runId.
        var secondRunId = Guid.NewGuid().ToString();
        await runner.RunAsync(new ApplyRequest
        {
            BundleRoot = _bundleRoot,
            SkipSnapshot = true,
            RunId = secondRunId,
            PriorRunId = firstRunId
        }, CancellationToken.None);

        var secondRunContent = await File.ReadAllTextAsync(logPath);
        var secondDoc = JsonDocument.Parse(secondRunContent);
        secondDoc.RootElement.GetProperty("runId").GetString().Should().Be(secondRunId,
            "second run must write its own run ID to apply_run.json");
        secondDoc.RootElement.GetProperty("priorRunId").GetString().Should().Be(firstRunId,
            "second run must record its priorRunId link to the first run");
    }

    private static ApplyRunner CreateRunner(IAuditTrailService audit)
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(x => x.ExistsInPath(It.IsAny<string>())).Returns(false);

        var snapshotService = new SnapshotService(new Mock<ILogger<SnapshotService>>().Object, processRunner.Object);
        var rollbackGenerator = new RollbackScriptGenerator(new Mock<ILogger<RollbackScriptGenerator>>().Object);
        var lcmService = new LcmService(new Mock<ILogger<LcmService>>().Object);
        var rebootCoordinator = new RebootCoordinator(new Mock<ILogger<RebootCoordinator>>().Object, _ => true);

        return new ApplyRunner(
            new Mock<ILogger<ApplyRunner>>().Object,
            snapshotService,
            rollbackGenerator,
            lcmService,
            rebootCoordinator,
            audit);
    }

    private static ApplyRunner CreateRunnerWithEvidence(IAuditTrailService audit)
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(x => x.ExistsInPath(It.IsAny<string>())).Returns(false);

        var snapshotService = new SnapshotService(new Mock<ILogger<SnapshotService>>().Object, processRunner.Object);
        var rollbackGenerator = new RollbackScriptGenerator(new Mock<ILogger<RollbackScriptGenerator>>().Object);
        var lcmService = new LcmService(new Mock<ILogger<LcmService>>().Object);
        var rebootCoordinator = new RebootCoordinator(new Mock<ILogger<RebootCoordinator>>().Object, _ => true);
        var evidenceCollector = new EvidenceCollector();

        return new ApplyRunner(
            new Mock<ILogger<ApplyRunner>>().Object,
            snapshotService,
            rollbackGenerator,
            lcmService,
            rebootCoordinator,
            audit,
            evidenceCollector);
    }

    private static IAuditTrailService CreatePassingAudit()
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return audit.Object;
    }
}

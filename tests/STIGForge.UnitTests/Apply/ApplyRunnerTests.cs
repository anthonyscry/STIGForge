using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using STIGForge.Apply;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Apply.Snapshot;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

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

    private static IAuditTrailService CreatePassingAudit()
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return audit.Object;
    }
}

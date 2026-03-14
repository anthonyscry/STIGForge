using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class PhaseCCommandServiceTests
{
    // ── factory helpers ───────────────────────────────────────────────────────

    private static DriftDetectionService BuildDriftService()
    {
        var repo = new Mock<IDriftRepository>();
        repo.Setup(r => r.GetLatestByRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DriftSnapshot>)[]);
        repo.Setup(r => r.SaveBatchAsync(It.IsAny<IReadOnlyList<DriftSnapshot>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new DriftDetectionService(repo.Object);
    }

    private static RollbackService BuildRollbackService()
    {
        var repo = new Mock<IRollbackRepository>();
        repo.Setup(r => r.SaveAsync(It.IsAny<RollbackSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RollbackSnapshot?)null);
        repo.Setup(r => r.ListByBundleAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RollbackSnapshot>)[]);
        return new RollbackService(repo.Object);
    }

    private static PhaseCCommandService BuildMinimalService() =>
        new(BuildDriftService(), BuildRollbackService(), new GpoConflictDetector());

    // ── constructor null-guard ────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenDriftIsNull()
    {
        Action act = () => new PhaseCCommandService(null!, BuildRollbackService(), new GpoConflictDetector());

        act.Should().Throw<ArgumentNullException>().WithParameterName("drift");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenRollbackIsNull()
    {
        Action act = () => new PhaseCCommandService(BuildDriftService(), null!, new GpoConflictDetector());

        act.Should().Throw<ArgumentNullException>().WithParameterName("rollback");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenGpoConflictsIsNull()
    {
        Action act = () => new PhaseCCommandService(BuildDriftService(), BuildRollbackService(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("gpoConflicts");
    }

    [Fact]
    public void Constructor_Succeeds_WithOnlyRequiredArguments()
    {
        var act = BuildMinimalService;

        act.Should().NotThrow();
    }

    // ── optional service guards ───────────────────────────────────────────────

    [Fact]
    public async Task NessusImportAsync_ThrowsInvalidOperationException_WhenNessusIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.NessusImportAsync("/any/file.nessus", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NessusImporter*");
    }

    [Fact]
    public async Task AcasImportAsync_ThrowsInvalidOperationException_WhenAcasIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.AcasImportAsync("/any/file.nessus", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AcasCorrelationService*");
    }

    [Fact]
    public async Task CklImportAsync_ThrowsInvalidOperationException_WhenCklImporterIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.CklImportAsync("/any/file.ckl", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CklImporter*");
    }

    [Fact]
    public async Task CklExportAsync_ThrowsInvalidOperationException_WhenCklExporterIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.CklExportAsync("/bundle", "/out/file.ckl", "host", "title", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CklExporter*");
    }

    [Fact]
    public async Task MergeAsync_ThrowsInvalidOperationException_WhenCklMergeIsNull()
    {
        var svc = BuildMinimalService();
        var checklist = new CklChecklist();
        IReadOnlyList<ControlResult> existing = [];

        Func<Task> act = () => svc.MergeAsync(checklist, existing, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CklMergeService*");
    }

    [Fact]
    public async Task EmassPackageAsync_ThrowsInvalidOperationException_WhenEmassIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.EmassPackageAsync("/bundle", "Sys", "SYS", "/out", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EmassPackageGenerator*");
    }

    [Fact]
    public async Task CklMergeAsync_ThrowsInvalidOperationException_WhenCklImporterIsNull()
    {
        var svc = BuildMinimalService();

        Func<Task> act = () => svc.CklMergeAsync("/any.ckl", "/bundle", CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CklImporter*");
    }

    // ── cancellation token propagation ────────────────────────────────────────

    [Fact]
    public async Task NessusImportAsync_ThrowsOperationCanceled_WhenTokenAlreadyCancelled()
    {
        var svc = new PhaseCCommandService(
            BuildDriftService(), BuildRollbackService(), new GpoConflictDetector(),
            nessus: new NessusImporter());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => svc.NessusImportAsync("/any/file.nessus", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CklImportAsync_ThrowsOperationCanceled_WhenTokenAlreadyCancelled()
    {
        var svc = new PhaseCCommandService(
            BuildDriftService(), BuildRollbackService(), new GpoConflictDetector(),
            cklImporter: new CklImporter());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => svc.CklImportAsync("/any/file.ckl", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Windows service command helper (cross-platform observable behaviours) ─

    [Fact]
    public async Task AgentStatusAsync_ReturnsUnsupported_OnNonWindowsHost()
    {
        if (OperatingSystem.IsWindows()) return; // skip — Windows path tested separately

        var svc = BuildMinimalService();

        var status = await svc.AgentStatusAsync("my_valid_agent_svc", CancellationToken.None);

        status.Should().Be("Unsupported on non-Windows host");
    }

    [Fact]
    public async Task AgentInstallAsync_CompletesWithoutException_OnNonWindowsHost()
    {
        if (OperatingSystem.IsWindows()) return;

        var svc = BuildMinimalService();

        Func<Task> act = () => svc.AgentInstallAsync("my_valid_svc", "Display Name", "/path/to/exe", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AgentUninstallAsync_CompletesWithoutException_OnNonWindowsHost()
    {
        if (OperatingSystem.IsWindows()) return;

        var svc = BuildMinimalService();

        Func<Task> act = () => svc.AgentUninstallAsync("my_valid_svc", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AgentInstallAsync_ThrowsOperationCanceled_WhenTokenAlreadyCancelled()
    {
        var svc = BuildMinimalService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The method calls ct.ThrowIfCancellationRequested() before the Windows-only call
        Func<Task> act = () => svc.AgentInstallAsync("valid_svc", "Display", "/path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── rollback list delegates correctly ────────────────────────────────────

    [Fact]
    public async Task RollbackListAsync_ReturnsList_DelegatingToRollbackService()
    {
        var snapshot = new RollbackSnapshot
        {
            SnapshotId = "snap1",
            BundleRoot = "/bundle",
            Description = "pre-hardening"
        };
        var repo = new Mock<IRollbackRepository>();
        repo.Setup(r => r.ListByBundleAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RollbackSnapshot>)[snapshot]);
        repo.Setup(r => r.SaveAsync(It.IsAny<RollbackSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new PhaseCCommandService(
            BuildDriftService(),
            new RollbackService(repo.Object),
            new GpoConflictDetector());

        var result = await svc.RollbackListAsync("/bundle", 10, CancellationToken.None);

        result.Should().ContainSingle(s => s.SnapshotId == "snap1");
    }
}

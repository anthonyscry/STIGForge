using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class EmassPackageGeneratorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IComplianceTrendRepository> _trendRepo = new();
    private readonly Mock<IExceptionRepository> _exceptionRepo = new();
    private readonly Mock<IAuditTrailService> _auditTrail = new();
    private readonly DateTimeOffset _fixedNow = new(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public EmassPackageGeneratorTests()
    {
        _clock.Setup(c => c.Now).Returns(_fixedNow);
        _exceptionRepo.Setup(r => r.ListActiveByRuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<ControlException>());
        _trendRepo.Setup(r => r.GetLatestSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((ComplianceSnapshot?)null);
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);
    }

    public void Dispose() => _temp.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private string BundleRoot => _temp.Path;

    private EmassPackageGenerator CreateSut(
        bool withTrend = false,
        bool withException = false,
        bool withAudit = false)
        => new(
            withTrend ? _trendRepo.Object : null,
            withException ? _exceptionRepo.Object : null,
            withAudit ? _auditTrail.Object : null,
            _clock.Object);

    private void WriteManifest(string? name = "TestSystem", string? version = "1.0")
    {
        var manifestDir = Path.Combine(BundleRoot, "Manifest");
        Directory.CreateDirectory(manifestDir);
        var manifest = new { Name = name, Version = version };
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"),
            JsonSerializer.Serialize(manifest));
    }

    private void WriteControls(IReadOnlyList<object> controls)
    {
        var manifestDir = Path.Combine(BundleRoot, "Manifest");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "pack_controls.json"),
            JsonSerializer.Serialize(controls));
    }

    private static object MakeControl(string id, string title, string severity = "medium", bool isManual = false)
        => new { ControlId = id, Title = title, Severity = severity, IsManual = isManual,
                 SourcePackId = "pack1", Discussion = (string?)null, ExternalIds = new { } };

    // ── argument validation ───────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePackageAsync_NullBundleRoot_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = async () => await sut.GeneratePackageAsync(null!, "System", "SYS", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
                 .WithMessage("*bundleRoot*");
    }

    [Fact]
    public async Task GeneratePackageAsync_WhitespaceBundleRoot_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = async () => await sut.GeneratePackageAsync("  ", "System", "SYS", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GeneratePackageAsync_NullSystemName_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = async () => await sut.GeneratePackageAsync(BundleRoot, null!, "SYS", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
                 .WithMessage("*systemName*");
    }

    // ── happy-path generation ────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePackageAsync_EmptyBundle_ReturnsPackageWithExpectedFields()
    {
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "My System", "MSYS", null, CancellationToken.None);

        pkg.SystemName.Should().Be("My System");
        pkg.SystemAcronym.Should().Be("MSYS");
        pkg.BundleRoot.Should().Be(BundleRoot);
        pkg.PackageId.Should().NotBeNullOrEmpty();
        pkg.GeneratedAt.Should().Be(_fixedNow);
    }

    [Fact]
    public async Task GeneratePackageAsync_WithControls_GeneratesCcmEntries()
    {
        WriteControls([
            MakeControl("AC-1", "Access Control Policy"),
            MakeControl("AC-2", "Account Management"),
        ]);
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.ControlCorrelationMatrix.Controls.Should().HaveCount(2);
        pkg.ControlCorrelationMatrix.Controls.Select(c => c.ControlId).Should()
           .BeEquivalentTo(["AC-1", "AC-2"]);
    }

    [Fact]
    public async Task GeneratePackageAsync_WithControls_GeneratesSspEntries()
    {
        WriteControls([MakeControl("SC-1", "System Communications Protection")]);
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "MyApp", "APP", null, CancellationToken.None);

        pkg.SystemSecurityPlan.SystemName.Should().Be("MyApp");
        pkg.SystemSecurityPlan.ControlImplementations.Should().ContainSingle()
           .Which.ControlId.Should().Be("SC-1");
    }

    [Fact]
    public async Task GeneratePackageAsync_HighSeverityControl_AppearsInPoam()
    {
        WriteControls([MakeControl("SI-1", "High Sev Control", severity: "high")]);
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.Poam.Entries.Should().ContainSingle()
           .Which.ControlId.Should().Be("SI-1");
    }

    [Fact]
    public async Task GeneratePackageAsync_ManualControl_AppearsInPoam()
    {
        WriteControls([MakeControl("IR-1", "Manual Check", isManual: true)]);
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.Poam.Entries.Should().ContainSingle()
           .Which.ControlId.Should().Be("IR-1");
    }

    [Fact]
    public async Task GeneratePackageAsync_WithEvidenceDir_CollectsArtifacts()
    {
        var evidenceDir = Path.Combine(BundleRoot, "Evidence");
        Directory.CreateDirectory(evidenceDir);
        File.WriteAllText(Path.Combine(evidenceDir, "output.txt"), "evidence content");
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.EvidenceArtifacts.Should().ContainSingle()
           .Which.FileName.Should().Be("output.txt");
    }

    [Fact]
    public async Task GeneratePackageAsync_WithVerifyJsonFile_CollectsScanResult()
    {
        var verifyDir = Path.Combine(BundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);
        File.WriteAllText(Path.Combine(verifyDir, "scan-report.json"), "{}");
        var sut = CreateSut();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.ScanResults.Should().ContainSingle()
           .Which.Tool.Should().Be("scan-report");
    }

    [Fact]
    public async Task GeneratePackageAsync_WithPreviousPackageDir_GeneratesChangeLog()
    {
        var prevDir = Path.Combine(_temp.Path, "previous-package");
        Directory.CreateDirectory(prevDir);
        // Write a minimal previous package manifest so it's a valid directory
        File.WriteAllText(Path.Combine(prevDir, "package-manifest.json"), "{}");

        var sut = CreateSut();
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", prevDir, CancellationToken.None);

        pkg.ChangeLog.Should().NotBeNull();
    }

    [Fact]
    public async Task GeneratePackageAsync_NullPreviousPackagePath_ChangeLogIsNull()
    {
        var sut = CreateSut();
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "Sys", "SYS", null, CancellationToken.None);

        pkg.ChangeLog.Should().BeNull();
    }

    // ── SavePackageAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SavePackageAsync_CreatesPackageDirWithExpectedFiles()
    {
        var sut = CreateSut();
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "SaveTest", "SVT", null, CancellationToken.None);
        var outputDir = Path.Combine(_temp.Path, "output");

        await sut.SavePackageAsync(pkg, outputDir, CancellationToken.None);

        var packageDirs = Directory.GetDirectories(outputDir, "eMASS_SVT_*");
        packageDirs.Should().ContainSingle();

        var packageDir = packageDirs[0];
        File.Exists(Path.Combine(packageDir, "package-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "control-correlation-matrix.json")).Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "system-security-plan.json")).Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "poam.json")).Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "sha256-checksums.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task SavePackageAsync_WithAuditTrail_RecordsAuditEntry()
    {
        var sut = CreateSut(withAudit: true);
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "AuditTest", "AUT", null, CancellationToken.None);
        var outputDir = Path.Combine(_temp.Path, "audited-output");

        await sut.SavePackageAsync(pkg, outputDir, CancellationToken.None);

        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditEntry>(e => e.Action == "EmassPackage" && e.Result == "Success"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavePackageAsync_WithEvidenceArtifact_CopiesFileToEvidenceDir()
    {
        var evidenceDir = Path.Combine(BundleRoot, "Evidence");
        Directory.CreateDirectory(evidenceDir);
        var srcFile = Path.Combine(evidenceDir, "artifact.txt");
        File.WriteAllText(srcFile, "evidence data");

        var sut = CreateSut();
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "EvidSys", "EVS", null, CancellationToken.None);
        var outputDir = Path.Combine(_temp.Path, "ev-output");

        await sut.SavePackageAsync(pkg, outputDir, CancellationToken.None);

        var packageDir = Directory.GetDirectories(outputDir, "eMASS_EVS_*")[0];
        var copiedFiles = Directory.GetFiles(Path.Combine(packageDir, "evidence"), "*.*", SearchOption.AllDirectories);
        copiedFiles.Should().Contain(f => Path.GetFileName(f) == "artifact.txt");
    }

    [Fact]
    public async Task SavePackageAsync_WithChangeLog_WritesChangeLogFile()
    {
        var prevDir = Path.Combine(_temp.Path, "prev");
        Directory.CreateDirectory(prevDir);
        File.WriteAllText(Path.Combine(prevDir, "package-manifest.json"), "{}");

        var sut = CreateSut();
        var pkg = await sut.GeneratePackageAsync(BundleRoot, "CLSys", "CLS", prevDir, CancellationToken.None);
        var outputDir = Path.Combine(_temp.Path, "cl-output");

        await sut.SavePackageAsync(pkg, outputDir, CancellationToken.None);

        var packageDir = Directory.GetDirectories(outputDir, "eMASS_CLS_*")[0];
        File.Exists(Path.Combine(packageDir, "change-log.md")).Should().BeTrue();
    }

    // ── constructor defaults ──────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePackageAsync_DefaultClock_PackageIdIsNonEmpty()
    {
        var sut = new EmassPackageGenerator();

        var pkg = await sut.GeneratePackageAsync(BundleRoot, "DefaultClock", "DC", null, CancellationToken.None);

        pkg.PackageId.Should().NotBeNullOrWhiteSpace();
        pkg.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}

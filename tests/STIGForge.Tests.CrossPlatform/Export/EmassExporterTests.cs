using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;
using VerifyControlResult = STIGForge.Verify.ControlResult;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class EmassExporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly Mock<IPathBuilder> _pathBuilderMock = new();
    private readonly Mock<IHashingService> _hashMock = new();
    private readonly Mock<IAuditTrailService> _auditMock = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    private EmassExporter CreateSut(bool withAudit = false) =>
        new(_pathBuilderMock.Object, _hashMock.Object, withAudit ? _auditMock.Object : null);

    private string NewBundle() =>
        Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Sets up a minimal valid bundle root with a Manifest/manifest.json
    /// and optionally a Verify/consolidated-results.json.
    /// </summary>
    private string BuildBundle(
        IEnumerable<VerifyControlResult>? results = null,
        string systemName = "TestSystem",
        string bundleId = "test-bundle-id",
        string packName = "TestPack",
        string profileName = "TestProfile")
    {
        var root = NewBundle();
        Directory.CreateDirectory(root);

        var manifestDir = Path.Combine(root, "Manifest");
        Directory.CreateDirectory(manifestDir);

        var manifest = new
        {
            bundleId,
            run = new
            {
                systemName,
                osTarget = 0,
                roleTemplate = 0,
                profileName,
                packName,
                runId = Guid.NewGuid().ToString(),
                packId = "pk-001",
                profileId = "prof-001"
            }
        };
        File.WriteAllText(
            Path.Combine(manifestDir, "manifest.json"),
            JsonSerializer.Serialize(manifest));

        if (results != null)
        {
            var verifyDir = Path.Combine(root, "Verify");
            Directory.CreateDirectory(verifyDir);
            var report = new
            {
                Tool = "unit-test",
                ToolVersion = "1.0",
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                OutputRoot = verifyDir,
                Results = results
            };
            File.WriteAllText(
                Path.Combine(verifyDir, "consolidated-results.json"),
                JsonSerializer.Serialize(report));
        }

        return root;
    }

    private void SetupHashMock()
    {
        _hashMock
            .Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("aabbccdd00112233aabbccdd00112233aabbccdd00112233aabbccdd00112233");
    }

    private string SetupPathBuilderWithExportRoot()
    {
        var exportRoot = Path.Combine(_temp.Path, "export-" + Guid.NewGuid().ToString("N"));
        _pathBuilderMock
            .Setup(p => p.GetEmassExportRoot(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()))
            .Returns(exportRoot);
        return exportRoot;
    }

    // ── ExportAsync: guard clauses ────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_EmptyBundleRoot_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ExportAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsync_NullBundleRoot_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = "" };

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ExportAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsync_NonExistentBundleRoot_ThrowsDirectoryNotFoundException()
    {
        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = Path.Combine(_temp.Path, "no-such-dir") };

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => sut.ExportAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsync_MissingBundleManifest_ThrowsFileNotFoundException()
    {
        var bundleRoot = NewBundle();
        Directory.CreateDirectory(bundleRoot);
        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = bundleRoot };

        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.ExportAsync(request, CancellationToken.None));
    }

    // ── ExportAsync: happy path with no verify results ────────────────────────

    [Fact]
    public async Task ExportAsync_MinimalBundle_CreatesExportDirectories()
    {
        var bundleRoot = BuildBundle();
        var exportRoot = SetupPathBuilderWithExportRoot();
        SetupHashMock();

        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = bundleRoot };

        var result = await sut.ExportAsync(request, CancellationToken.None);

        result.OutputRoot.Should().Be(exportRoot);
        Directory.Exists(Path.Combine(exportRoot, "00_Manifest")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "01_Scans")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "02_Checklists")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "03_POAM")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "04_Evidence")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "05_Attestations")).Should().BeTrue();
        Directory.Exists(Path.Combine(exportRoot, "06_Index")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_MinimalBundle_CreatesRequiredFiles()
    {
        var bundleRoot = BuildBundle();
        var exportRoot = SetupPathBuilderWithExportRoot();
        SetupHashMock();

        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = bundleRoot };

        var result = await sut.ExportAsync(request, CancellationToken.None);

        File.Exists(Path.Combine(exportRoot, "README_Submission.txt")).Should().BeTrue();
        File.Exists(Path.Combine(exportRoot, "00_Manifest", "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(exportRoot, "00_Manifest", "file_hashes.sha256")).Should().BeTrue();
        File.Exists(Path.Combine(exportRoot, "00_Manifest", "export_log.txt")).Should().BeTrue();
        File.Exists(Path.Combine(exportRoot, "06_Index", "control_evidence_index.csv")).Should().BeTrue();
    }

    // ── ExportAsync: explicit OutputRoot bypasses IPathBuilder ───────────────

    [Fact]
    public async Task ExportAsync_WithExplicitOutputRoot_UsesProvidedPath()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "explicit-export-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        var request = new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput };

        var result = await sut.ExportAsync(request, CancellationToken.None);

        result.OutputRoot.Should().Be(explicitOutput);
        _pathBuilderMock.Verify(p => p.GetEmassExportRoot(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    // ── ExportAsync: verify results are processed ─────────────────────────────

    [Fact]
    public async Task ExportAsync_BundleWithResults_WritesControlEvidenceIndex()
    {
        var results = new[]
        {
            new VerifyControlResult { VulnId = "V-001", RuleId = "SV-001", Title = "Test", Severity = "medium", Status = "pass", Tool = "StigTool" },
            new VerifyControlResult { VulnId = "V-002", RuleId = "SV-002", Title = "Fail Test", Severity = "high", Status = "fail", Tool = "StigTool" }
        };
        var bundleRoot = BuildBundle(results);
        var explicitOutput = Path.Combine(_temp.Path, "exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        var result = await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        var indexPath = Path.Combine(explicitOutput, "06_Index", "control_evidence_index.csv");
        File.Exists(indexPath).Should().BeTrue();
        var csv = File.ReadAllText(indexPath);
        csv.Should().Contain("V-001");
        csv.Should().Contain("V-002");
    }

    [Fact]
    public async Task ExportAsync_BundleWithFailResults_WritesPoamFile()
    {
        var results = new[]
        {
            new VerifyControlResult { VulnId = "V-001", RuleId = "SV-001", Title = "Fail Test", Severity = "high", Status = "fail", Tool = "StigTool" }
        };
        var bundleRoot = BuildBundle(results);
        var explicitOutput = Path.Combine(_temp.Path, "poam-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        File.Exists(Path.Combine(explicitOutput, "03_POAM", "poam.json")).Should().BeTrue();
        File.Exists(Path.Combine(explicitOutput, "03_POAM", "poam.csv")).Should().BeTrue();
    }

    // ── ExportAsync: copies source files ─────────────────────────────────────

    [Fact]
    public async Task ExportAsync_BundleWithVerifyDir_CopiesScansToOutput()
    {
        var bundleRoot = BuildBundle(results: [
            new VerifyControlResult { VulnId = "V-001", Status = "pass" }
        ]);
        // The Verify dir should have been created by BuildBundle
        var explicitOutput = Path.Combine(_temp.Path, "scans-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        var scansRaw = Path.Combine(explicitOutput, "01_Scans", "raw");
        Directory.Exists(scansRaw).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_BundleWithEvidenceDir_CopiesEvidenceToOutput()
    {
        var bundleRoot = BuildBundle();
        var evidenceSrc = Path.Combine(bundleRoot, "Evidence");
        Directory.CreateDirectory(evidenceSrc);
        File.WriteAllText(Path.Combine(evidenceSrc, "screenshot.png"), "fake-img");

        var explicitOutput = Path.Combine(_temp.Path, "ev-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        var destFile = Path.Combine(explicitOutput, "04_Evidence", "screenshot.png");
        File.Exists(destFile).Should().BeTrue();
    }

    // ── ExportAsync: hash manifest content ───────────────────────────────────

    [Fact]
    public async Task ExportAsync_Always_WritesHashManifestWithHashes()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "hash-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        var hashManifest = File.ReadAllText(Path.Combine(explicitOutput, "00_Manifest", "file_hashes.sha256"));
        hashManifest.Should().Contain("aabbccdd00112233");
    }

    // ── ExportAsync: IHashingService is used ─────────────────────────────────

    [Fact]
    public async Task ExportAsync_Always_CallsHashingService()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "hs-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        _hashMock.Verify(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── ExportAsync: audit trail ──────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_WithAuditService_RecordsAuditEntry()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "aud-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();
        _auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(withAudit: true);
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        _auditMock.Verify(a => a.RecordAsync(
            It.Is<AuditEntry>(e => e.Action == "export-emass"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportAsync_AuditServiceThrows_WarningAddedToResult()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "audf-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();
        _auditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit db offline"));

        var sut = CreateSut(withAudit: true);
        var result = await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        result.Warnings.Should().Contain(w => w.Contains("audit"));
    }

    // ── ExportAsync: result metadata ─────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Always_ReturnsValidationResult()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "val-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        var result = await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        result.ValidationResult.Should().NotBeNull();
        result.ManifestPath.Should().Contain("manifest.json");
        result.IndexPath.Should().Contain("control_evidence_index.csv");
    }

    [Fact]
    public async Task ExportAsync_Always_ReturnsValidationReportPaths()
    {
        var bundleRoot = BuildBundle();
        var explicitOutput = Path.Combine(_temp.Path, "vrp-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        var result = await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        File.Exists(result.ValidationReportPath).Should().BeTrue();
        File.Exists(result.ValidationReportJsonPath).Should().BeTrue();
    }

    // ── ExportAsync: manual answers merged ───────────────────────────────────

    [Fact]
    public async Task ExportAsync_BundleWithManualAnswers_MergesAnswers()
    {
        var bundleRoot = BuildBundle(results: [
            new VerifyControlResult { VulnId = "V-001", RuleId = "SV-001", Status = "notreviewed", Tool = "auto" }
        ]);

        // Write a manual answer that overrides V-001
        var manualDir = Path.Combine(bundleRoot, "Manual");
        Directory.CreateDirectory(manualDir);
        var answers = new
        {
            answers = new[]
            {
                new { vulnId = "V-001", ruleId = "SV-001", status = "pass", comment = "Manually verified", updatedAt = DateTimeOffset.UtcNow }
            }
        };
        File.WriteAllText(Path.Combine(manualDir, "answers.json"), JsonSerializer.Serialize(answers));

        var explicitOutput = Path.Combine(_temp.Path, "man-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        var result = await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        result.Should().NotBeNull();
        var indexContent = File.ReadAllText(Path.Combine(explicitOutput, "06_Index", "control_evidence_index.csv"));
        indexContent.Should().Contain("V-001");
    }

    // ── ExportAsync: CKL files copied ────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_BundleWithCklFiles_CopiesChecklistsToOutput()
    {
        var bundleRoot = BuildBundle();
        var verifyDir = Path.Combine(bundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);
        File.WriteAllText(Path.Combine(verifyDir, "checklist.ckl"), "<CHECKLIST/>");

        var explicitOutput = Path.Combine(_temp.Path, "ckl-exp-" + Guid.NewGuid().ToString("N"));
        SetupHashMock();

        var sut = CreateSut();
        await sut.ExportAsync(new ExportRequest { BundleRoot = bundleRoot, OutputRoot = explicitOutput }, CancellationToken.None);

        File.Exists(Path.Combine(explicitOutput, "02_Checklists", "checklist.ckl")).Should().BeTrue();
    }
}

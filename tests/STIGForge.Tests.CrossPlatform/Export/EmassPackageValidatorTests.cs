using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class EmassPackageValidatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] RequiredDirs =
    [
        "00_Manifest", "01_Scans", "02_Checklists", "03_POAM",
        "04_Evidence", "05_Attestations", "06_Index"
    ];

    private void CreateRequiredDirs(string root)
    {
        foreach (var dir in RequiredDirs)
            Directory.CreateDirectory(Path.Combine(root, dir));
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// Builds a package root with all required dirs, required files (valid JSON where
    /// appropriate), a correct hash manifest, and a README.
    /// Returns the root path.
    /// </summary>
    private string BuildValidPackage()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);

        // Required files with valid content
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"),
            """{"bundleId":"test","run":{"systemName":"TestSystem"}}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.json"),
            """{"items":[]}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"),
            "VulnId,RuleId,Title\n");
        WriteText(Path.Combine(root, "05_Attestations", "attestations.json"),
            """{"attestations":[]}""");
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"),
            "VulnId,RuleId,Title,Severity,Status\nV-001,SV-001,Test,medium,Pass\n");
        WriteText(Path.Combine(root, "README_Submission.txt"), "STIGForge eMASS Package");

        // Build correct hash manifest (all files in the package except the manifest itself)
        BuildHashManifest(root);

        return root;
    }

    private static void BuildHashManifest(string root)
    {
        var manifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(f, manifestPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var hash = ComputeSha256(file);
            var rel = GetRelativePath(root, file);
            sb.AppendLine(hash + "  " + rel);
        }
        File.WriteAllText(manifestPath, sb.ToString(), Encoding.UTF8);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string GetRelativePath(string root, string path)
    {
        var rootUri = new Uri(root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar);
        var pathUri = new Uri(path);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    // ── ValidatePackage: non-existent root ───────────────────────────────────

    [Fact]
    public void ValidatePackage_NonExistentRoot_ReturnsFailure()
    {
        var sut = new EmassPackageValidator();
        var result = sut.ValidatePackage(Path.Combine(_temp.Path, "does-not-exist"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch("*not found*");
    }

    // ── ValidatePackage: empty directory (missing dirs + files) ─────────────

    [Fact]
    public void ValidatePackage_EmptyRoot_ReportsAllMissingDirsAndFiles()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("00_Manifest"));
        result.Errors.Should().Contain(e => e.Contains("03_POAM"));
        result.Metrics.MissingRequiredDirectoryCount.Should().Be(7);
        result.Metrics.MissingRequiredFileCount.Should().BeGreaterThan(0);
    }

    // ── ValidatePackage: dirs present, files missing ─────────────────────────

    [Fact]
    public void ValidatePackage_DirsOnlyNoFiles_ReportsMissingFiles()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.IsValid.Should().BeFalse();
        result.Metrics.MissingRequiredDirectoryCount.Should().Be(0);
        result.Metrics.MissingRequiredFileCount.Should().BeGreaterThan(0);
    }

    // ── ValidatePackage: empty scan dir → warning ────────────────────────────

    [Fact]
    public void ValidatePackage_EmptyScanDir_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("01_Scans"));
    }

    // ── ValidatePackage: empty evidence dir → warning ────────────────────────

    [Fact]
    public void ValidatePackage_EmptyEvidenceDir_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("04_Evidence"));
    }

    // ── ValidatePackage: hash manifest missing → error ───────────────────────

    [Fact]
    public void ValidatePackage_MissingHashManifest_ReportsHashManifestMissing()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("Hash manifest missing"));
    }

    // ── ValidatePackage: hash manifest with wrong hash → error ───────────────

    [Fact]
    public void ValidatePackage_HashMismatch_ReportsHashMismatch()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        var readmePath = Path.Combine(root, "README_Submission.txt");
        WriteText(readmePath, "content");

        // Write hash manifest with a deliberately wrong hash
        var manifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
        var rel = GetRelativePath(root, readmePath);
        WriteText(manifestPath, "deadbeef00000000000000000000000000000000000000000000000000000000  " + rel + Environment.NewLine);

        var sut = new EmassPackageValidator();
        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("Hash mismatch"));
        result.Metrics.HashMismatchCount.Should().BeGreaterThan(0);
    }

    // ── ValidatePackage: file in manifest but missing from disk → error ──────

    [Fact]
    public void ValidatePackage_ManifestFileNotOnDisk_ReportsFileMissing()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);

        // Reference a file that does not exist
        var manifestPath = Path.Combine(root, "00_Manifest", "file_hashes.sha256");
        WriteText(manifestPath, "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890  03_POAM" + Path.DirectorySeparatorChar + "poam.json" + Environment.NewLine);

        var sut = new EmassPackageValidator();
        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("missing from package"));
    }

    // ── ValidatePackage: file on disk but not in manifest → warning ──────────

    [Fact]
    public void ValidatePackage_ExtraFileNotInManifest_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);

        // Write manifest that covers nothing
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        // Put a file in the package
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"), "data");

        var sut = new EmassPackageValidator();
        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("not in hash manifest"));
    }

    // ── ValidatePackage: invalid manifest.json → error ───────────────────────

    [Fact]
    public void ValidatePackage_InvalidManifestJson_ReportsInvalidJson()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "THIS IS NOT JSON {{{");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("Invalid manifest.json"));
    }

    // ── ValidatePackage: invalid poam.json → error ───────────────────────────

    [Fact]
    public void ValidatePackage_InvalidPoamJson_ReportsInvalidJson()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        WriteText(Path.Combine(root, "03_POAM", "poam.json"), "not-json");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("Invalid poam.json"));
    }

    // ── ValidatePackage: control_evidence_index.csv empty → warning ──────────

    [Fact]
    public void ValidatePackage_EmptyControlIndex_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"), "header-only\n");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("control_evidence_index.csv"));
    }

    // ── ValidatePackage: POAM item missing from index → error ────────────────

    [Fact]
    public void ValidatePackage_PoamItemNotInIndex_ReportsCrossArtifactMismatch()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        // POAM references V-999 which is not in the index
        WriteText(Path.Combine(root, "03_POAM", "poam.json"),
            """{"items":[{"controlId":"V-999","vulnId":"V-999","ruleId":"SV-999"}]}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"), "header\n");
        WriteText(Path.Combine(root, "05_Attestations", "attestations.json"), """{"attestations":[]}""");
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"),
            "VulnId,RuleId,Title,Severity,Status\nV-001,SV-001,Test,medium,Pass\n");
        WriteText(Path.Combine(root, "README_Submission.txt"), "readme");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("POA&M item does not map"));
        result.Metrics.CrossArtifactMismatchCount.Should().BeGreaterThan(0);
    }

    // ── ValidatePackage: failed control missing POAM → error ─────────────────

    [Fact]
    public void ValidatePackage_FailedControlMissingPoam_ReportsMissingPoamEntry()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        WriteText(Path.Combine(root, "03_POAM", "poam.json"), """{"items":[]}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"), "header\n");
        WriteText(Path.Combine(root, "05_Attestations", "attestations.json"), """{"attestations":[]}""");
        // V-001 has status Fail but no POAM entry
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"),
            "VulnId,RuleId,Title,Severity,Status\nV-001,SV-001,Test,medium,Fail\n");
        WriteText(Path.Combine(root, "README_Submission.txt"), "readme");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Errors.Should().Contain(e => e.Contains("Failed control missing POA&M entry"));
    }

    // ── ValidatePackage: attestation with empty ControlId → warning ──────────

    [Fact]
    public void ValidatePackage_AttestationWithEmptyControlId_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        WriteText(Path.Combine(root, "03_POAM", "poam.json"), """{"items":[]}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"), "header\n");
        WriteText(Path.Combine(root, "05_Attestations", "attestations.json"),
            """{"attestations":[{"controlId":"","complianceStatus":"Compliant"}]}""");
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"),
            "VulnId,RuleId,Title,Severity,Status\nV-001,SV-001,Test,medium,Pass\n");
        WriteText(Path.Combine(root, "README_Submission.txt"), "readme");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("empty ControlId"));
    }

    // ── ValidatePackage: attestation with bad status → warning ───────────────

    [Fact]
    public void ValidatePackage_AttestationWithBadStatus_AddsWarning()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        CreateRequiredDirs(root);
        WriteText(Path.Combine(root, "00_Manifest", "manifest.json"), "{}");
        WriteText(Path.Combine(root, "00_Manifest", "file_hashes.sha256"), "");
        WriteText(Path.Combine(root, "03_POAM", "poam.json"), """{"items":[]}""");
        WriteText(Path.Combine(root, "03_POAM", "poam.csv"), "header\n");
        WriteText(Path.Combine(root, "05_Attestations", "attestations.json"),
            """{"attestations":[{"controlId":"V-001","complianceStatus":"InvalidStatus"}]}""");
        WriteText(Path.Combine(root, "06_Index", "control_evidence_index.csv"),
            "VulnId,RuleId,Title,Severity,Status\nV-001,SV-001,Test,medium,Pass\n");
        WriteText(Path.Combine(root, "README_Submission.txt"), "readme");
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Warnings.Should().Contain(w => w.Contains("unsupported compliance status"));
    }

    // ── ValidatePackage: valid package → IsValid ─────────────────────────────

    [Fact]
    public void ValidatePackage_ValidPackage_IsValid()
    {
        var root = BuildValidPackage();
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.PackageRoot.Should().Be(root);
        result.ValidatedAt.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
    }

    // ── ValidatePackage: metrics populated ───────────────────────────────────

    [Fact]
    public void ValidatePackage_ValidPackage_MetricsArePopulated()
    {
        var root = BuildValidPackage();
        var sut = new EmassPackageValidator();

        var result = sut.ValidatePackage(root);

        result.Metrics.RequiredDirectoriesChecked.Should().Be(7);
        result.Metrics.RequiredFilesChecked.Should().BeGreaterThan(0);
        result.Metrics.HashManifestEntryCount.Should().BeGreaterThan(0);
    }

    // ── WriteValidationReport ────────────────────────────────────────────────

    [Fact]
    public void WriteValidationReport_WritesReportFile()
    {
        var result = ValidationResult.Failure("test error");
        var outPath = _temp.File("report.txt");
        var sut = new EmassPackageValidator();

        sut.WriteValidationReport(result, outPath);

        File.Exists(outPath).Should().BeTrue();
        var content = File.ReadAllText(outPath);
        content.Should().Contain("eMASS Package Validation Report");
        content.Should().Contain("INVALID");
        content.Should().Contain("test error");
    }

    [Fact]
    public void WriteValidationReport_ValidResult_WritesValidStatus()
    {
        var result = new ValidationResult
        {
            IsValid = true,
            Errors = [],
            Warnings = [],
            PackageRoot = "/some/path",
            ValidatedAt = DateTimeOffset.Now,
            Metrics = new ValidationMetrics { RequiredDirectoriesChecked = 7 }
        };
        var outPath = _temp.File("report2.txt");
        var sut = new EmassPackageValidator();

        sut.WriteValidationReport(result, outPath);

        var content = File.ReadAllText(outPath);
        content.Should().Contain("VALID");
        content.Should().Contain("Required directories checked: 7");
    }

    [Fact]
    public void WriteValidationReport_WithWarnings_IncludesWarnSection()
    {
        var result = new ValidationResult
        {
            IsValid = true,
            Errors = [],
            Warnings = ["Some warning"],
            PackageRoot = "/path",
            ValidatedAt = DateTimeOffset.Now,
            Metrics = new ValidationMetrics()
        };
        var outPath = _temp.File("warn_report.txt");
        var sut = new EmassPackageValidator();

        sut.WriteValidationReport(result, outPath);

        File.ReadAllText(outPath).Should().Contain("WARNINGS");
        File.ReadAllText(outPath).Should().Contain("Some warning");
    }

    // ── ValidationResult.Failure factory ─────────────────────────────────────

    [Fact]
    public void ValidationResult_Failure_PopulatesExpectedFields()
    {
        var result = ValidationResult.Failure("something went wrong");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e == "something went wrong");
        result.ValidatedAt.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
    }
}

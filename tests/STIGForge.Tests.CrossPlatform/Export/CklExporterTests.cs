using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;
using VerifyControlResult = STIGForge.Verify.ControlResult;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class CklExporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal bundle root with a Verify/consolidated-results.json
    /// containing the given results.
    /// </summary>
    private string BuildBundle(IEnumerable<VerifyControlResult> results, string? packId = null, string? packName = null)
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
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

        if (packId != null || packName != null)
        {
            var manifestDir = Path.Combine(root, "Manifest");
            Directory.CreateDirectory(manifestDir);
            var manifest = new
            {
                run = new
                {
                    packId = packId ?? string.Empty,
                    packName = packName ?? string.Empty
                }
            };
            File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), JsonSerializer.Serialize(manifest));
        }

        return root;
    }

    private static VerifyControlResult PassResult(string vulnId = "V-001") =>
        new() { VulnId = vulnId, RuleId = "SV-001_rule", Title = "Test Rule", Severity = "medium", Status = "pass", Tool = "StigTool" };

    private static VerifyControlResult FailResult(string vulnId = "V-002") =>
        new() { VulnId = vulnId, RuleId = "SV-002_rule", Title = "Fail Rule", Severity = "high", Status = "fail", Tool = "StigTool", FindingDetails = "failed", Comments = "see notes" };

    // ── null/missing guard clauses ────────────────────────────────────────────

    [Fact]
    public void ExportCkl_NullRequest_ThrowsArgumentNullException()
    {
        Action act = () => CklExporter.ExportCkl(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExportCkl_NoBundleRoot_ThrowsArgumentException()
    {
        var request = new CklExportRequest { BundleRoot = string.Empty };
        Action act = () => CklExporter.ExportCkl(request);
        act.Should().Throw<ArgumentException>().WithMessage("*BundleRoot*");
    }

    [Fact]
    public void ExportCkl_EmptyBundleRootList_ThrowsArgumentException()
    {
        var request = new CklExportRequest
        {
            BundleRoot = string.Empty,
            BundleRoots = new List<string>()
        };
        Action act = () => CklExporter.ExportCkl(request);
        act.Should().Throw<ArgumentException>().WithMessage("*BundleRoot*");
    }

    // ── non-existent directory ────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_NonExistentBundleRoot_ThrowsDirectoryNotFoundException()
    {
        var request = new CklExportRequest { BundleRoot = Path.Combine(_temp.Path, "no-such-dir") };
        Action act = () => CklExporter.ExportCkl(request);
        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*not found*");
    }

    [Fact]
    public void ExportCkl_OneOfMultipleRootsNotFound_ThrowsDirectoryNotFoundException()
    {
        var goodRoot = BuildBundle([PassResult()]);
        var request = new CklExportRequest
        {
            BundleRoot = goodRoot,
            BundleRoots = new[] { goodRoot, Path.Combine(_temp.Path, "ghost") }
        };
        Action act = () => CklExporter.ExportCkl(request);
        act.Should().Throw<DirectoryNotFoundException>();
    }

    // ── no results → empty result ─────────────────────────────────────────────

    [Fact]
    public void ExportCkl_BundleWithNoVerifyDir_ReturnsEmptyResult()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var request = new CklExportRequest { BundleRoot = root };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(0);
        result.OutputPath.Should().BeEmpty();
        result.Message.Should().Contain("No verification results");
    }

    [Fact]
    public void ExportCkl_BundleWithEmptyVerifyDir_ReturnsEmptyResult()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Verify"));
        var request = new CklExportRequest { BundleRoot = root };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(0);
    }

    // ── CKL format ────────────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_DefaultFormat_CreatesCklFile()
    {
        var root = BuildBundle([PassResult(), FailResult()]);
        var outputDir = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            FileFormat = CklFileFormat.Ckl
        };

        var result = CklExporter.ExportCkl(request);

        result.OutputPath.Should().EndWith(".ckl");
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    [Fact]
    public void ExportCkl_CklFormat_OutputContainsChecklistXml()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "ckl-out-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir, FileFormat = CklFileFormat.Ckl };

        var result = CklExporter.ExportCkl(request);

        var doc = XDocument.Load(result.OutputPath);
        doc.Root!.Name.LocalName.Should().Be("CHECKLIST");
        doc.Descendants("VULN").Should().HaveCount(1);
    }

    [Fact]
    public void ExportCkl_CklFormat_IncludesAssetInfo()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "asset-out-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            HostName = "myhost",
            HostIp = "10.0.0.1",
            HostMac = "AA:BB:CC:DD:EE:FF"
        };

        var result = CklExporter.ExportCkl(request);

        var doc = XDocument.Load(result.OutputPath);
        doc.Descendants("HOST_NAME").First().Value.Should().Be("myhost");
        doc.Descendants("HOST_IP").First().Value.Should().Be("10.0.0.1");
        doc.Descendants("HOST_MAC").First().Value.Should().Be("AA:BB:CC:DD:EE:FF");
    }

    // ── CKLB format ───────────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_CklbFormat_CreatesCklbZipFile()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "cklb-out-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            FileFormat = CklFileFormat.Cklb
        };

        var result = CklExporter.ExportCkl(request);

        result.OutputPath.Should().EndWith(".cklb");
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    [Fact]
    public void ExportCkl_CklbFormat_ZipContainsEmbeddedCklFile()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "cklb-zip-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            FileFormat = CklFileFormat.Cklb
        };

        var result = CklExporter.ExportCkl(request);

        using var archive = ZipFile.OpenRead(result.OutputPath);
        archive.Entries.Should().Contain(e => e.Name.EndsWith(".ckl"));
    }

    // ── CSV output ────────────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_IncludeCsv_CreatesCsvFile()
    {
        var root = BuildBundle([PassResult(), FailResult()]);
        var outputDir = Path.Combine(_temp.Path, "csv-out-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            IncludeCsv = true
        };

        var result = CklExporter.ExportCkl(request);

        result.OutputPaths.Should().Contain(p => p.EndsWith(".csv"));
        var csvPath = result.OutputPaths.First(p => p.EndsWith(".csv"));
        File.Exists(csvPath).Should().BeTrue();
    }

    [Fact]
    public void ExportCkl_IncludeCsv_CsvContainsHeaderAndData()
    {
        var root = BuildBundle([FailResult("V-010")]);
        var outputDir = Path.Combine(_temp.Path, "csv-data-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir, IncludeCsv = true };

        var result = CklExporter.ExportCkl(request);

        var csvPath = result.OutputPaths.First(p => p.EndsWith(".csv"));
        var csv = File.ReadAllText(csvPath);
        csv.Should().StartWith("PackId,PackName");
        csv.Should().Contain("V-010");
    }

    [Fact]
    public void ExportCkl_ExcludeCsv_DoesNotCreateCsvFile()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "nocsv-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir, IncludeCsv = false };

        var result = CklExporter.ExportCkl(request);

        result.OutputPaths.Should().NotContain(p => p.EndsWith(".csv"));
    }

    // ── result metadata ───────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_WithResults_ReturnsCorrectControlCount()
    {
        var root = BuildBundle([PassResult("V-001"), FailResult("V-002"), new VerifyControlResult { VulnId = "V-003", Status = "notapplicable" }]);
        var outputDir = Path.Combine(_temp.Path, "count-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(3);
        result.Message.Should().Contain("complete");
    }

    [Fact]
    public void ExportCkl_WithResults_OutputPathsContainsMainFile()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "paths-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        result.OutputPaths.Should().ContainSingle();
        result.OutputPaths[0].Should().Be(result.OutputPath);
    }

    // ── custom file name ──────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_CustomFileName_UsesProvidedStem()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "fname-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            FileName = "my_checklist.ckl"
        };

        var result = CklExporter.ExportCkl(request);

        Path.GetFileNameWithoutExtension(result.OutputPath).Should().Be("my_checklist");
    }

    // ── default output directory ──────────────────────────────────────────────

    [Fact]
    public void ExportCkl_NoOutputDirectory_CreatesExportSubDir()
    {
        var root = BuildBundle([PassResult()]);
        var request = new CklExportRequest { BundleRoot = root };

        var result = CklExporter.ExportCkl(request);

        result.OutputPath.Should().Contain("Export");
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    // ── STIG ID override ──────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_WithStigIdOverride_UsesStigIdInXml()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "stigid-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            OutputDirectory = outputDir,
            StigId = "MY_CUSTOM_STIG_ID"
        };

        var result = CklExporter.ExportCkl(request);

        var xml = File.ReadAllText(result.OutputPath);
        xml.Should().Contain("MY_CUSTOM_STIG_ID");
    }

    // ── deduplication of same control in multiple reports ────────────────────

    [Fact]
    public void ExportCkl_DuplicateVulnIdAcrossReports_DeduplicatesEntries()
    {
        var root = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        var verifyDir = Path.Combine(root, "Verify");
        Directory.CreateDirectory(verifyDir);

        // Write two sub-directories each with a report containing V-001
        foreach (var sub in new[] { "scan1", "scan2" })
        {
            var subDir = Path.Combine(verifyDir, sub);
            Directory.CreateDirectory(subDir);
            var report = new
            {
                Tool = "StigTool",
                ToolVersion = "1.0",
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                OutputRoot = subDir,
                Results = new[] { new { VulnId = "V-001", RuleId = "SV-001", Status = "pass", Severity = "medium", Title = "Test" } }
            };
            File.WriteAllText(Path.Combine(subDir, "consolidated-results.json"), JsonSerializer.Serialize(report));
        }

        var outputDir = Path.Combine(_temp.Path, "dedup-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(1);
    }

    // ── multiple bundle roots ─────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_MultipleBundleRoots_CombinesResultsFromAll()
    {
        var root1 = BuildBundle([PassResult("V-001")], packId: "pack-A", packName: "Pack A");
        var root2 = BuildBundle([FailResult("V-002")], packId: "pack-B", packName: "Pack B");

        var outputDir = Path.Combine(_temp.Path, "multi-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root1,
            BundleRoots = new[] { root1, root2 },
            OutputDirectory = outputDir
        };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(2);
    }

    [Fact]
    public void ExportCkl_MultipleBundleRoots_DeduplicatesRootsFromBothProperties()
    {
        // Same root provided via BundleRoot and BundleRoots - should be deduped
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "dd2-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest
        {
            BundleRoot = root,
            BundleRoots = new[] { root },
            OutputDirectory = outputDir
        };

        var result = CklExporter.ExportCkl(request);

        result.ControlCount.Should().Be(1);
    }

    // ── pack metadata in XML ──────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_BundleWithManifest_UsesPackNameInXml()
    {
        var root = BuildBundle([PassResult()], packId: "RHEL9_STIG", packName: "RHEL 9 STIG");
        var outputDir = Path.Combine(_temp.Path, "meta-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        var xml = File.ReadAllText(result.OutputPath);
        xml.Should().Contain("RHEL 9 STIG");
    }

    // ── status mapping ────────────────────────────────────────────────────────

    [Fact]
    public void ExportCkl_PassResult_StatusElementContainsNotAFinding()
    {
        var root = BuildBundle([PassResult()]);
        var outputDir = Path.Combine(_temp.Path, "status-pass-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        var doc = XDocument.Load(result.OutputPath);
        var status = doc.Descendants("STATUS").First().Value;
        status.Should().Be("NotAFinding");
    }

    [Fact]
    public void ExportCkl_FailResult_StatusElementContainsOpen()
    {
        var root = BuildBundle([FailResult()]);
        var outputDir = Path.Combine(_temp.Path, "status-fail-" + Guid.NewGuid().ToString("N"));
        var request = new CklExportRequest { BundleRoot = root, OutputDirectory = outputDir };

        var result = CklExporter.ExportCkl(request);

        var doc = XDocument.Load(result.OutputPath);
        var status = doc.Descendants("STATUS").First().Value;
        status.Should().Be("Open");
    }
}

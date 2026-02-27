using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using System.IO.Compression;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class CklExporterTests : IDisposable
{
  private readonly string _tempDir;

  public CklExporterTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_ckl_test_" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void ExportCkl_CreatesValidXml()
  {
    var bundleRoot = CreateTestBundle();
    var outputDir = Path.Combine(_tempDir, "output");

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir,
      HostName = "TESTHOST",
      StigId = "TEST_STIG_001"
    });

    result.OutputPath.Should().NotBeNullOrWhiteSpace();
    result.ControlCount.Should().BeGreaterThan(0);
    File.Exists(result.OutputPath).Should().BeTrue();

    // Validate XML structure
    var doc = XDocument.Load(result.OutputPath);
    doc.Root!.Name.LocalName.Should().Be("CHECKLIST");
    doc.Root.Element("ASSET").Should().NotBeNull();
    doc.Root.Element("ASSET")!.Element("HOST_NAME")!.Value.Should().Be("TESTHOST");
    doc.Descendants("VULN").Should().NotBeEmpty();
  }

  [Fact]
  public void ExportCkl_MapsCklStatuses()
  {
    var bundleRoot = CreateTestBundle();
    var outputDir = Path.Combine(_tempDir, "output_status");

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir
    });

    var doc = XDocument.Load(result.OutputPath);
    var statuses = doc.Descendants("STATUS").Select(e => e.Value).ToList();
    statuses.Should().Contain("NotAFinding");
    statuses.Should().Contain("Open");
  }

  [Fact]
  public void ExportCkl_ReturnsZeroCountForEmptyBundle()
  {
    var emptyBundle = Path.Combine(_tempDir, "empty_bundle");
    Directory.CreateDirectory(emptyBundle);

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = emptyBundle });
    result.ControlCount.Should().Be(0);
  }

  [Fact]
  public void ExportCkl_ThrowsForMissingBundle()
  {
    var act = () => CklExporter.ExportCkl(new CklExportRequest { BundleRoot = Path.Combine(_tempDir, "nonexistent") });
    act.Should().Throw<DirectoryNotFoundException>();
  }

  [Fact]
  public void ExportCkl_WithCklbFormat_CreatesCklbArchive()
  {
    var bundleRoot = CreateTestBundle("bundle-cklb");
    var outputDir = Path.Combine(_tempDir, "output-cklb");

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir,
      FileFormat = CklFileFormat.Cklb
    });

    result.OutputPath.Should().EndWith(".cklb");
    File.Exists(result.OutputPath).Should().BeTrue();

    using var archive = ZipFile.OpenRead(result.OutputPath);
    archive.Entries.Should().Contain(e => e.FullName.EndsWith(".ckl", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void ExportCkl_WithCklbFormat_AllowsOverwriteOnRepeatExport()
  {
    var bundleRoot = CreateTestBundle("bundle-cklb-repeat");
    var outputDir = Path.Combine(_tempDir, "output-cklb-repeat");

    var request = new CklExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir,
      FileFormat = CklFileFormat.Cklb
    };

    var first = CklExporter.ExportCkl(request);
    var second = CklExporter.ExportCkl(request);

    second.OutputPath.Should().Be(first.OutputPath);
    File.Exists(second.OutputPath).Should().BeTrue();
  }

  [Fact]
  public void ExportCkl_WithCsvEnabled_WritesCsvCompanion()
  {
    var bundleRoot = CreateTestBundle("bundle-csv");
    var outputDir = Path.Combine(_tempDir, "output-csv");

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir,
      IncludeCsv = true
    });

    result.OutputPaths.Should().Contain(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
    var csvPath = result.OutputPaths.First(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
    File.ReadAllText(csvPath).Should().Contain("PackId,PackName,VulnId");
  }

  [Fact]
  public void ExportCkl_WithMultipleBundleRoots_CombinesIntoSingleChecklist()
  {
    var bundleOne = CreateTestBundle("bundle-one", new List<ControlResult>
    {
      new() { VulnId = "V-111111", RuleId = "SV-111111r1_rule", Title = "Rule A", Severity = "high", Status = "Open", Tool = "SCAP" }
    });
    var bundleTwo = CreateTestBundle("bundle-two", new List<ControlResult>
    {
      new() { VulnId = "V-222222", RuleId = "SV-222222r1_rule", Title = "Rule B", Severity = "medium", Status = "NotAFinding", Tool = "SCAP" }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest
    {
      BundleRoots = new[] { bundleOne, bundleTwo }
    });

    result.ControlCount.Should().Be(2);
    var doc = XDocument.Load(result.OutputPath);
    doc.Descendants("iSTIG").Count().Should().Be(2);
  }

  [Fact]
  public void ExportCkl_DeduplicatesControls_PreservesMergedDetailsAndComments()
  {
    var bundleRoot = CreateTestBundle("bundle-dedup", new List<ControlResult>
    {
      new() { VulnId = "V-20001", RuleId = "SV-20001r1_rule", Title = "Merged Control", Severity = "high", Status = "Open", FindingDetails = "Initial detail", Comments = "Initial comment", Tool = "Test" }
    });

    AddVerifyReport(bundleRoot, "run2", new List<ControlResult>
    {
      new() { VulnId = "V-20001", RuleId = "SV-20001r1_rule", Title = "Merged Control", Severity = "high", Status = "NotAFinding", FindingDetails = "Duplicate detail", Comments = "Duplicate comment", Tool = "Test" }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = bundleRoot });
    result.ControlCount.Should().Be(1);

    var doc = XDocument.Load(result.OutputPath);
    var vuln = doc.Descendants("VULN").Single();
    vuln.Element("FINDING_DETAILS")!.Value.Should().Contain("Initial detail");
    vuln.Element("FINDING_DETAILS")!.Value.Should().Contain("Duplicate detail");
    vuln.Element("COMMENTS")!.Value.Should().Contain("Initial comment");
    vuln.Element("COMMENTS")!.Value.Should().Contain("Duplicate comment");
  }

  [Fact]
  public void LoadResultsForBundle_StatusWinnerAlignsMetadata()
  {
    var bundleRoot = CreateTestBundle("bundle-status-metadata", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-21001",
        RuleId = "SV-21001r1_rule",
        Title = "Metadata Merge",
        Severity = "medium",
        Status = "NotAFinding",
        Tool = "BaselineTool",
        SourceFile = "baseline.xml",
        VerifiedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
      }
    });

    AddVerifyReport(bundleRoot, "run2", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-21001",
        RuleId = "SV-21001r1_rule",
        Title = "Metadata Merge",
        Severity = "medium",
        Status = "Open",
        Tool = "WinnerTool",
        SourceFile = "winner.xml",
        VerifiedAt = DateTimeOffset.Parse("2026-02-02T00:00:00Z")
      }
    });

    var method = typeof(CklExporter).GetMethod("LoadResultsForBundle", BindingFlags.NonPublic | BindingFlags.Static);
    method.Should().NotBeNull();
    var resultSet = method!.Invoke(null, new object[] { bundleRoot })!;
    var resultsProp = resultSet.GetType().GetProperty("Results");
    var results = (IReadOnlyList<ControlResult>)resultsProp!.GetValue(resultSet)!;
    var merged = results.Single();

    merged.Status.Should().Be("Open");
    merged.Tool.Should().Be("WinnerTool");
    merged.SourceFile.Should().Be("winner.xml");
    merged.VerifiedAt.Should().Be(DateTimeOffset.Parse("2026-02-02T00:00:00Z"));
  }

  [Fact]
  public void LoadResultsForBundle_StatusWinnerPrefersLatestVerifiedAt()
  {
    var bundleRoot = CreateTestBundle("bundle-status-latest", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-22010",
        RuleId = "SV-22010r1_rule",
        Title = "Latest Verification",
        Severity = "medium",
        Status = "Open",
        Tool = "BaselineTool",
        SourceFile = "baseline.xml",
        VerifiedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
      }
    });

    AddVerifyReport(bundleRoot, "run2", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-22010",
        RuleId = "SV-22010r1_rule",
        Title = "Latest Verification",
        Severity = "medium",
        Status = "NotAFinding",
        Tool = "LaterTool",
        SourceFile = "later.xml",
        VerifiedAt = DateTimeOffset.Parse("2026-02-03T00:00:00Z")
      }
    });

    var method = typeof(CklExporter).GetMethod("LoadResultsForBundle", BindingFlags.NonPublic | BindingFlags.Static);
    method.Should().NotBeNull();
    var resultSet = method!.Invoke(null, new object[] { bundleRoot })!;
    var resultsProp = resultSet.GetType().GetProperty("Results");
    var results = (IReadOnlyList<ControlResult>)resultsProp!.GetValue(resultSet)!;
    var merged = results.Single();

    merged.Status.Should().Be("NotAFinding");
    merged.Tool.Should().Be("LaterTool");
    merged.SourceFile.Should().Be("later.xml");
    merged.VerifiedAt.Should().Be(DateTimeOffset.Parse("2026-02-03T00:00:00Z"));
  }

  [Fact]
  public void ExportCkl_DoesNotDuplicateRepeatedDetailsAndComments()
  {
    const string repeatedDetail = "Shared detail";
    const string repeatedComment = "Shared comment";
    var bundleRoot = CreateTestBundle("bundle-repeated-text", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-22001",
        RuleId = "SV-22001r1_rule",
        Title = "Repeated Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "Test"
      }
    });

    AddVerifyReport(bundleRoot, "run2", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-22001",
        RuleId = "SV-22001r1_rule",
        Title = "Repeated Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "Test"
      }
    });

    AddVerifyReport(bundleRoot, "run3", new List<ControlResult>
    {
      new()
      {
        VulnId = "V-22001",
        RuleId = "SV-22001r1_rule",
        Title = "Repeated Text",
        Severity = "medium",
        Status = "Open",
        FindingDetails = repeatedDetail,
        Comments = repeatedComment,
        Tool = "Test"
      }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = bundleRoot });
    var doc = XDocument.Load(result.OutputPath);
    var vuln = doc.Descendants("VULN").Single();

    vuln.Element("FINDING_DETAILS")!.Value.Should().Be(repeatedDetail);
    vuln.Element("COMMENTS")!.Value.Should().Be(repeatedComment);
  }

  [Fact]
  public void ExportCkl_UnknownStatusDefaultsToNotReviewed()
  {
    var bundleRoot = CreateTestBundle("bundle-unknown-status", new List<ControlResult>
    {
      new() { VulnId = "V-30001", RuleId = "SV-30001r1_rule", Title = "Mystery status", Severity = "low", Status = "WeirdState", Tool = "Test" }
    });

    var result = CklExporter.ExportCkl(new CklExportRequest { BundleRoot = bundleRoot });
    var doc = XDocument.Load(result.OutputPath);
    doc.Descendants("STATUS").Single().Value.Should().Be("Not_Reviewed");
  }

  private string CreateTestBundle(string folderName = "bundle", IReadOnlyList<ControlResult>? controls = null)
  {
    var bundleRoot = Path.Combine(_tempDir, folderName);
    var verifyDir = Path.Combine(bundleRoot, "Verify", "run1");
    var manifestDir = Path.Combine(bundleRoot, "Manifest");
    Directory.CreateDirectory(verifyDir);
    Directory.CreateDirectory(manifestDir);

    var report = new VerifyReport
    {
      Tool = "Test",
      Results = controls ?? new List<ControlResult>
      {
        new() { VulnId = "V-100001", RuleId = "SV-100001r1_rule", Title = "Test Rule 1", Severity = "high", Status = "NotAFinding", Tool = "SCAP" },
        new() { VulnId = "V-100002", RuleId = "SV-100002r1_rule", Title = "Test Rule 2", Severity = "medium", Status = "Open", Tool = "SCAP", Comments = "Needs fix" }
      }
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    var manifest = new
    {
      run = new
      {
        packId = folderName + "-pack",
        packName = "Pack " + folderName,
        profileName = "Test Profile"
      }
    };
    File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), JsonSerializer.Serialize(manifest));

    return bundleRoot;
  }

  private static void AddVerifyReport(string bundleRoot, string runName, IReadOnlyList<ControlResult> results)
  {
    var reportDir = Path.Combine(bundleRoot, "Verify", runName);
    Directory.CreateDirectory(reportDir);

    var report = new VerifyReport
    {
      Tool = "Test",
      Results = results
    };

    File.WriteAllText(Path.Combine(reportDir, "consolidated-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
  }
}

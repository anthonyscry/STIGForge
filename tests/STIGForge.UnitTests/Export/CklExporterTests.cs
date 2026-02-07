using System.Text.Json;
using System.Xml.Linq;
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

  private string CreateTestBundle()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    var verifyDir = Path.Combine(bundleRoot, "Verify", "run1");
    Directory.CreateDirectory(verifyDir);

    var report = new VerifyReport
    {
      Tool = "Test",
      Results = new List<ControlResult>
      {
        new() { VulnId = "V-100001", RuleId = "SV-100001r1_rule", Title = "Test Rule 1", Severity = "high", Status = "NotAFinding", Tool = "SCAP" },
        new() { VulnId = "V-100002", RuleId = "SV-100002r1_rule", Title = "Test Rule 2", Severity = "medium", Status = "Open", Tool = "SCAP", Comments = "Needs fix" }
      }
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    return bundleRoot;
  }
}

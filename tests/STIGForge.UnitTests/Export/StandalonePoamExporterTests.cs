using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class StandalonePoamExporterTests : IDisposable
{
  private readonly string _tempDir;

  public StandalonePoamExporterTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_poam_test_" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void ExportPoam_GeneratesPoamFiles()
  {
    var bundleRoot = CreateTestBundle();
    var outputDir = Path.Combine(_tempDir, "poam_output");

    var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir,
      SystemName = "TestSystem"
    });

    result.ItemCount.Should().BeGreaterThan(0);
    result.OutputDirectory.Should().Be(outputDir);
    File.Exists(Path.Combine(outputDir, "poam.json")).Should().BeTrue();
    File.Exists(Path.Combine(outputDir, "poam.csv")).Should().BeTrue();
    File.Exists(Path.Combine(outputDir, "poam_summary.txt")).Should().BeTrue();
  }

  [Fact]
  public void ExportPoam_OnlyIncludesOpenFindings()
  {
    var bundleRoot = CreateTestBundle();
    var outputDir = Path.Combine(_tempDir, "poam_filter");

    var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest
    {
      BundleRoot = bundleRoot,
      OutputDirectory = outputDir
    });

    // Bundle has 1 pass and 2 fail/open — POAM should only include the fail/open
    result.ItemCount.Should().Be(2);
  }

  [Fact]
  public void ExportPoam_ReturnsZeroForEmptyBundle()
  {
    var emptyBundle = Path.Combine(_tempDir, "empty_bundle");
    Directory.CreateDirectory(emptyBundle);

    var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest { BundleRoot = emptyBundle });
    result.ItemCount.Should().Be(0);
  }

  [Fact]
  public void ExportPoam_ThrowsForMissingBundle()
  {
    var act = () => StandalonePoamExporter.ExportPoam(new PoamExportRequest { BundleRoot = Path.Combine(_tempDir, "missing") });
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
        new() { VulnId = "V-200001", RuleId = "SV-200001r1_rule", Title = "Pass Rule", Severity = "medium", Status = "NotAFinding", Tool = "SCAP" },
        new() { VulnId = "V-200002", RuleId = "SV-200002r1_rule", Title = "Fail Rule CAT I", Severity = "high", Status = "Open", Tool = "SCAP" },
        new() { VulnId = "V-200003", RuleId = "SV-200003r1_rule", Title = "Fail Rule CAT III", Severity = "low", Status = "Open", Tool = "SCAP" }
      }
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    return bundleRoot;
  }
}

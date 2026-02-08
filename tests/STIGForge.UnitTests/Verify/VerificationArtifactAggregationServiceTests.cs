using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class VerificationArtifactAggregationServiceTests : IDisposable
{
  private readonly string _tempDir;

  public VerificationArtifactAggregationServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-aggregate-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void WriteCoverageArtifacts_WritesExpectedArtifacts()
  {
    var evalRoot = Path.Combine(_tempDir, "Verify", "Evaluate-STIG");
    var scapRoot = Path.Combine(_tempDir, "Verify", "SCAP");
    var reportsRoot = Path.Combine(_tempDir, "Reports");

    WriteConsolidatedReport(evalRoot, "Evaluate-STIG", new[]
    {
      ("V-100", "SV-100", "NotAFinding", ""),
      ("V-101", "SV-101", "Open", "")
    });

    WriteConsolidatedReport(scapRoot, "SCAP", new[]
    {
      ("V-100", "SV-100", "Pass", ""),
      ("V-102", "SV-102", "Fail", "")
    });

    var service = new VerificationArtifactAggregationService();
    var result = service.WriteCoverageArtifacts(reportsRoot, new[]
    {
      new VerificationCoverageInput { ToolLabel = "Evaluate-STIG", ReportPath = evalRoot },
      new VerificationCoverageInput { ToolLabel = "SCAP", ReportPath = scapRoot }
    });

    result.InputCount.Should().Be(2);
    result.TotalResultCount.Should().Be(4);

    File.Exists(result.CoverageByToolCsvPath).Should().BeTrue();
    File.Exists(result.CoverageByToolJsonPath).Should().BeTrue();
    File.Exists(result.ControlSourcesCsvPath).Should().BeTrue();
    File.Exists(result.CoverageOverlapCsvPath).Should().BeTrue();
    File.Exists(result.CoverageOverlapJsonPath).Should().BeTrue();

    File.ReadAllText(result.CoverageOverlapCsvPath).Should().Contain("Evaluate-STIG|SCAP");
    File.ReadAllText(result.CoverageByToolCsvPath).Should().Contain("Evaluate-STIG");
    File.ReadAllText(result.CoverageByToolCsvPath).Should().Contain("SCAP");
  }

  [Fact]
  public void WriteCoverageArtifacts_ResolvesDirectJsonPath()
  {
    var reportFile = Path.Combine(_tempDir, "single", "consolidated-results.json");
    var reportsRoot = Path.Combine(_tempDir, "Reports");

    WriteConsolidatedReportFile(reportFile, "Evaluate-STIG", new[]
    {
      ("V-200", "SV-200", "NotAFinding", "")
    });

    var service = new VerificationArtifactAggregationService();
    var result = service.WriteCoverageArtifacts(reportsRoot, new[]
    {
      new VerificationCoverageInput { ToolLabel = "Evaluate-STIG", ReportPath = reportFile }
    });

    result.TotalResultCount.Should().Be(1);
    File.ReadAllText(result.ControlSourcesCsvPath).Should().Contain("RULE:SV-200");
  }

  [Fact]
  public void WriteCoverageArtifacts_MissingReport_Throws()
  {
    var reportsRoot = Path.Combine(_tempDir, "Reports");
    var missing = Path.Combine(_tempDir, "Verify", "missing");

    var service = new VerificationArtifactAggregationService();
    Action act = () => service.WriteCoverageArtifacts(reportsRoot, new[]
    {
      new VerificationCoverageInput { ToolLabel = "SCAP", ReportPath = missing }
    });

    act.Should().Throw<FileNotFoundException>();
  }

  private static void WriteConsolidatedReport(string outputRoot, string tool, IReadOnlyList<(string VulnId, string RuleId, string Status, string Tool)> rows)
  {
    var reportPath = Path.Combine(outputRoot, "consolidated-results.json");
    WriteConsolidatedReportFile(reportPath, tool, rows);
  }

  private static void WriteConsolidatedReportFile(string reportPath, string tool, IReadOnlyList<(string VulnId, string RuleId, string Status, string Tool)> rows)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

    var report = new
    {
      tool,
      toolVersion = "1.0",
      startedAt = DateTimeOffset.Parse("2026-02-02T00:00:00Z"),
      finishedAt = DateTimeOffset.Parse("2026-02-02T00:05:00Z"),
      outputRoot = Path.GetDirectoryName(reportPath),
      results = rows.Select(r => new
      {
        vulnId = r.VulnId,
        ruleId = r.RuleId,
        title = "Control " + r.RuleId,
        severity = "medium",
        status = r.Status,
        tool = r.Tool,
        sourceFile = reportPath,
        verifiedAt = DateTimeOffset.Parse("2026-02-02T00:05:00Z")
      }).ToList()
    };

    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      WriteIndented = true
    }), Encoding.UTF8);
  }
}

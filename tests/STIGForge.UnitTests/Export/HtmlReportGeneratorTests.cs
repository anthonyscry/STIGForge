using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Export;
using STIGForge.Verify;
using VerifyNs = STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class HtmlReportGeneratorTests : IDisposable
{
  private readonly string _tempDir;

  public HtmlReportGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_report_test_" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void BuildReportData_CalculatesCorrectCounts()
  {
    var bundleRoot = CreateTestBundle();
    var data = HtmlReportGenerator.BuildReportData(bundleRoot);

    data.PassCount.Should().Be(1);
    data.FailCount.Should().Be(2);
    data.TotalControls.Should().Be(3);
    // 1 pass / (1 pass + 2 fail) = 33.33%
    data.OverallCompliancePercent.Should().BeApproximately(33.33, 0.01);
  }

  [Fact]
  public void BuildReportData_GroupsBySeverity()
  {
    var bundleRoot = CreateTestBundle();
    var data = HtmlReportGenerator.BuildReportData(bundleRoot);

    // high: V-002 (fail) => CatI
    data.Severity.CatIFail.Should().Be(1);
    data.Severity.CatITotal.Should().Be(1);
    // medium: V-001 (pass) => CatII
    data.Severity.CatIIPass.Should().Be(1);
    data.Severity.CatIITotal.Should().Be(1);
    // low: V-003 (fail) => CatIII
    data.Severity.CatIIIFail.Should().Be(1);
    data.Severity.CatIIITotal.Should().Be(1);
  }

  [Fact]
  public void BuildReportData_SortsFindingsBySeverity()
  {
    var bundleRoot = CreateTestBundle();
    var data = HtmlReportGenerator.BuildReportData(bundleRoot);

    // Open findings should be sorted: high first, then low
    data.OpenFindings.Should().HaveCount(2);
    data.OpenFindings[0].Severity.Should().Be("high");
    data.OpenFindings[1].Severity.Should().Be("low");
  }

  [Fact]
  public void BuildReportData_GroupsByBenchmarkId()
  {
    var bundleRoot = CreateTestBundleWithBenchmarks();
    var data = HtmlReportGenerator.BuildReportData(bundleRoot);

    data.StigBreakdowns.Should().HaveCount(2);
    var win11 = data.StigBreakdowns.FirstOrDefault(s => s.BenchmarkId == "Windows_11_STIG");
    win11.Should().NotBeNull();
    win11!.PassCount.Should().Be(1);
    win11.FailCount.Should().Be(1);
  }

  [Fact]
  public void RenderHtml_ContainsSystemName()
  {
    var data = MakeReportData("TestSystem-42");
    var html = HtmlReportGenerator.RenderHtml(data);

    html.Should().Contain("TestSystem-42");
  }

  [Fact]
  public void RenderHtml_ContainsCompliancePercent()
  {
    var data = MakeReportData();
    data.OverallCompliancePercent = 87.5;
    var html = HtmlReportGenerator.RenderHtml(data);

    html.Should().Contain("87.5");
  }

  [Fact]
  public void RenderHtml_ContainsSvgDonut()
  {
    var data = MakeReportData();
    data.PassCount = 8;
    data.FailCount = 2;
    var html = HtmlReportGenerator.RenderHtml(data);

    html.Should().Contain("donut-chart");
    html.Should().Contain("<circle");
    html.Should().Contain("stroke-dasharray");
  }

  [Fact]
  public void RenderHtml_NoExternalUrls()
  {
    var data = MakeReportData();
    data.OpenFindings = new List<OpenFinding>
    {
      new() { VulnId = "V-001", Severity = "high", Status = "Fail", Title = "Test" }
    };
    var html = HtmlReportGenerator.RenderHtml(data);

    // Should not contain any http:// or https:// URLs
    var urlPattern = new Regex(@"(src|href|url)\s*=\s*[""']https?://", RegexOptions.IgnoreCase);
    urlPattern.IsMatch(html).Should().BeFalse("report must be fully offline — no external URLs");
  }

  [Fact]
  public void RenderHtml_ExecutiveAudience_ExcludesDetails()
  {
    var data = MakeReportData();
    data.Audience = "executive";
    data.OpenFindings = Enumerable.Range(1, 25).Select(i => new OpenFinding
    {
      VulnId = $"V-{i:D6}",
      Severity = "medium",
      Status = "Fail",
      Title = "Finding " + i,
      Tool = "SCAP",
      SourceFile = "/path/to/file.xml"
    }).ToList();

    var html = HtmlReportGenerator.RenderHtml(data);

    // Executive audience with >20 findings should not show the findings table
    html.Should().NotContain("V-000001");
  }

  private string CreateTestBundle()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    var verifyDir = Path.Combine(bundleRoot, "Verify", "run1");
    Directory.CreateDirectory(verifyDir);

    var report = new VerifyReport
    {
      Tool = "Test",
      Results = new List<VerifyNs.ControlResult>
      {
        new() { VulnId = "V-001", RuleId = "SV-001r1_rule", Title = "Pass Rule", Severity = "medium", Status = "NotAFinding", Tool = "SCAP" },
        new() { VulnId = "V-002", RuleId = "SV-002r1_rule", Title = "Fail CAT I", Severity = "high", Status = "Open", Tool = "SCAP" },
        new() { VulnId = "V-003", RuleId = "SV-003r1_rule", Title = "Fail CAT III", Severity = "low", Status = "Open", Tool = "SCAP" }
      }
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    return bundleRoot;
  }

  private string CreateTestBundleWithBenchmarks()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle_bench");
    var verifyDir = Path.Combine(bundleRoot, "Verify", "run1");
    Directory.CreateDirectory(verifyDir);

    var report = new VerifyReport
    {
      Tool = "Test",
      Results = new List<VerifyNs.ControlResult>
      {
        new() { VulnId = "V-001", Title = "Win11 Pass", Severity = "medium", Status = "NotAFinding", Tool = "SCAP", BenchmarkId = "Windows_11_STIG" },
        new() { VulnId = "V-002", Title = "Win11 Fail", Severity = "high", Status = "Open", Tool = "SCAP", BenchmarkId = "Windows_11_STIG" },
        new() { VulnId = "V-003", Title = "Edge Pass", Severity = "medium", Status = "NotAFinding", Tool = "SCAP", BenchmarkId = "Edge_STIG" },
        new() { VulnId = "V-004", Title = "No Benchmark", Severity = "low", Status = "Open", Tool = "SCAP" }
      }
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    return bundleRoot;
  }

  private static ExecutiveReportData MakeReportData(string systemName = "TestSystem")
  {
    return new ExecutiveReportData
    {
      SystemName = systemName,
      BundleId = "test-bundle-001",
      GeneratedAt = DateTimeOffset.UtcNow,
      OverallCompliancePercent = 85.0,
      TotalControls = 100,
      PassCount = 85,
      FailCount = 12,
      ErrorCount = 3,
      NotApplicableCount = 0,
      NotReviewedCount = 0,
      Severity = new SeverityBreakdown
      {
        CatIPass = 10, CatIFail = 2, CatITotal = 12,
        CatIIPass = 50, CatIIFail = 8, CatIITotal = 58,
        CatIIIPass = 25, CatIIIFail = 5, CatIIITotal = 30
      },
      TrendData = [],
      OpenFindings = [],
      PoamAges = new PoamAgeSummary(),
      StigBreakdowns = [],
      Audience = "admin"
    };
  }
}

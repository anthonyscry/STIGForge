using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;
using CoreModels = STIGForge.Core.Models;

namespace STIGForge.UnitTests.Export;

public sealed class ComplianceDiffGeneratorTests : IDisposable
{
  private readonly string _tempDir;

  public ComplianceDiffGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_diff_test_" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public void ComputeDiff_IdenticalResults_NoDifferences()
  {
    var results = MakeResults(("V-001", "Pass", "high"), ("V-002", "Fail", "medium"));

    var diff = ComplianceDiffGenerator.ComputeDiff(results, results, "base", "target");

    diff.Regressions.Should().BeEmpty();
    diff.Remediations.Should().BeEmpty();
    diff.Added.Should().BeEmpty();
    diff.Removed.Should().BeEmpty();
    diff.DeltaPercent.Should().Be(0);
  }

  [Fact]
  public void ComputeDiff_PassToFail_DetectsRegression()
  {
    var baseline = MakeResults(("V-001", "Pass", "high"));
    var target = MakeResults(("V-001", "Fail", "high"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.Regressions.Should().HaveCount(1);
    diff.Regressions[0].VulnId.Should().Be("V-001");
    diff.Regressions[0].OldStatus.Should().Be("Pass");
    diff.Regressions[0].NewStatus.Should().Be("Fail");
    diff.Remediations.Should().BeEmpty();
  }

  [Fact]
  public void ComputeDiff_FailToPass_DetectsRemediation()
  {
    var baseline = MakeResults(("V-001", "Fail", "medium"));
    var target = MakeResults(("V-001", "Pass", "medium"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.Remediations.Should().HaveCount(1);
    diff.Remediations[0].VulnId.Should().Be("V-001");
    diff.Regressions.Should().BeEmpty();
  }

  [Fact]
  public void ComputeDiff_NewControlInTarget_MarkedAsAdded()
  {
    var baseline = MakeResults(("V-001", "Pass", "medium"));
    var target = MakeResults(("V-001", "Pass", "medium"), ("V-002", "Fail", "low"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.Added.Should().HaveCount(1);
    diff.Added[0].VulnId.Should().Be("V-002");
    diff.Added[0].NewStatus.Should().Be("Fail");
  }

  [Fact]
  public void ComputeDiff_ControlMissingInTarget_MarkedAsRemoved()
  {
    var baseline = MakeResults(("V-001", "Pass", "medium"), ("V-002", "Fail", "low"));
    var target = MakeResults(("V-001", "Pass", "medium"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.Removed.Should().HaveCount(1);
    diff.Removed[0].VulnId.Should().Be("V-002");
  }

  [Fact]
  public void ComputeDiff_EmptyBaseline_AllControlsAdded()
  {
    var baseline = MakeResults();
    var target = MakeResults(("V-001", "Pass", "high"), ("V-002", "Fail", "medium"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.Added.Should().HaveCount(2);
    diff.Regressions.Should().BeEmpty();
    diff.Remediations.Should().BeEmpty();
    diff.Removed.Should().BeEmpty();
  }

  [Fact]
  public void ComputeDiff_SeveritySummary_CountsCorrectly()
  {
    var baseline = MakeResults(
      ("V-001", "Pass", "high"),
      ("V-002", "Fail", "medium"),
      ("V-003", "Pass", "low"));
    var target = MakeResults(
      ("V-001", "Fail", "high"),   // regression CAT I
      ("V-002", "Pass", "medium"), // remediation CAT II
      ("V-003", "Fail", "low"));   // regression CAT III

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.SeveritySummary.CatIRegressions.Should().Be(1);
    diff.SeveritySummary.CatIIRemediations.Should().Be(1);
    diff.SeveritySummary.CatIIIRegressions.Should().Be(1);
    diff.SeveritySummary.NetChange.Should().Be(-1); // 1 remediation - 2 regressions
  }

  [Fact]
  public void ComputeDiff_CompliancePercentDelta_CalculatedCorrectly()
  {
    // Baseline: 1 pass, 1 fail = 50%
    var baseline = MakeResults(("V-001", "Pass", "medium"), ("V-002", "Fail", "medium"));
    // Target: 2 pass, 0 fail = 100%
    var target = MakeResults(("V-001", "Pass", "medium"), ("V-002", "Pass", "medium"));

    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    diff.BaselineCompliancePercent.Should().Be(50.0);
    diff.TargetCompliancePercent.Should().Be(100.0);
    diff.DeltaPercent.Should().Be(50.0);
  }

  [Fact]
  public void WriteDiffCsv_CreatesValidFile()
  {
    var baseline = MakeResults(("V-001", "Pass", "high"));
    var target = MakeResults(("V-001", "Fail", "high"));
    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    var csvPath = Path.Combine(_tempDir, "diff.csv");
    ComplianceDiffGenerator.WriteDiffCsv(diff, csvPath);

    File.Exists(csvPath).Should().BeTrue();
    var lines = File.ReadAllLines(csvPath);
    lines.Length.Should().Be(2); // header + 1 regression
    lines[0].Should().StartWith("ChangeType,");
    lines[1].Should().StartWith("Regression,");
  }

  [Fact]
  public void WriteDiffJson_RoundTrips()
  {
    var baseline = MakeResults(("V-001", "Pass", "high"), ("V-002", "Fail", "medium"));
    var target = MakeResults(("V-001", "Fail", "high"), ("V-002", "Pass", "medium"));
    var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

    var jsonPath = Path.Combine(_tempDir, "diff.json");
    ComplianceDiffGenerator.WriteDiffJson(diff, jsonPath);

    File.Exists(jsonPath).Should().BeTrue();
    var json = File.ReadAllText(jsonPath);
    var deserialized = JsonSerializer.Deserialize<CoreModels.ComplianceDiff>(json,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    deserialized.Should().NotBeNull();
    deserialized!.Regressions.Should().HaveCount(1);
    deserialized.Remediations.Should().HaveCount(1);
  }

  private static List<NormalizedVerifyResult> MakeResults(params (string vulnId, string status, string severity)[] items)
  {
    return items.Select(i => new NormalizedVerifyResult
    {
      ControlId = i.vulnId,
      VulnId = i.vulnId,
      RuleId = "SV-" + i.vulnId.Replace("V-", "") + "r1_rule",
      Title = "Test Rule " + i.vulnId,
      Severity = i.severity,
      Status = MapStatus(i.status),
      Tool = "TestTool",
      VerifiedAt = DateTimeOffset.UtcNow
    }).ToList();
  }

  private static VerifyStatus MapStatus(string status) =>
    status switch
    {
      "Pass" => VerifyStatus.Pass,
      "Fail" => VerifyStatus.Fail,
      "Error" => VerifyStatus.Error,
      "NotApplicable" => VerifyStatus.NotApplicable,
      "NotReviewed" => VerifyStatus.NotReviewed,
      _ => VerifyStatus.Unknown
    };
}

using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public sealed class BundleMissionSummaryServiceTests : IDisposable
{
  private readonly string _bundleRoot;

  public BundleMissionSummaryServiceTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-bundle-summary-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manifest"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manual"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Verify", "ToolA"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Verify", "ToolB"));
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  [Fact]
  public void LoadSummary_AggregatesManifestVerifyAndManualMetrics()
  {
    WriteManifest("Q1_2026", "Classified Safe");
    WriteControls(
      MakeControl("SV-001_rule", "V-001", isManual: true),
      MakeControl("SV-002_rule", "V-002", isManual: true),
      MakeControl("SV-003_rule", "V-003", isManual: false));

    var manualAnswers = new ManualAnswerService();
    manualAnswers.SaveAnswer(_bundleRoot, new ManualAnswer { RuleId = "SV-001_rule", VulnId = "V-001", Status = "Pass" });
    manualAnswers.SaveAnswer(_bundleRoot, new ManualAnswer { RuleId = "SV-002_rule", VulnId = "V-002", Status = "Fail" });

    WriteVerifyReport(Path.Combine(_bundleRoot, "Verify", "ToolA", "consolidated-results.json"), "NotAFinding", "Open");
    WriteVerifyReport(Path.Combine(_bundleRoot, "Verify", "ToolB", "consolidated-results.json"), "not_applicable", "Fail", "NotReviewed");

    var service = new BundleMissionSummaryService(manualAnswers);
    var summary = service.LoadSummary(_bundleRoot);

    summary.PackName.Should().Be("Q1_2026");
    summary.ProfileName.Should().Be("Classified Safe");

    summary.TotalControls.Should().Be(3);
    summary.AutoControls.Should().Be(1);
    summary.ManualControls.Should().Be(2);

    summary.Verify.ReportCount.Should().Be(2);
    summary.Verify.TotalCount.Should().Be(5);
    summary.Verify.ClosedCount.Should().Be(2);
    summary.Verify.OpenCount.Should().Be(3);
    summary.Verify.BlockingFailureCount.Should().Be(3);
    summary.Verify.RecoverableWarningCount.Should().Be(0);
    summary.Verify.OptionalSkipCount.Should().Be(1);

    summary.Manual.TotalCount.Should().Be(2);
    summary.Manual.AnsweredCount.Should().Be(2);
    summary.Manual.PassCount.Should().Be(1);
    summary.Manual.FailCount.Should().Be(1);
    summary.Manual.NotApplicableCount.Should().Be(0);
    summary.Manual.OpenCount.Should().Be(0);
    summary.Manual.PercentComplete.Should().Be(100.0);
  }

  [Fact]
  public void LoadSummary_NormalizesLegacyStatusAliases()
  {
    WriteManifest("Q2_2026", "Classified Full");
    WriteControls();

    WriteVerifyReport(
      Path.Combine(_bundleRoot, "Verify", "ToolA", "consolidated-results.json"),
      "Pass",
      "NotAFinding",
      "NOT_APPLICABLE",
      "n/a",
      "Compliant",
      "Fail",
      "Open",
      "not reviewed",
      "unknown");

    var service = new BundleMissionSummaryService();
    var summary = service.LoadSummary(_bundleRoot);

    summary.Verify.TotalCount.Should().Be(9);
    summary.Verify.ClosedCount.Should().Be(5);
    summary.Verify.OpenCount.Should().Be(4);
    summary.Verify.BlockingFailureCount.Should().Be(4);
    summary.Verify.OptionalSkipCount.Should().Be(2);
  }

  [Fact]
  public void LoadSummary_CollectsDiagnosticsForMalformedVerifyReport()
  {
    WriteManifest("Q3_2026", "Classified AuditOnly");
    WriteControls();

    var malformed = Path.Combine(_bundleRoot, "Verify", "ToolA", "consolidated-results.json");
    File.WriteAllText(malformed, "{ bad json", Encoding.UTF8);

    var service = new BundleMissionSummaryService();
    var summary = service.LoadSummary(_bundleRoot);

    summary.Verify.TotalCount.Should().Be(0);
    summary.Verify.RecoverableWarningCount.Should().Be(1);
    summary.Diagnostics.Should().NotBeEmpty();
    summary.Diagnostics.Any(d => d.Contains("Failed to parse verify report", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
  }

  private void WriteManifest(string packName, string profileName)
  {
    var manifestPath = Path.Combine(_bundleRoot, "Manifest", "manifest.json");
    var manifest = new
    {
      run = new
      {
        packName,
        profileName
      }
    };

    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
    {
      WriteIndented = true
    }), Encoding.UTF8);
  }

  private void WriteControls(params ControlRecord[] controls)
  {
    var controlsPath = Path.Combine(_bundleRoot, "Manifest", "pack_controls.json");
    File.WriteAllText(controlsPath, JsonSerializer.Serialize(controls, new JsonSerializerOptions
    {
      WriteIndented = true
    }), Encoding.UTF8);
  }

  private static void WriteVerifyReport(string path, params string[] statuses)
  {
    var resultRows = statuses.Select((status, idx) => new
    {
      ruleId = "SV-" + (idx + 1).ToString("000") + "_rule",
      vulnId = "V-" + (idx + 1).ToString("000"),
      status
    }).ToList();

    var report = new
    {
      results = resultRows
    };

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      WriteIndented = true
    }), Encoding.UTF8);
  }

  private static ControlRecord MakeControl(string ruleId, string vulnId, bool isManual)
  {
    return new ControlRecord
    {
      ControlId = ruleId,
      Title = "Control " + ruleId,
      Severity = "medium",
      CheckText = "Check",
      FixText = "Fix",
      IsManual = isManual,
      ExternalIds = new ExternalIds
      {
        RuleId = ruleId,
        VulnId = vulnId,
        BenchmarkId = "WIN11"
      },
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Both,
        Confidence = Confidence.High
      },
      Revision = new RevisionInfo
      {
        PackName = "TestPack"
      }
    };
  }
}

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
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

  [Fact]
  public void LoadSummary_UsesLegacyManifestMetadataFallback()
  {
    WriteLegacyManifest("Q4_2026", "Legacy Profile");
    WriteControls();

    var service = new BundleMissionSummaryService();
    var summary = service.LoadSummary(_bundleRoot);

    summary.PackName.Should().Be("Q4_2026");
    summary.ProfileName.Should().Be("Legacy Profile");
  }

  [Fact]
  public void LoadSummary_TracksInformationalAndWarningStatusesAsRecoverableWarnings()
  {
    WriteManifest("Q1_2027", "Classified Safe");
    WriteControls();

    WriteVerifyReport(
      Path.Combine(_bundleRoot, "Verify", "ToolA", "consolidated-results.json"),
      "informational",
      "info",
      "warning",
      "error");

    var service = new BundleMissionSummaryService();
    var summary = service.LoadSummary(_bundleRoot);

    summary.Verify.TotalCount.Should().Be(4);
    summary.Verify.ClosedCount.Should().Be(0);
    summary.Verify.OpenCount.Should().Be(4);
    summary.Verify.BlockingFailureCount.Should().Be(4);
    summary.Verify.RecoverableWarningCount.Should().Be(3);
    summary.Verify.OptionalSkipCount.Should().Be(0);
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

  private void WriteLegacyManifest(string packName, string profileName)
  {
    var manifestPath = Path.Combine(_bundleRoot, "Manifest", "manifest.json");
    var manifest = new
    {
      packName,
      profileName
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

  [Fact]
  public async Task LoadTimelineSummaryAsync_ReturnsNull_WhenNoRepositoryConfigured()
  {
    var service = new BundleMissionSummaryService();

    var result = await service.LoadTimelineSummaryAsync(_bundleRoot, CancellationToken.None);

    result.Should().BeNull("the service has no repository configured");
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_ReturnsEventsOrderedBySeq_WhenRunExists()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun
    {
      RunId = runId,
      Label = "Test",
      BundleRoot = _bundleRoot,
      Status = MissionRunStatus.Completed,
      CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
      FinishedAt = DateTimeOffset.UtcNow
    };

    // Events deliberately out of order to verify sorting
    var events = new List<MissionTimelineEvent>
    {
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 3, Phase = MissionPhase.Verify, StepName = "evaluate_stig", Status = MissionEventStatus.Skipped, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 1, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Started, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-4) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 2, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Finished, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-3) },
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(runId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(events.OrderBy(e => e.Seq).ToList());

    var service = new BundleMissionSummaryService(missionRunRepo: repo.Object);

    var result = await service.LoadTimelineSummaryAsync(_bundleRoot, CancellationToken.None);

    result.Should().NotBeNull();
    result!.Events.Should().HaveCount(3);
    result.Events.Select(e => e.Seq).Should().BeInAscendingOrder("repository returns events ordered by seq");
    result.LastPhase.Should().Be(MissionPhase.Verify);
    result.LastStepName.Should().Be("evaluate_stig");
    result.IsBlocked.Should().BeFalse();
  }

  [Fact]
  public async Task LoadTimelineSummaryAsync_SetsIsBlocked_WhenFailedEventPresent()
  {
    var runId = Guid.NewGuid().ToString();
    var run = new MissionRun { RunId = runId, Label = "Fail", BundleRoot = _bundleRoot, Status = MissionRunStatus.Failed, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) };

    var events = new List<MissionTimelineEvent>
    {
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 1, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Started, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
      new() { EventId = Guid.NewGuid().ToString(), RunId = runId, Seq = 2, Phase = MissionPhase.Apply, StepName = "apply", Status = MissionEventStatus.Failed, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), Message = "Script error" },
    };

    var repo = new Mock<IMissionRunRepository>();
    repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(run);
    repo.Setup(r => r.GetTimelineAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(events);

    var service = new BundleMissionSummaryService(missionRunRepo: repo.Object);
    var result = await service.LoadTimelineSummaryAsync(_bundleRoot, CancellationToken.None);

    result.Should().NotBeNull();
    result!.IsBlocked.Should().BeTrue();
    result.NextAction.ToLowerInvariant().Should().Contain("blocked");
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

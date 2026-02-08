using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class ManualAnswerServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ManualAnswerService _svc;

  public ManualAnswerServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(_tempDir, "Manifest"));
    Directory.CreateDirectory(Path.Combine(_tempDir, "Manual"));
    _svc = new ManualAnswerService();
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  private static ControlRecord MakeManualControl(string ruleId, string vulnId)
  {
    return new ControlRecord
    {
      ControlId = ruleId,
      Title = "Manual: " + ruleId,
      Severity = "medium",
      CheckText = "Check manually",
      FixText = "Fix manually",
      IsManual = true,
      ExternalIds = new ExternalIds
      {
        RuleId = ruleId,
        VulnId = vulnId,
        SrgId = null,
        BenchmarkId = "WIN11"
      },
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Both,
        Confidence = Confidence.High
      },
      Revision = new RevisionInfo { PackName = "TestPack" }
    };
  }

  [Fact]
  public void LoadAnswerFile_NonExistentBundle_ReturnsEmpty()
  {
    var file = _svc.LoadAnswerFile(_tempDir);

    file.Should().NotBeNull();
    file.Answers.Should().BeEmpty();
  }

  [Fact]
  public void SaveAndLoadAnswer_RoundTrips()
  {
    var answer = new ManualAnswer
    {
      RuleId = "SV-12345_rule",
      VulnId = "V-12345",
      Status = "Pass",
      Reason = "Verified manually",
      Comment = "Looks good"
    };

    _svc.SaveAnswer(_tempDir, answer);

    var file = _svc.LoadAnswerFile(_tempDir);
    file.Answers.Should().HaveCount(1);
    file.Answers[0].RuleId.Should().Be("SV-12345_rule");
    file.Answers[0].Status.Should().Be("Pass");
    file.Answers[0].Reason.Should().Be("Verified manually");
  }

  [Fact]
  public void SaveAnswer_UpdatesExisting()
  {
    var answer1 = new ManualAnswer
    {
      RuleId = "SV-12345_rule",
      VulnId = "V-12345",
      Status = "Fail",
      Reason = "Initial finding"
    };

    _svc.SaveAnswer(_tempDir, answer1);

    var answer2 = new ManualAnswer
    {
      RuleId = "SV-12345_rule",
      VulnId = "V-12345",
      Status = "Pass",
      Reason = "Remediated"
    };

    _svc.SaveAnswer(_tempDir, answer2);

    var file = _svc.LoadAnswerFile(_tempDir);
    file.Answers.Should().HaveCount(1); // Updated, not duplicated
    file.Answers[0].Status.Should().Be("Pass");
    file.Answers[0].Reason.Should().Be("Remediated");
  }

  [Fact]
  public void GetAnswer_ReturnsMatchByRuleId()
  {
    var answer = new ManualAnswer
    {
      RuleId = "SV-12345_rule",
      VulnId = "V-12345",
      Status = "NotApplicable",
      Reason = "N/A"
    };

    _svc.SaveAnswer(_tempDir, answer);

    var control = MakeManualControl("SV-12345_rule", "V-12345");
    var result = _svc.GetAnswer(_tempDir, control);

    result.Should().NotBeNull();
    result!.Status.Should().Be("NotApplicable");
  }

  [Fact]
  public void GetAnswer_NoMatch_ReturnsNull()
  {
    var control = MakeManualControl("SV-99999_rule", "V-99999");
    var result = _svc.GetAnswer(_tempDir, control);

    result.Should().BeNull();
  }

  [Fact]
  public void GetUnansweredControls_ReturnsOnlyUnanswered()
  {
    var controls = new List<ControlRecord>
    {
      MakeManualControl("SV-001_rule", "V-001"),
      MakeManualControl("SV-002_rule", "V-002"),
      MakeManualControl("SV-003_rule", "V-003")
    };

    // Answer first control
    _svc.SaveAnswer(_tempDir, new ManualAnswer
    {
      RuleId = "SV-001_rule",
      VulnId = "V-001",
      Status = "Pass"
    });

    var unanswered = _svc.GetUnansweredControls(_tempDir, controls);

    unanswered.Should().HaveCount(2);
    unanswered.Select(c => c.ExternalIds.RuleId).Should().Contain("SV-002_rule", "SV-003_rule");
    unanswered.Select(c => c.ExternalIds.RuleId).Should().NotContain("SV-001_rule");
  }

  [Fact]
  public void GetProgressStats_CalculatesCorrectly()
  {
    var controls = new List<ControlRecord>
    {
      MakeManualControl("SV-001_rule", "V-001"),
      MakeManualControl("SV-002_rule", "V-002"),
      MakeManualControl("SV-003_rule", "V-003"),
      MakeManualControl("SV-004_rule", "V-004")
    };

    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-001_rule", VulnId = "V-001", Status = "Pass" });
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-002_rule", VulnId = "V-002", Status = "Fail" });
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-003_rule", VulnId = "V-003", Status = "NotApplicable" });

    var stats = _svc.GetProgressStats(_tempDir, controls);

    stats.TotalControls.Should().Be(4);
    stats.AnsweredControls.Should().Be(3);
    stats.UnansweredControls.Should().Be(1);
    stats.PassCount.Should().Be(1);
    stats.FailCount.Should().Be(1);
    stats.NotApplicableCount.Should().Be(1);
    stats.PercentComplete.Should().Be(75.0);
  }

  [Fact]
  public void GetProgressStats_EmptyAnswers_ZeroPercent()
  {
    var controls = new List<ControlRecord>
    {
      MakeManualControl("SV-001_rule", "V-001")
    };

    var stats = _svc.GetProgressStats(_tempDir, controls);

    stats.TotalControls.Should().Be(1);
    stats.AnsweredControls.Should().Be(0);
    stats.PercentComplete.Should().Be(0.0);
  }

  [Fact]
  public void GetProgressStats_NoControls_ZeroPercent()
  {
    var stats = _svc.GetProgressStats(_tempDir, new List<ControlRecord>());

    stats.TotalControls.Should().Be(0);
    stats.PercentComplete.Should().Be(0.0);
  }

  [Fact]
  public void SaveAnswer_NormalizesLegacyStatusAliases()
  {
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-100_rule", VulnId = "V-100", Status = "NotAFinding" });
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-101_rule", VulnId = "V-101", Status = "Open" });
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-102_rule", VulnId = "V-102", Status = "not_applicable" });
    _svc.SaveAnswer(_tempDir, new ManualAnswer { RuleId = "SV-103_rule", VulnId = "V-103", Status = "not reviewed" });

    var file = _svc.LoadAnswerFile(_tempDir);
    file.Answers.Should().ContainSingle(a => a.RuleId == "SV-100_rule" && a.Status == "Pass");
    file.Answers.Should().ContainSingle(a => a.RuleId == "SV-101_rule" && a.Status == "Open");
    file.Answers.Should().ContainSingle(a => a.RuleId == "SV-102_rule" && a.Status == "NotApplicable");
    file.Answers.Should().ContainSingle(a => a.RuleId == "SV-103_rule" && a.Status == "Open");
  }

  [Fact]
  public void ValidateReasonRequirement_FailAndNotApplicable_RequireReason()
  {
    var failAction = () => _svc.ValidateReasonRequirement("Fail", "");
    var naAction = () => _svc.ValidateReasonRequirement("NotApplicable", "   ");
    var placeholderAction = () => _svc.ValidateReasonRequirement("Fail", "none");
    var passAction = () => _svc.ValidateReasonRequirement("Pass", "");

    failAction.Should().Throw<ArgumentException>();
    naAction.Should().Throw<ArgumentException>();
    placeholderAction.Should().Throw<ArgumentException>();
    passAction.Should().NotThrow();
  }

  [Fact]
  public void ValidateBreakGlassReason_RequiresSpecificReason()
  {
    var shortReason = () => _svc.ValidateBreakGlassReason("urgent");
    var placeholder = () => _svc.ValidateBreakGlassReason("N/A");
    var valid = () => _svc.ValidateBreakGlassReason("Emergency rollback baseline unavailable");

    shortReason.Should().Throw<ArgumentException>();
    placeholder.Should().Throw<ArgumentException>();
    valid.Should().NotThrow();
  }

  [Fact]
  public void GetProgressStats_OpenStatus_RemainsUnanswered()
  {
    var controls = new List<ControlRecord>
    {
      MakeManualControl("SV-200_rule", "V-200")
    };

    _svc.SaveAnswer(_tempDir, new ManualAnswer
    {
      RuleId = "SV-200_rule",
      VulnId = "V-200",
      Status = "Open"
    });

    var stats = _svc.GetProgressStats(_tempDir, controls);
    stats.TotalControls.Should().Be(1);
    stats.AnsweredControls.Should().Be(0);
    stats.UnansweredControls.Should().Be(1);
    stats.FailCount.Should().Be(0);
    stats.PercentComplete.Should().Be(0.0);
  }

  [Fact]
  public void SaveAnswerFile_CreatesDirectoryIfMissing()
  {
    var deepDir = Path.Combine(_tempDir, "sub", "deep");
    // This won't have the Manual dir - SaveAnswer should create it
    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-1_rule", Status = "Pass" }
      }
    };

    _svc.SaveAnswerFile(deepDir, answerFile);

    File.Exists(Path.Combine(deepDir, "Manual", "answers.json")).Should().BeTrue();
  }

  [Fact]
  public void ValidateBreakGlassReason_RejectsPlaceholderAndShortReasons()
  {
    var shortReason = () => _svc.ValidateBreakGlassReason("urgent");
    var placeholderReason = () => _svc.ValidateBreakGlassReason("N/A");

    shortReason.Should().Throw<ArgumentException>();
    placeholderReason.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void ValidateBreakGlassReason_AcceptsSpecificReason()
  {
    var action = () => _svc.ValidateBreakGlassReason("Emergency rollback baseline unavailable");

    action.Should().NotThrow();
  }

  [Fact]
  public void ValidateReasonRequirement_RejectsPlaceholderReason()
  {
    var action = () => _svc.ValidateReasonRequirement("Fail", "none");

    action.Should().Throw<ArgumentException>();
  }
}

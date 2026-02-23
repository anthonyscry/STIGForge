using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public class AnswerRebaseServiceTests : IDisposable
{
  private readonly string _bundleRoot;

  public AnswerRebaseServiceTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-rebase-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manual"));
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  private static ControlRecord MakeControl(string id, string? checkText = null, string? fixText = null,
    string severity = "medium", string? discussion = null, bool isManual = true)
  {
    return new ControlRecord
    {
      ControlId = id,
      Title = "Title " + id,
      Severity = severity,
      CheckText = checkText ?? "Check " + id,
      FixText = fixText ?? "Fix " + id,
      Discussion = discussion ?? "Discussion " + id,
      IsManual = isManual,
      ExternalIds = new ExternalIds
      {
        RuleId = "SV-" + id + "_rule",
        VulnId = "V-" + id,
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

  private void WriteAnswerFile(params ManualAnswer[] answers)
  {
    var answerService = new ManualAnswerService();
    var file = new AnswerFile
    {
      Answers = answers.ToList(),
      CreatedAt = DateTimeOffset.UtcNow
    };
    answerService.SaveAnswerFile(_bundleRoot, file);
  }

  private (AnswerRebaseService svc, Mock<IControlRepository> controlRepo) CreateService()
  {
    var controlRepo = new Mock<IControlRepository>();
    var diffService = new BaselineDiffService(controlRepo.Object);
    var answerService = new ManualAnswerService();
    var svc = new AnswerRebaseService(answerService, diffService);
    return (svc, controlRepo);
  }

  [Fact]
  public async Task UnchangedControl_AutoCarry()
  {
    var controls = new List<ControlRecord> { MakeControl("C1") };
    WriteAnswerFile(new ManualAnswer { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified" });

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(controls);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(controls);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(AnswerRebaseActionType.Carry);
    report.Actions[0].Confidence.Should().Be(1.0);
    report.BlockingConflicts.Should().Be(0);
  }

  [Fact]
  public async Task RemovedControl_BlockingConflict()
  {
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed
    WriteAnswerFile(new ManualAnswer { RuleId = "SV-C2_rule", VulnId = "V-C2", Status = "Pass", Reason = "Verified" });

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(AnswerRebaseActionType.Remove);
    report.Actions[0].IsBlockingConflict.Should().BeTrue();
    report.BlockingConflicts.Should().Be(1);
  }

  [Fact]
  public async Task HighImpactChange_ReviewRequired()
  {
    var baseline = new List<ControlRecord> { MakeControl("C1", checkText: "Old check") };
    var target = new List<ControlRecord> { MakeControl("C1", checkText: "Completely new check procedure") };
    WriteAnswerFile(new ManualAnswer { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified" });

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(AnswerRebaseActionType.ReviewRequired);
    report.Actions[0].Confidence.Should().BeLessThan(0.5);
  }

  [Fact]
  public async Task LowImpactChange_Carry()
  {
    var baseline = new List<ControlRecord> { MakeControl("C1", discussion: "Old discussion") };
    var target = new List<ControlRecord> { MakeControl("C1", discussion: "Updated discussion text") };
    WriteAnswerFile(new ManualAnswer { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified" });

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(AnswerRebaseActionType.Carry);
    report.Actions[0].Confidence.Should().BeGreaterThanOrEqualTo(0.8);
  }

  [Fact]
  public async Task ApplyRebase_BlocksOnConflicts()
  {
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed
    WriteAnswerFile(new ManualAnswer { RuleId = "SV-C2_rule", VulnId = "V-C2", Status = "Pass", Reason = "Verified" });

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");
    report.HasBlockingConflicts.Should().BeTrue();

    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-C2_rule", VulnId = "V-C2", Status = "Pass", Reason = "Verified" }
      }
    };

    Action act = () => svc.ApplyAnswerRebase(report, answerFile);
    act.Should().Throw<InvalidOperationException>()
      .WithMessage("*blocking conflicts*");
  }

  [Fact]
  public async Task ApplyRebase_WritesRebasedAnswers()
  {
    var controls = new List<ControlRecord> { MakeControl("C1") };
    WriteAnswerFile(
      new ManualAnswer { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified", Comment = "Original comment" }
    );

    var (svc, controlRepo) = CreateService();
    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(controls);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(controls);

    var report = await svc.RebaseAnswersAsync(_bundleRoot, "base", "target");
    report.HasBlockingConflicts.Should().BeFalse();

    var sourceAnswers = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified", Comment = "Original comment" }
      }
    };

    var rebased = svc.ApplyAnswerRebase(report, sourceAnswers);

    rebased.Answers.Should().HaveCount(1);
    rebased.Answers[0].RuleId.Should().Be("SV-C1_rule");
    rebased.Answers[0].Status.Should().Be("Pass");
    rebased.Answers[0].Reason.Should().Be("Verified");
    // Confidence was 1.0, so no REBASED prefix
    rebased.Answers[0].Comment.Should().Be("Original comment");
  }
}

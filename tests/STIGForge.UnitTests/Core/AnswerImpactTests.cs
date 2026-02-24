using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public class AnswerImpactTests
{
  private static ControlRecord MakeControl(string id, string? checkText = null, string? fixText = null,
    string severity = "medium", string? discussion = null)
  {
    return new ControlRecord
    {
      ControlId = id,
      Title = "Title " + id,
      Severity = severity,
      CheckText = checkText ?? "Check " + id,
      FixText = fixText ?? "Fix " + id,
      Discussion = discussion ?? "Discussion " + id,
      IsManual = true,
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

  [Fact]
  public void CheckTextUnchanged_AnswerValid()
  {
    // Only FixText changed — answer should remain Valid
    var baseline = MakeControl("C1", checkText: "Same check", fixText: "Old fix");
    var newCtrl = MakeControl("C1", checkText: "Same check", fixText: "New fix");

    var diff = new BaselineDiff
    {
      BaselinePackId = "base",
      NewPackId = "target",
      ModifiedControls = new List<ControlDiff>
      {
        new()
        {
          ControlKey = "RULE:SV-C1_rule",
          BaselineControl = baseline,
          NewControl = newCtrl,
          ChangeType = ControlChangeType.Modified,
          Changes = new List<ControlFieldChange>
          {
            new() { FieldName = "FixText", OldValue = "Old fix", NewValue = "New fix", Impact = FieldChangeImpact.Medium }
          }
        }
      }
    };

    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-C1_rule", VulnId = "V-C1", Status = "Pass", Reason = "Verified" }
      }
    };

    diff.AssessAnswerImpact(answerFile);

    var impact = diff.ModifiedControls[0].AnswerImpact;
    impact.Should().NotBeNull();
    impact!.Validity.Should().Be(AnswerValidity.Valid);
    impact.Reason!.Should().Contain("unchanged");
  }

  [Fact]
  public void CheckTextChanged_FixTextSame_AnswerUncertain()
  {
    var baseline = MakeControl("C1", checkText: "Old check", fixText: "Same fix");
    var newCtrl = MakeControl("C1", checkText: "New check", fixText: "Same fix");

    var diff = new BaselineDiff
    {
      BaselinePackId = "base",
      NewPackId = "target",
      ModifiedControls = new List<ControlDiff>
      {
        new()
        {
          ControlKey = "RULE:SV-C1_rule",
          BaselineControl = baseline,
          NewControl = newCtrl,
          ChangeType = ControlChangeType.Modified,
          Changes = new List<ControlFieldChange>
          {
            new() { FieldName = "CheckText", OldValue = "Old check", NewValue = "New check", Impact = FieldChangeImpact.High }
          }
        }
      }
    };

    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-C1_rule", Status = "Pass", Reason = "Verified" }
      }
    };

    diff.AssessAnswerImpact(answerFile);

    var impact = diff.ModifiedControls[0].AnswerImpact;
    impact.Should().NotBeNull();
    impact!.Validity.Should().Be(AnswerValidity.Uncertain);
    impact.Reason!.Should().Contain("review");
  }

  [Fact]
  public void BothChanged_AnswerInvalid()
  {
    var baseline = MakeControl("C1", checkText: "Old check", fixText: "Old fix");
    var newCtrl = MakeControl("C1", checkText: "New check", fixText: "New fix");

    var diff = new BaselineDiff
    {
      BaselinePackId = "base",
      NewPackId = "target",
      ModifiedControls = new List<ControlDiff>
      {
        new()
        {
          ControlKey = "RULE:SV-C1_rule",
          BaselineControl = baseline,
          NewControl = newCtrl,
          ChangeType = ControlChangeType.Modified,
          Changes = new List<ControlFieldChange>
          {
            new() { FieldName = "CheckText", OldValue = "Old check", NewValue = "New check", Impact = FieldChangeImpact.High },
            new() { FieldName = "FixText", OldValue = "Old fix", NewValue = "New fix", Impact = FieldChangeImpact.Medium }
          }
        }
      }
    };

    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>
      {
        new() { RuleId = "SV-C1_rule", Status = "Fail", Reason = "Non-compliant setting found" }
      }
    };

    diff.AssessAnswerImpact(answerFile);

    var impact = diff.ModifiedControls[0].AnswerImpact;
    impact.Should().NotBeNull();
    impact!.Validity.Should().Be(AnswerValidity.Invalid);
    impact.Reason!.Should().Contain("invalid");
  }

  [Fact]
  public void NoAnswer_NoImpact()
  {
    var baseline = MakeControl("C1", checkText: "Old check");
    var newCtrl = MakeControl("C1", checkText: "New check");

    var diff = new BaselineDiff
    {
      BaselinePackId = "base",
      NewPackId = "target",
      ModifiedControls = new List<ControlDiff>
      {
        new()
        {
          ControlKey = "RULE:SV-C1_rule",
          BaselineControl = baseline,
          NewControl = newCtrl,
          ChangeType = ControlChangeType.Modified,
          Changes = new List<ControlFieldChange>
          {
            new() { FieldName = "CheckText", OldValue = "Old check", NewValue = "New check", Impact = FieldChangeImpact.High }
          }
        }
      }
    };

    var answerFile = new AnswerFile
    {
      Answers = new List<ManualAnswer>() // No answers
    };

    diff.AssessAnswerImpact(answerFile);

    diff.ModifiedControls[0].AnswerImpact.Should().NotBeNull();
    diff.ModifiedControls[0].AnswerImpact!.Validity.Should().Be(AnswerValidity.NoAnswer);
  }
}

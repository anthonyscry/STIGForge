using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class ClassificationScopeServiceTests
{
  private readonly ClassificationScopeService _sut = new();

  private static ControlRecord MakeControl(string id, ScopeTag scope, Confidence confidence)
  {
    return new ControlRecord
    {
      ControlId = id,
      Applicability = new Applicability
      {
        ClassificationScope = scope,
        Confidence = confidence
      }
    };
  }

  private static Profile MakeProfile(ClassificationMode mode, Confidence threshold, bool autoNa = true)
  {
    return new Profile
    {
      ProfileId = "test",
      Name = "Test",
      ClassificationMode = mode,
      NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = autoNa,
        ConfidenceThreshold = threshold,
        DefaultNaCommentTemplate = "Auto-NA: out of scope"
      },
      AutomationPolicy = new AutomationPolicy
      {
        Mode = AutomationMode.Standard,
        NewRuleGraceDays = 30,
        AutoApplyRequiresMapping = true,
        ReleaseDateSource = ReleaseDateSource.ContentPack
      }
    };
  }

  // ── Classified mode ──

  [Fact]
  public void Classified_UnclassifiedOnlyControl_HighConfidence_MarkedNA()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.UnclassifiedOnly, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls.Should().HaveCount(1);
    result.Controls[0].Status.Should().Be(ControlStatus.NotApplicable);
    result.Controls[0].Comment.Should().Be("Auto-NA: out of scope");
    result.Controls[0].NeedsReview.Should().BeFalse();
    result.ReviewQueue.Should().BeEmpty();
  }

  [Fact]
  public void Classified_UnclassifiedOnlyControl_LowConfidence_RoutedToReview()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.UnclassifiedOnly, Confidence.Low)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeTrue();
    result.Controls[0].ReviewReason.Should().Contain("Low confidence");
    result.ReviewQueue.Should().HaveCount(1);
  }

  [Fact]
  public void Classified_UnknownScope_RoutedToReview()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.Unknown, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeTrue();
    result.Controls[0].ReviewReason.Should().Contain("Unknown classification scope");
    result.ReviewQueue.Should().HaveCount(1);
  }

  [Fact]
  public void Classified_BothScope_PassesThrough()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.Both, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeFalse();
    result.ReviewQueue.Should().BeEmpty();
  }

  // ── Unclassified mode ──

  [Fact]
  public void Unclassified_ClassifiedOnlyControl_HighConfidence_MarkedNA()
  {
    var profile = MakeProfile(ClassificationMode.Unclassified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.ClassifiedOnly, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls.Should().HaveCount(1);
    result.Controls[0].Status.Should().Be(ControlStatus.NotApplicable);
    result.Controls[0].Comment.Should().Be("Auto-NA: out of scope");
    result.Controls[0].NeedsReview.Should().BeFalse();
    result.ReviewQueue.Should().BeEmpty();
  }

  [Fact]
  public void Unclassified_ClassifiedOnlyControl_LowConfidence_RoutedToReview()
  {
    var profile = MakeProfile(ClassificationMode.Unclassified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.ClassifiedOnly, Confidence.Low)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeTrue();
    result.Controls[0].ReviewReason.Should().Contain("Low confidence");
    result.ReviewQueue.Should().HaveCount(1);
  }

  [Fact]
  public void Unclassified_UnknownScope_RoutedToReview()
  {
    var profile = MakeProfile(ClassificationMode.Unclassified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.Unknown, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeTrue();
    result.Controls[0].ReviewReason.Should().Contain("Unknown classification scope");
    result.ReviewQueue.Should().HaveCount(1);
  }

  [Fact]
  public void Unclassified_UnclassifiedOnlyControl_PassesThrough()
  {
    var profile = MakeProfile(ClassificationMode.Unclassified, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.UnclassifiedOnly, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeFalse();
    result.ReviewQueue.Should().BeEmpty();
  }

  // ── Mixed mode ──

  [Fact]
  public void Mixed_AllControlsPassThrough_NoAutoNa()
  {
    var profile = MakeProfile(ClassificationMode.Mixed, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.ClassifiedOnly, Confidence.High),
      MakeControl("V-002", ScopeTag.UnclassifiedOnly, Confidence.High),
      MakeControl("V-003", ScopeTag.Both, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls.Should().HaveCount(3);
    result.Controls.Should().OnlyContain(c => c.Status == ControlStatus.Open);
    // None of the non-Unknown controls should be marked for review
    result.ReviewQueue.Should().BeEmpty();
  }

  [Fact]
  public void Mixed_UnknownScope_RoutedToReviewWhenAutoNaEnabled()
  {
    var profile = MakeProfile(ClassificationMode.Mixed, Confidence.High);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.Unknown, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls[0].Status.Should().Be(ControlStatus.Open);
    result.Controls[0].NeedsReview.Should().BeTrue();
    result.Controls[0].ReviewReason.Should().Contain("Unknown classification scope");
    result.ReviewQueue.Should().HaveCount(1);
  }

  // ── Determinism ──

  [Fact]
  public void IdenticalInputs_ProduceIdenticalOutputs()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.Medium);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-003", ScopeTag.UnclassifiedOnly, Confidence.High),
      MakeControl("V-001", ScopeTag.Both, Confidence.Low),
      MakeControl("V-002", ScopeTag.Unknown, Confidence.Medium)
    };

    var result1 = _sut.Compile(profile, controls);
    var result2 = _sut.Compile(profile, controls);

    result1.Controls.Should().HaveCount(result2.Controls.Count);
    for (int i = 0; i < result1.Controls.Count; i++)
    {
      result1.Controls[i].Control.ControlId.Should().Be(result2.Controls[i].Control.ControlId);
      result1.Controls[i].Status.Should().Be(result2.Controls[i].Status);
      result1.Controls[i].NeedsReview.Should().Be(result2.Controls[i].NeedsReview);
    }
  }

  [Fact]
  public void DifferentControlOrder_ProducesSameOutput()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.Medium);
    var controlsA = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.Both, Confidence.High),
      MakeControl("V-002", ScopeTag.UnclassifiedOnly, Confidence.Medium),
      MakeControl("V-003", ScopeTag.Unknown, Confidence.Low)
    };
    var controlsB = new List<ControlRecord>
    {
      MakeControl("V-003", ScopeTag.Unknown, Confidence.Low),
      MakeControl("V-001", ScopeTag.Both, Confidence.High),
      MakeControl("V-002", ScopeTag.UnclassifiedOnly, Confidence.Medium)
    };

    var resultA = _sut.Compile(profile, controlsA);
    var resultB = _sut.Compile(profile, controlsB);

    resultA.Controls.Should().HaveCount(resultB.Controls.Count);
    for (int i = 0; i < resultA.Controls.Count; i++)
    {
      resultA.Controls[i].Control.ControlId.Should().Be(resultB.Controls[i].Control.ControlId);
      resultA.Controls[i].Status.Should().Be(resultB.Controls[i].Status);
      resultA.Controls[i].NeedsReview.Should().Be(resultB.Controls[i].NeedsReview);
    }
  }

  // ── Edge cases ──

  [Fact]
  public void AutoNaDisabled_NoScopeFiltering()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High, autoNa: false);
    var controls = new List<ControlRecord>
    {
      MakeControl("V-001", ScopeTag.UnclassifiedOnly, Confidence.High),
      MakeControl("V-002", ScopeTag.Unknown, Confidence.High)
    };

    var result = _sut.Compile(profile, controls);

    result.Controls.Should().HaveCount(2);
    result.Controls.Should().OnlyContain(c => c.Status == ControlStatus.Open);
    result.Controls.Should().OnlyContain(c => !c.NeedsReview);
    result.ReviewQueue.Should().BeEmpty();
  }

  [Fact]
  public void EmptyControlList_ReturnsEmptyResult()
  {
    var profile = MakeProfile(ClassificationMode.Classified, Confidence.High);
    var controls = new List<ControlRecord>();

    var result = _sut.Compile(profile, controls);

    result.Controls.Should().BeEmpty();
    result.ReviewQueue.Should().BeEmpty();
  }
}

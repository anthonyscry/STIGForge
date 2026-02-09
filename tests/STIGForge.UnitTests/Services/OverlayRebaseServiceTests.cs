using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class OverlayRebaseServiceTests
{
  private static ControlRecord MakeControl(string id, string title = "Title", string severity = "medium",
    string? checkText = null)
  {
    return new ControlRecord
    {
      ControlId = id,
      Title = title,
      Severity = severity,
      CheckText = checkText ?? "Check " + id,
      FixText = "Fix " + id,
      IsManual = false,
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

  private static (OverlayRebaseService svc, Mock<IOverlayRepository> overlayRepo, Mock<IControlRepository> controlRepo) CreateService()
  {
    var overlayRepo = new Mock<IOverlayRepository>();
    var controlRepo = new Mock<IControlRepository>();
    var diffService = new BaselineDiffService(controlRepo.Object);
    var svc = new OverlayRebaseService(overlayRepo.Object, diffService);
    return (svc, overlayRepo, controlRepo);
  }

  [Fact]
  public async Task Rebase_OverlayNotFound_ReturnsFailure()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync((Overlay?)null);

    // Also mock controls so diff doesn't fail
    controlRepo.Setup(r => r.ListControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<ControlRecord>());

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    report.Success.Should().BeFalse();
    report.ErrorMessage.Should().Contain("not found");
  }

  [Fact]
  public async Task Rebase_UnchangedControls_AllKept()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var controls = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };

    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(controls);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(controls);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Test Overlay",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>
      {
        new() { RuleId = "SV-C1_rule", VulnId = "V-C1", StatusOverride = ControlStatus.NotApplicable, NaReason = "Test" }
      },
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(RebaseActionType.Keep);
    report.Actions[0].Confidence.Should().BeGreaterThanOrEqualTo(0.9);
    report.BlockingConflicts.Should().Be(0);
  }

  [Fact]
  public async Task Rebase_RemovedControl_MarkedForRemoval()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed

    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Test Overlay",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>
      {
        new() { RuleId = "SV-C2_rule", VulnId = "V-C2", StatusOverride = ControlStatus.NotApplicable, NaReason = "Removed" }
      },
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(RebaseActionType.Remove);
    report.Actions[0].RequiresReview.Should().BeTrue();
    report.Actions[0].IsBlockingConflict.Should().BeTrue();
    report.Actions[0].RecommendedAction.Should().NotBeNullOrWhiteSpace();
    report.BlockingConflicts.Should().Be(1);
    // Remove actions have high confidence (removal is certain), but require review
  }

  [Fact]
  public async Task Rebase_ModifiedCheckText_FlaggedForReview()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1", checkText: "Old check") };
    var target = new List<ControlRecord> { MakeControl("C1", checkText: "Completely new check procedure") };

    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Test Overlay",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>
      {
        new() { RuleId = "SV-C1_rule", VulnId = "V-C1", StatusOverride = ControlStatus.NotApplicable, NaReason = "Test" }
      },
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(RebaseActionType.ReviewRequired);
    report.Actions[0].RequiresReview.Should().BeTrue();
    report.Actions[0].IsBlockingConflict.Should().BeTrue();
    report.Actions[0].RecommendedAction.Should().NotBeNullOrWhiteSpace();
    report.BlockingConflicts.Should().Be(1);
  }

  [Fact]
  public async Task Rebase_EmptyOverlay_EmptyReport()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var controls = new List<ControlRecord> { MakeControl("C1") };

    controlRepo.Setup(r => r.ListControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(controls);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Empty Overlay",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>(),
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    report.Success.Should().BeTrue();
    report.Actions.Should().BeEmpty();
    report.OverallConfidence.Should().Be(1.0);
  }

  [Fact]
  public async Task Rebase_OverallConfidence_ReflectsLowest()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed = low confidence

    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Test",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>
      {
        new() { RuleId = "SV-C1_rule", VulnId = "V-C1", StatusOverride = ControlStatus.NotApplicable },
        new() { RuleId = "SV-C2_rule", VulnId = "V-C2", StatusOverride = ControlStatus.NotApplicable }
      },
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");

    // One action is Keep (1.0), one is Remove (1.0), but Remove requires review
    report.Actions.Should().HaveCount(2);
    report.HighRisk.Should().Be(1);
    report.BlockingConflicts.Should().Be(1);
    report.Actions.Should().Contain(a => a.ActionType == RebaseActionType.Remove);
  }

  [Fact]
  public async Task ApplyRebase_WithBlockingConflicts_Throws()
  {
    var (svc, overlayRepo, controlRepo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed -> blocking

    controlRepo.Setup(r => r.ListControlsAsync("base", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    controlRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var overlay = new Overlay
    {
      OverlayId = "overlay1",
      Name = "Blocking overlay",
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = new List<ControlOverride>
      {
        new() { RuleId = "SV-C2_rule", VulnId = "V-C2", StatusOverride = ControlStatus.NotApplicable }
      },
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };

    overlayRepo.Setup(r => r.GetAsync("overlay1", It.IsAny<CancellationToken>())).ReturnsAsync(overlay);

    var report = await svc.RebaseOverlayAsync("overlay1", "base", "target");
    report.HasBlockingConflicts.Should().BeTrue();

    Func<Task> act = async () => await svc.ApplyRebaseAsync("overlay1", report);
    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*blocking conflicts*");
  }
}

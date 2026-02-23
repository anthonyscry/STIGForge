using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class OverlayConflictDetectorTests
{
  private readonly OverlayConflictDetector _sut = new();

  private static Overlay MakeOverlay(string id, params ControlOverride[] overrides)
  {
    return new Overlay
    {
      OverlayId = id,
      Name = id,
      Overrides = overrides
    };
  }

  private static ControlOverride MakeOverride(string ruleId, ControlStatus? status, string? naReason = null, string? vulnId = null)
  {
    return new ControlOverride
    {
      RuleId = ruleId,
      VulnId = vulnId,
      StatusOverride = status,
      NaReason = naReason
    };
  }

  [Fact]
  public void NoOverlays_EmptyReport()
  {
    var result = _sut.DetectConflicts(Array.Empty<Overlay>());

    result.Conflicts.Should().BeEmpty();
    result.HasBlockingConflicts.Should().BeFalse();
  }

  [Fact]
  public void SingleOverlay_NoConflicts()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Not needed"))
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().BeEmpty();
    result.HasBlockingConflicts.Should().BeFalse();
  }

  [Fact]
  public void TwoOverlays_SameControl_LastWins()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-low", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Low priority reason")),
      MakeOverlay("ov-high", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "High priority reason"))
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().HaveCount(1);
    result.Conflicts[0].WinningOverlayId.Should().Be("ov-high");
    result.Conflicts[0].OverriddenOverlayId.Should().Be("ov-low");
    result.Conflicts[0].ControlKey.Should().Be("SV-001r1");
  }

  [Fact]
  public void TwoOverlays_DifferentStatus_IsBlockingConflict()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "NA reason")),
      MakeOverlay("ov-2", MakeOverride("SV-001r1", null)) // null = Open
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().HaveCount(1);
    result.Conflicts[0].IsBlockingConflict.Should().BeTrue();
    result.HasBlockingConflicts.Should().BeTrue();
    result.BlockingConflictCount.Should().Be(1);
    result.Conflicts[0].Reason.Should().Contain("Blocking");
  }

  [Fact]
  public void TwoOverlays_SameStatus_NonBlocking()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Reason A")),
      MakeOverlay("ov-2", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Reason B"))
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().HaveCount(1);
    result.Conflicts[0].IsBlockingConflict.Should().BeFalse();
    result.HasBlockingConflicts.Should().BeFalse();
    result.Conflicts[0].Reason.Should().Contain("Non-blocking");
  }

  [Fact]
  public void ThreeOverlays_SameControl_HighestIndexWins()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "One")),
      MakeOverlay("ov-2", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Two")),
      MakeOverlay("ov-3", MakeOverride("SV-001r1", ControlStatus.NotApplicable, "Three"))
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().HaveCount(2);
    result.Conflicts.Should().OnlyContain(c => c.WinningOverlayId == "ov-3");
    result.Conflicts.Select(c => c.OverriddenOverlayId).Should().BeEquivalentTo(new[] { "ov-1", "ov-2" });
  }

  [Fact]
  public void DeterministicOutput()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1",
        MakeOverride("SV-002r1", ControlStatus.NotApplicable, "A"),
        MakeOverride("SV-001r1", ControlStatus.NotApplicable, "A")),
      MakeOverlay("ov-2",
        MakeOverride("SV-001r1", null),
        MakeOverride("SV-002r1", ControlStatus.NotApplicable, "B"))
    };

    var result1 = _sut.DetectConflicts(overlays);
    var result2 = _sut.DetectConflicts(overlays);

    result1.Conflicts.Should().HaveCount(result2.Conflicts.Count);
    for (int i = 0; i < result1.Conflicts.Count; i++)
    {
      result1.Conflicts[i].ControlKey.Should().Be(result2.Conflicts[i].ControlKey);
      result1.Conflicts[i].WinningOverlayId.Should().Be(result2.Conflicts[i].WinningOverlayId);
      result1.Conflicts[i].OverriddenOverlayId.Should().Be(result2.Conflicts[i].OverriddenOverlayId);
      result1.Conflicts[i].IsBlockingConflict.Should().Be(result2.Conflicts[i].IsBlockingConflict);
    }

    // Also verify ControlKey ordering is ascending
    var keys = result1.Conflicts.Select(c => c.ControlKey).ToList();
    keys.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
  }

  [Fact]
  public void ControlKey_PrefersRuleId_FallsBackToVulnId()
  {
    var overlays = new List<Overlay>
    {
      MakeOverlay("ov-1", new ControlOverride { VulnId = "V-001", RuleId = null, StatusOverride = ControlStatus.NotApplicable }),
      MakeOverlay("ov-2", new ControlOverride { VulnId = "V-001", RuleId = null, StatusOverride = null })
    };

    var result = _sut.DetectConflicts(overlays);

    result.Conflicts.Should().HaveCount(1);
    result.Conflicts[0].ControlKey.Should().Be("V-001");
    result.Conflicts[0].IsBlockingConflict.Should().BeTrue();
  }
}

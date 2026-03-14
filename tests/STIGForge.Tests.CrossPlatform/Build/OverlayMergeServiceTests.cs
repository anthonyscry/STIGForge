using FluentAssertions;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Build;

/// <summary>
/// Direct unit tests for OverlayMergeService targeting previously uncovered paths.
/// BundleBuilderTests already exercises the service indirectly with empty overlays,
/// so these tests focus on the override-application, conflict-detection, and
/// last-wins semantics that are not covered there.
/// </summary>
public sealed class OverlayMergeServiceTests
{
    private readonly OverlayMergeService _sut = new();

    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Merge_NullCompiledControls_ThrowsArgumentNullException()
    {
        var act = () => _sut.Merge(null!, []);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Merge_NullOverlays_ThrowsArgumentNullException()
    {
        var act = () => _sut.Merge([], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Empty overlays ────────────────────────────────────────────────────────

    [Fact]
    public void Merge_EmptyOverlays_ReturnsCopiesUnchanged()
    {
        var controls = new[]
        {
            MakeControl("V-001", "SV-001r1", ControlStatus.Open, "original")
        };

        var result = _sut.Merge(controls, []);

        result.MergedControls.Should().HaveCount(1);
        result.MergedControls[0].Status.Should().Be(ControlStatus.Open);
        result.MergedControls[0].Comment.Should().Be("original");
        result.AppliedDecisions.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Merge_EmptyControlsEmptyOverlays_ReturnsEmptyResult()
    {
        var result = _sut.Merge([], []);

        result.MergedControls.Should().BeEmpty();
        result.AppliedDecisions.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
    }

    // ── Override by RuleId ────────────────────────────────────────────────────

    [Fact]
    public void Merge_SingleOverride_ByRuleId_AppliesStatus()
    {
        var controls = new[] { MakeControl("V-001", "SV-001r1", ControlStatus.Open) };
        var overlay = MakeOverlay("overlay-1", "O1",
            new ControlOverride { RuleId = "SV-001r1", StatusOverride = ControlStatus.NotApplicable });

        var result = _sut.Merge(controls, [overlay]);

        result.MergedControls[0].Status.Should().Be(ControlStatus.NotApplicable);
        result.AppliedDecisions.Should().HaveCount(1);
        result.AppliedDecisions[0].Key.Should().Be("RULE:SV-001r1");
    }

    // ── Override by VulnId ────────────────────────────────────────────────────

    [Fact]
    public void Merge_SingleOverride_ByVulnId_AppliesStatus()
    {
        var controls = new[] { MakeControl("V-002", "SV-002r1", ControlStatus.Open) };
        var overlay = MakeOverlay("overlay-1", "O1",
            new ControlOverride { VulnId = "V-002", StatusOverride = ControlStatus.Pass });

        var result = _sut.Merge(controls, [overlay]);

        result.MergedControls[0].Status.Should().Be(ControlStatus.Pass);
        result.AppliedDecisions.Should().HaveCount(1);
        result.AppliedDecisions[0].Key.Should().Be("VULN:V-002");
    }

    // ── NaReason applied as Comment ───────────────────────────────────────────

    [Fact]
    public void Merge_Override_WithNaReason_SetsComment()
    {
        var controls = new[] { MakeControl("V-003", "SV-003r1", ControlStatus.Open, "old comment") };
        var overlay = MakeOverlay("overlay-1", "O1",
            new ControlOverride
            {
                RuleId = "SV-003r1",
                StatusOverride = ControlStatus.NotApplicable,
                NaReason = "not applicable because X"
            });

        var result = _sut.Merge(controls, [overlay]);

        result.MergedControls[0].Comment.Should().Be("not applicable because X");
    }

    [Fact]
    public void Merge_Override_EmptyNaReason_PreservesOriginalComment()
    {
        var controls = new[] { MakeControl("V-004", "SV-004r1", ControlStatus.Open, "keep this") };
        var overlay = MakeOverlay("overlay-1", "O1",
            new ControlOverride
            {
                RuleId = "SV-004r1",
                StatusOverride = ControlStatus.Pass,
                NaReason = null
            });

        var result = _sut.Merge(controls, [overlay]);

        result.MergedControls[0].Comment.Should().Be("keep this");
    }

    // ── Last-wins across multiple overlays ───────────────────────────────────

    [Fact]
    public void Merge_TwoOverlays_SameKey_SecondOverlayWins()
    {
        var controls = new[] { MakeControl("V-005", "SV-005r1", ControlStatus.Open) };
        var first = MakeOverlay("ov-1", "First",
            new ControlOverride { RuleId = "SV-005r1", StatusOverride = ControlStatus.Pass });
        var second = MakeOverlay("ov-2", "Second",
            new ControlOverride { RuleId = "SV-005r1", StatusOverride = ControlStatus.Fail });

        var result = _sut.Merge(controls, [first, second]);

        result.MergedControls[0].Status.Should().Be(ControlStatus.Fail);
        result.AppliedDecisions.Should().HaveCount(2);
    }

    // ── Conflict detection ────────────────────────────────────────────────────

    [Fact]
    public void Merge_TwoOverlays_DifferentOutcomes_ConflictRecorded()
    {
        var controls = new[] { MakeControl("V-006", "SV-006r1", ControlStatus.Open) };
        var first = MakeOverlay("ov-1", "First",
            new ControlOverride { RuleId = "SV-006r1", StatusOverride = ControlStatus.Pass });
        var second = MakeOverlay("ov-2", "Second",
            new ControlOverride { RuleId = "SV-006r1", StatusOverride = ControlStatus.NotApplicable });

        var result = _sut.Merge(controls, [first, second]);

        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].Key.Should().Be("RULE:SV-006r1");
        result.Conflicts[0].Previous.OverlayId.Should().Be("ov-1");
        result.Conflicts[0].Current.OverlayId.Should().Be("ov-2");
    }

    [Fact]
    public void Merge_TwoOverlays_IdenticalOutcomes_NoConflict()
    {
        var controls = new[] { MakeControl("V-007", "SV-007r1", ControlStatus.Open) };
        var override1 = new ControlOverride { RuleId = "SV-007r1", StatusOverride = ControlStatus.Pass };
        var override2 = new ControlOverride { RuleId = "SV-007r1", StatusOverride = ControlStatus.Pass };
        var first  = MakeOverlay("ov-1", "First",  override1);
        var second = MakeOverlay("ov-2", "Second", override2);

        var result = _sut.Merge(controls, [first, second]);

        result.Conflicts.Should().BeEmpty();
    }

    // ── Multiple controls matching the same key ───────────────────────────────

    [Fact]
    public void Merge_OverrideByRuleId_MatchesAllControlsWithThatRuleId()
    {
        // Two controls sharing the same RuleId (unusual but defensively handled)
        var c1 = MakeControl("V-010", "SV-010r1", ControlStatus.Open);
        var c2 = MakeControl("V-011", "SV-010r1", ControlStatus.Open);  // same ruleId
        var overlay = MakeOverlay("ov-1", "OV1",
            new ControlOverride { RuleId = "SV-010r1", StatusOverride = ControlStatus.Pass });

        var result = _sut.Merge([c1, c2], [overlay]);

        result.MergedControls.Should().AllSatisfy(c => c.Status.Should().Be(ControlStatus.Pass));
    }

    // ── Override key resolves RuleId first, VulnId as fallback ───────────────

    [Fact]
    public void Merge_OverrideHasNoRuleIdNoVulnId_IsIgnored()
    {
        var controls = new[] { MakeControl("V-012", "SV-012r1", ControlStatus.Open) };
        var overlay = MakeOverlay("ov-1", "OV1",
            new ControlOverride { RuleId = null, VulnId = null, StatusOverride = ControlStatus.Pass });

        var result = _sut.Merge(controls, [overlay]);

        // No key resolved → override is a no-op
        result.MergedControls[0].Status.Should().Be(ControlStatus.Open);
        result.AppliedDecisions.Should().BeEmpty();
    }

    // ── Input controls are not mutated ────────────────────────────────────────

    [Fact]
    public void Merge_DoesNotMutateInputControls()
    {
        var controls = new[] { MakeControl("V-020", "SV-020r1", ControlStatus.Open, "original") };
        var overlay = MakeOverlay("ov-1", "OV1",
            new ControlOverride { RuleId = "SV-020r1", StatusOverride = ControlStatus.Pass });

        _sut.Merge(controls, [overlay]);

        // Original control object must be unchanged
        controls[0].Status.Should().Be(ControlStatus.Open);
        controls[0].Comment.Should().Be("original");
    }

    // ── Overlay with no overrides ─────────────────────────────────────────────

    [Fact]
    public void Merge_OverlayWithNoOverrides_LeavesControlsUnchanged()
    {
        var controls = new[] { MakeControl("V-030", "SV-030r1", ControlStatus.Open) };
        var emptyOverlay = new Overlay
        {
            OverlayId = "ov-empty",
            Name = "Empty",
            Overrides = []
        };

        var result = _sut.Merge(controls, [emptyOverlay]);

        result.MergedControls[0].Status.Should().Be(ControlStatus.Open);
        result.AppliedDecisions.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CompiledControl MakeControl(
        string vulnId,
        string ruleId,
        ControlStatus status,
        string? comment = null)
    {
        var record = new ControlRecord
        {
            ControlId = vulnId,
            ExternalIds = new ExternalIds { VulnId = vulnId, RuleId = ruleId }
        };
        return new CompiledControl(record, status, comment, needsReview: false, reviewReason: null);
    }

    private static Overlay MakeOverlay(string id, string name, params ControlOverride[] overrides) =>
        new()
        {
            OverlayId = id,
            Name = name,
            Overrides = overrides
        };
}

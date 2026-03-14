using FluentAssertions;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class ImportSelectionOrchestratorTests
{
    private readonly ImportSelectionOrchestrator _sut = new();

    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_NullCandidates_ThrowsArgumentNullException()
    {
        var act = () => _sut.BuildPlan(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
    }

    // ── Empty input ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_EmptyCandidates_ReturnsEmptyPlanWithZeroCounts()
    {
        var plan = _sut.BuildPlan([]);

        plan.Rows.Should().BeEmpty();
        plan.Warnings.Should().BeEmpty();
        plan.Counts.StigSelected.Should().Be(0);
        plan.Counts.ScapAutoIncluded.Should().Be(0);
        plan.Counts.RuleCount.Should().Be(0);
    }

    // ── No selected STIG → no auto-inclusion of companions ──────────────────

    [Fact]
    public void BuildPlan_NoStigSelected_ScapNotAutoIncluded()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Rows.Where(r => r.ArtifactType == ImportSelectionArtifactType.Scap)
            .Should().AllSatisfy(r => r.IsLocked.Should().BeFalse());
        plan.Counts.ScapAutoIncluded.Should().Be(0);
        plan.Warnings.Should().BeEmpty();
    }

    // ── Selected STIG → companions locked/auto-included ──────────────────────

    [Fact]
    public void BuildPlan_StigSelected_ScapAutoIncludedAndLocked()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        var scapRow = plan.Rows.Single(r => r.ArtifactType == ImportSelectionArtifactType.Scap);
        scapRow.IsSelected.Should().BeTrue();
        scapRow.IsLocked.Should().BeTrue();
        plan.Counts.ScapAutoIncluded.Should().Be(1);
    }

    [Fact]
    public void BuildPlan_StigSelected_GpoAndAdmxAutoIncludedAndLocked()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Gpo, "GPO-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Admx, "ADMX-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Rows.Where(r => r.ArtifactType is
                ImportSelectionArtifactType.Gpo or
                ImportSelectionArtifactType.Admx)
            .Should().AllSatisfy(r =>
            {
                r.IsSelected.Should().BeTrue();
                r.IsLocked.Should().BeTrue();
            });
    }

    // ── Missing SCAP dependency warning ─────────────────────────────────────

    [Fact]
    public void BuildPlan_StigSelected_NoScapPresent_AddsMissingScapWarning()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Warnings.Should().HaveCount(1);
        plan.Warnings[0].Code.Should().Be("missing_scap_dependency");
        plan.WarningLines.Should().HaveCount(1);
        plan.WarningLines[0].Should().Contain("SCAP");
    }

    [Fact]
    public void BuildPlan_StigSelected_ScapPresent_NoWarnings()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Warnings.Should().BeEmpty();
        plan.WarningLines.Should().BeEmpty();
    }

    // ── Counts ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_MultipleSelectedStigs_StigCountCorrect()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true, ruleCount: 50),
            Candidate(ImportSelectionArtifactType.Stig, "STIG-002", isSelected: true, ruleCount: 30),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Counts.StigSelected.Should().Be(2);
        plan.Counts.RuleCount.Should().Be(80);
    }

    [Fact]
    public void BuildPlan_RuleCount_OnlyFromSelectedStigs()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true,  ruleCount: 100),
            Candidate(ImportSelectionArtifactType.Stig, "STIG-002", isSelected: false, ruleCount: 999),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Counts.RuleCount.Should().Be(100);
    }

    // ── Status summary text ───────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_StatusSummaryText_ContainsAllArtifactTypeCounts()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Gpo, "GPO-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Admx, "ADMX-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.StatusSummaryText.Should().Contain("STIG: 1");
        plan.StatusSummaryText.Should().Contain("Auto SCAP: 1");
        plan.StatusSummaryText.Should().Contain("Auto GPO: 1");
        plan.StatusSummaryText.Should().Contain("Auto ADMX: 1");
    }

    // ── Fingerprint ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_SameInput_ProducesSameFingerprint()
    {
        var candidatesA = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };
        var candidatesB = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var planA = _sut.BuildPlan(candidatesA);
        var planB = _sut.BuildPlan(candidatesB);

        planA.Fingerprint.Should().Be(planB.Fingerprint);
    }

    [Fact]
    public void BuildPlan_DifferentSelection_ProducesDifferentFingerprint()
    {
        var withStig = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };
        var withoutStig = new[]
        {
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false)
        };

        var planA = _sut.BuildPlan(withStig);
        var planB = _sut.BuildPlan(withoutStig);

        planA.Fingerprint.Should().NotBe(planB.Fingerprint);
    }

    // ── Null rows in input are skipped ───────────────────────────────────────

    [Fact]
    public void BuildPlan_NullCandidateInList_IsIgnored()
    {
        var candidates = new ImportSelectionCandidate?[]
        {
            null,
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: true),
            null
        };

        var plan = _sut.BuildPlan(candidates!);

        plan.Rows.Should().HaveCount(1);
    }

    // ── Row ordering ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlan_RowsOrderedByStigFirstThenScapGpoAdmx()
    {
        var candidates = new[]
        {
            Candidate(ImportSelectionArtifactType.Admx, "ADMX-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Gpo,  "GPO-001",  isSelected: false),
            Candidate(ImportSelectionArtifactType.Scap, "SCAP-001", isSelected: false),
            Candidate(ImportSelectionArtifactType.Stig, "STIG-001", isSelected: false)
        };

        var plan = _sut.BuildPlan(candidates);

        plan.Rows[0].ArtifactType.Should().Be(ImportSelectionArtifactType.Stig);
        plan.Rows[1].ArtifactType.Should().Be(ImportSelectionArtifactType.Scap);
        plan.Rows[2].ArtifactType.Should().Be(ImportSelectionArtifactType.Gpo);
        plan.Rows[3].ArtifactType.Should().Be(ImportSelectionArtifactType.Admx);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ImportSelectionCandidate Candidate(
        ImportSelectionArtifactType type,
        string id,
        bool isSelected = false,
        int ruleCount = 0) =>
        new()
        {
            ArtifactType = type,
            Id = id,
            IsSelected = isSelected,
            StigRuleCount = ruleCount
        };
}

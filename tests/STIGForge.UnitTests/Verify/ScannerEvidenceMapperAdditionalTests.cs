using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Verify;
using VerifyControlResult = STIGForge.Verify.ControlResult;

namespace STIGForge.UnitTests.Verify;

public sealed class ScannerEvidenceMapperAdditionalTests
{
    private static readonly IReadOnlyList<LocalWorkflowChecklistItem> SingleItemChecklist =
    [
        new LocalWorkflowChecklistItem { RuleId = "SV-100r1_rule" }
    ];

    // ── null guard tests ────────────────────────────────────────────────────

    [Fact]
    public void Map_NullCanonicalChecklist_ThrowsArgumentNullException()
    {
        var mapper = new ScannerEvidenceMapper();

        var act = () => mapper.Map(null!, new List<VerifyControlResult>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("canonicalChecklist");
    }

    [Fact]
    public void Map_NullFindings_ThrowsArgumentNullException()
    {
        var mapper = new ScannerEvidenceMapper();

        var act = () => mapper.Map(SingleItemChecklist, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("findings");
    }

    // ── empty collections ───────────────────────────────────────────────────

    [Fact]
    public void Map_EmptyFindings_ReturnsEmptyCollections()
    {
        var mapper = new ScannerEvidenceMapper();

        var result = mapper.Map(SingleItemChecklist, new List<VerifyControlResult>());

        result.ScannerEvidence.Should().BeEmpty();
        result.Unmapped.Should().BeEmpty();
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Map_EmptyCanonicalChecklist_AllFindingsAreUnmapped()
    {
        var mapper = new ScannerEvidenceMapper();
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-100r1_rule", Tool = "Evaluate-STIG", SourceFile = "a.ckl" }
        };

        var result = mapper.Map(new List<LocalWorkflowChecklistItem>(), findings);

        result.ScannerEvidence.Should().BeEmpty();
        result.Unmapped.Should().HaveCount(1);
        result.Diagnostics.Should().HaveCount(1).And.Contain(d => d.Contains("SV-100r1_rule"));
    }

    // ── empty / null RuleId in finding ──────────────────────────────────────

    [Fact]
    public void Map_FindingWithNullRuleId_GoesToUnmappedWithMissingRuleIdReason()
    {
        var mapper = new ScannerEvidenceMapper();
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = null, Tool = "SomeTool", SourceFile = "out.ckl" }
        };

        var result = mapper.Map(SingleItemChecklist, findings);

        result.ScannerEvidence.Should().BeEmpty();
        result.Unmapped.Should().ContainSingle(u => u.Reason.Contains("Missing RuleId", StringComparison.Ordinal));
        result.Diagnostics.Should().ContainSingle(d => d.Contains("missing RuleId", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_FindingWithWhiteSpaceRuleId_GoesToUnmappedWithMissingRuleIdReason()
    {
        var mapper = new ScannerEvidenceMapper();
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "   ", Tool = "SomeTool", SourceFile = "out.ckl" }
        };

        var result = mapper.Map(SingleItemChecklist, findings);

        result.Unmapped.Should().ContainSingle(u => u.Reason.Contains("Missing RuleId", StringComparison.Ordinal));
    }

    // ── canonical checklist items with null/empty RuleId are skipped ─────────

    [Fact]
    public void Map_CanonicalChecklistWithNullRuleIdItem_SkipsNullItem()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = string.Empty },
            new() { RuleId = "SV-100r1_rule" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-100r1_rule", Tool = "T", SourceFile = "f.ckl" }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle(e => e.RuleId == "SV-100r1_rule");
    }

    // ── BuildSource branch coverage ─────────────────────────────────────────

    [Fact]
    public void Map_FindingWithToolAndSourceFile_SourceIsToolColonFile()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = "SV-A" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-A", Tool = "MyTool", SourceFile = "output.ckl" }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle(e => e.Source == "MyTool:output.ckl");
    }

    [Fact]
    public void Map_FindingWithSourceFileOnly_SourceIsFilePath()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = "SV-B" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-B", Tool = string.Empty, SourceFile = "result.ckl" }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle(e => e.Source == "result.ckl");
    }

    [Fact]
    public void Map_FindingWithToolOnly_SourceIsToolName()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = "SV-C" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-C", Tool = "OnlyTool", SourceFile = string.Empty }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle(e => e.Source == "OnlyTool");
    }

    [Fact]
    public void Map_FindingWithNoToolOrFile_SourceIsScannerFallback()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = "SV-D" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-D", Tool = string.Empty, SourceFile = string.Empty }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle(e => e.Source == "scanner");
    }

    // ── unmapped with source context ─────────────────────────────────────────

    [Fact]
    public void Map_UnmatchedFindingWithRuleId_UnmappedSourceIncludesRuleId()
    {
        var mapper = new ScannerEvidenceMapper();
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "SV-NOTFOUND", Tool = "T", SourceFile = "x.ckl" }
        };

        var result = mapper.Map(SingleItemChecklist, findings);

        result.Unmapped.Should().ContainSingle(u =>
            u.Source.Contains("SV-NOTFOUND", StringComparison.Ordinal)
            && u.Reason.Contains("No canonical checklist match", StringComparison.Ordinal));
    }

    // ── case-insensitive RuleId matching ─────────────────────────────────────

    [Fact]
    public void Map_RuleIdMatchIsCaseInsensitive()
    {
        var mapper = new ScannerEvidenceMapper();
        var checklist = new List<LocalWorkflowChecklistItem>
        {
            new() { RuleId = "SV-100r1_RULE" }
        };
        var findings = new List<VerifyControlResult>
        {
            new() { RuleId = "sv-100r1_rule", Tool = "T", SourceFile = "f.ckl" }
        };

        var result = mapper.Map(checklist, findings);

        result.ScannerEvidence.Should().ContainSingle();
        result.Unmapped.Should().BeEmpty();
    }
}

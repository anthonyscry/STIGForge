using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class ComplianceDiffGeneratorTests
{
    private static readonly DateTimeOffset _t0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _t1 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

    // ── ComputeDiff  -  empty inputs ────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_BothEmpty_ReturnsZeroCounts()
    {
        var diff = ComplianceDiffGenerator.ComputeDiff([], [], "base", "target");

        diff.Regressions.Should().BeEmpty();
        diff.Remediations.Should().BeEmpty();
        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
        diff.BaselineCompliancePercent.Should().Be(0.0);
        diff.TargetCompliancePercent.Should().Be(0.0);
        diff.DeltaPercent.Should().Be(0.0);
    }

    [Fact]
    public void ComputeDiff_Labels_ArePropagatedToResult()
    {
        var diff = ComplianceDiffGenerator.ComputeDiff([], [], "BaselineLabel", "TargetLabel");

        diff.BaselineLabel.Should().Be("BaselineLabel");
        diff.TargetLabel.Should().Be("TargetLabel");
    }

    // ── ComputeDiff  -  added controls ──────────────────────────────────────────

    [Fact]
    public void ComputeDiff_ControlInTargetOnly_IsReportedAsAdded()
    {
        var target = new[] { MakeResult("V-001", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff([], target, "base", "target");

        diff.Added.Should().ContainSingle().Which.VulnId.Should().Be("V-001");
        diff.Removed.Should().BeEmpty();
    }

    // ── ComputeDiff  -  removed controls ────────────────────────────────────────

    [Fact]
    public void ComputeDiff_ControlInBaselineOnly_IsReportedAsRemoved()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, [], "base", "target");

        diff.Removed.Should().ContainSingle().Which.VulnId.Should().Be("V-001");
        diff.Added.Should().BeEmpty();
    }

    // ── ComputeDiff  -  regressions ─────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_PassToFail_IsRegressionNotRemediation()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Pass) };
        var target = new[] { MakeResult("V-001", VerifyStatus.Fail) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Regressions.Should().ContainSingle().Which.VulnId.Should().Be("V-001");
        diff.Remediations.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_NotApplicableToFail_IsRegression()
    {
        var baseline = new[] { MakeResult("V-002", VerifyStatus.NotApplicable) };
        var target = new[] { MakeResult("V-002", VerifyStatus.Fail) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Regressions.Should().ContainSingle().Which.VulnId.Should().Be("V-002");
    }

    [Fact]
    public void ComputeDiff_PassToError_IsRegression()
    {
        var baseline = new[] { MakeResult("V-003", VerifyStatus.Pass) };
        var target = new[] { MakeResult("V-003", VerifyStatus.Error) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Regressions.Should().ContainSingle().Which.VulnId.Should().Be("V-003");
    }

    // ── ComputeDiff  -  remediations ────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_FailToPass_IsRemediationNotRegression()
    {
        var baseline = new[] { MakeResult("V-010", VerifyStatus.Fail) };
        var target = new[] { MakeResult("V-010", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Remediations.Should().ContainSingle().Which.VulnId.Should().Be("V-010");
        diff.Regressions.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_ErrorToPass_IsRemediation()
    {
        var baseline = new[] { MakeResult("V-011", VerifyStatus.Error) };
        var target = new[] { MakeResult("V-011", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Remediations.Should().ContainSingle();
    }

    [Fact]
    public void ComputeDiff_FailToNotApplicable_IsRemediation()
    {
        var baseline = new[] { MakeResult("V-012", VerifyStatus.Fail) };
        var target = new[] { MakeResult("V-012", VerifyStatus.NotApplicable) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Remediations.Should().ContainSingle();
    }

    // ── ComputeDiff  -  no change ───────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_PassToPass_NoRegressionOrRemediation()
    {
        var baseline = new[] { MakeResult("V-020", VerifyStatus.Pass) };
        var target = new[] { MakeResult("V-020", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.Regressions.Should().BeEmpty();
        diff.Remediations.Should().BeEmpty();
    }

    // ── ComputeDiff  -  compliance percent ─────────────────────────────────────

    [Fact]
    public void ComputeDiff_AllPass_BaselinePercent100()
    {
        var results = new[]
        {
            MakeResult("V-001", VerifyStatus.Pass),
            MakeResult("V-002", VerifyStatus.Pass)
        };

        var diff = ComplianceDiffGenerator.ComputeDiff(results, results, "base", "target");

        diff.BaselineCompliancePercent.Should().Be(100.0);
        diff.TargetCompliancePercent.Should().Be(100.0);
        diff.DeltaPercent.Should().Be(0.0);
    }

    [Fact]
    public void ComputeDiff_OnePassOneFail_Percent50()
    {
        var results = new[]
        {
            MakeResult("V-001", VerifyStatus.Pass),
            MakeResult("V-002", VerifyStatus.Fail)
        };

        var diff = ComplianceDiffGenerator.ComputeDiff(results, results, "base", "target");

        diff.BaselineCompliancePercent.Should().Be(50.0);
    }

    [Fact]
    public void ComputeDiff_DeltaPercent_IsTargetMinusBaseline()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Fail), MakeResult("V-002", VerifyStatus.Pass) };
        var target = new[] { MakeResult("V-001", VerifyStatus.Pass), MakeResult("V-002", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.DeltaPercent.Should().BeGreaterThan(0);
    }

    // ── ComputeDiff  -  timestamps ──────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_VerifiedAtTimestamps_MaxIsUsedPerSide()
    {
        var baseline = new[]
        {
            MakeResult("V-001", VerifyStatus.Pass, verifiedAt: _t0),
            MakeResult("V-002", VerifyStatus.Pass, verifiedAt: _t0.AddDays(5))
        };
        var target = new[]
        {
            MakeResult("V-001", VerifyStatus.Pass, verifiedAt: _t1)
        };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.BaselineTimestamp.Should().Be(_t0.AddDays(5));
        diff.TargetTimestamp.Should().Be(_t1);
    }

    // ── ComputeDiff  -  severity summary ────────────────────────────────────────

    [Fact]
    public void ComputeDiff_HighSeverityRegression_CatIRegressionIncremented()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Pass, severity: "high") };
        var target = new[] { MakeResult("V-001", VerifyStatus.Fail, severity: "high") };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.SeveritySummary.CatIRegressions.Should().Be(1);
        diff.SeveritySummary.CatIIRegressions.Should().Be(0);
    }

    [Fact]
    public void ComputeDiff_CatIISeverityLabel_MappedToMedium()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Pass, severity: "cat ii") };
        var target = new[] { MakeResult("V-001", VerifyStatus.Fail, severity: "cat ii") };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.SeveritySummary.CatIIRegressions.Should().Be(1);
        diff.SeveritySummary.CatIRegressions.Should().Be(0);
    }

    [Fact]
    public void ComputeDiff_CatIIISeverityLabel_MappedToLow()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Pass, severity: "cat iii") };
        var target = new[] { MakeResult("V-001", VerifyStatus.Fail, severity: "cat iii") };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.SeveritySummary.CatIIIRegressions.Should().Be(1);
    }

    [Fact]
    public void ComputeDiff_Remediation_NetChangeIsPositive()
    {
        var baseline = new[] { MakeResult("V-001", VerifyStatus.Fail), MakeResult("V-002", VerifyStatus.Fail) };
        var target = new[] { MakeResult("V-001", VerifyStatus.Pass), MakeResult("V-002", VerifyStatus.Pass) };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.SeveritySummary.NetChange.Should().Be(2); // 2 remediations, 0 regressions
    }

    [Fact]
    public void ComputeDiff_MixedSeverityRemediations_AllCategoriesCounted()
    {
        var baseline = new[]
        {
            MakeResult("V-001", VerifyStatus.Fail, severity: "high"),
            MakeResult("V-002", VerifyStatus.Fail, severity: "medium"),
            MakeResult("V-003", VerifyStatus.Fail, severity: "low")
        };
        var target = new[]
        {
            MakeResult("V-001", VerifyStatus.Pass, severity: "high"),
            MakeResult("V-002", VerifyStatus.Pass, severity: "medium"),
            MakeResult("V-003", VerifyStatus.Pass, severity: "low")
        };

        var diff = ComplianceDiffGenerator.ComputeDiff(baseline, target, "base", "target");

        diff.SeveritySummary.CatIRemediations.Should().Be(1);
        diff.SeveritySummary.CatIIRemediations.Should().Be(1);
        diff.SeveritySummary.CatIIIRemediations.Should().Be(1);
    }

    // ── WriteDiffConsole ──────────────────────────────────────────────────────

    [Fact]
    public void WriteDiffConsole_WritesHeaderWithLabels()
    {
        var diff = BuildSimpleDiff("Baseline", "Target");
        var writer = new StringWriter();

        ComplianceDiffGenerator.WriteDiffConsole(diff, writer);

        var output = writer.ToString();
        output.Should().Contain("Baseline").And.Contain("Target");
    }

    [Fact]
    public void WriteDiffConsole_WithRegressions_WritesRegressionSection()
    {
        var regression = new ControlStatusChange
        {
            VulnId = "V-007",
            Severity = "high",
            OldStatus = "Pass",
            NewStatus = "Fail",
            Title = "Some Control"
        };
        var diff = new ComplianceDiff
        {
            BaselineLabel = "base",
            TargetLabel = "target",
            Regressions = new[] { regression },
            Remediations = [],
            Added = [],
            Removed = [],
            SeveritySummary = new DiffSeveritySummary()
        };

        var writer = new StringWriter();
        ComplianceDiffGenerator.WriteDiffConsole(diff, writer);

        writer.ToString().Should().Contain("REGRESSIONS").And.Contain("V-007");
    }

    [Fact]
    public void WriteDiffConsole_WithRemediations_WritesRemediationSection()
    {
        var remediation = new ControlStatusChange
        {
            VulnId = "V-008",
            OldStatus = "Fail",
            NewStatus = "Pass"
        };
        var diff = new ComplianceDiff
        {
            BaselineLabel = "base",
            TargetLabel = "target",
            Regressions = [],
            Remediations = new[] { remediation },
            Added = [],
            Removed = [],
            SeveritySummary = new DiffSeveritySummary()
        };

        var writer = new StringWriter();
        ComplianceDiffGenerator.WriteDiffConsole(diff, writer);

        writer.ToString().Should().Contain("REMEDIATIONS").And.Contain("V-008");
    }

    [Fact]
    public void WriteDiffConsole_WithAddedAndRemoved_WritesCounts()
    {
        var diff = new ComplianceDiff
        {
            BaselineLabel = "base",
            TargetLabel = "target",
            Regressions = [],
            Remediations = [],
            Added = new[] { new ControlStatusChange { VulnId = "V-NEW" } },
            Removed = new[] { new ControlStatusChange { VulnId = "V-OLD" } },
            SeveritySummary = new DiffSeveritySummary()
        };

        var writer = new StringWriter();
        ComplianceDiffGenerator.WriteDiffConsole(diff, writer);

        var output = writer.ToString();
        output.Should().Contain("ADDED").And.Contain("REMOVED");
    }

    // ── WriteDiffJson ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteDiffJson_WritesValidJsonFile()
    {
        using var tempDir = new TempDirectory();
        var outputPath = tempDir.File("diff.json");
        var diff = BuildSimpleDiff("base", "target");

        ComplianceDiffGenerator.WriteDiffJson(diff, outputPath);

        File.Exists(outputPath).Should().BeTrue();
        var json = File.ReadAllText(outputPath);
        json.Should().Contain("baselineLabel");
    }

    [Fact]
    public void WriteDiffJson_CreatesIntermediateDirectories()
    {
        using var tempDir = new TempDirectory();
        var outputPath = Path.Combine(tempDir.Path, "nested", "dir", "diff.json");
        var diff = BuildSimpleDiff("base", "target");

        ComplianceDiffGenerator.WriteDiffJson(diff, outputPath);

        File.Exists(outputPath).Should().BeTrue();
    }

    // ── WriteDiffCsv ──────────────────────────────────────────────────────────

    [Fact]
    public void WriteDiffCsv_WritesCsvWithHeader()
    {
        using var tempDir = new TempDirectory();
        var outputPath = tempDir.File("diff.csv");
        var diff = BuildSimpleDiff("base", "target");

        ComplianceDiffGenerator.WriteDiffCsv(diff, outputPath);

        var csv = File.ReadAllText(outputPath);
        csv.Should().Contain("ChangeType").And.Contain("VulnId");
    }

    [Fact]
    public void WriteDiffCsv_WithChanges_WritesAllChangeTypes()
    {
        using var tempDir = new TempDirectory();
        var outputPath = tempDir.File("diff.csv");
        var diff = new ComplianceDiff
        {
            BaselineLabel = "base",
            TargetLabel = "target",
            Regressions = new[] { new ControlStatusChange { VulnId = "V-R1", OldStatus = "Pass", NewStatus = "Fail" } },
            Remediations = new[] { new ControlStatusChange { VulnId = "V-M1", OldStatus = "Fail", NewStatus = "Pass" } },
            Added = new[] { new ControlStatusChange { VulnId = "V-A1" } },
            Removed = new[] { new ControlStatusChange { VulnId = "V-D1" } },
            SeveritySummary = new DiffSeveritySummary()
        };

        ComplianceDiffGenerator.WriteDiffCsv(diff, outputPath);

        var csv = File.ReadAllText(outputPath);
        csv.Should().Contain("Regression").And.Contain("Remediation").And.Contain("Added").And.Contain("Removed");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NormalizedVerifyResult MakeResult(
        string vulnId,
        VerifyStatus status,
        string? severity = null,
        DateTimeOffset? verifiedAt = null) =>
        new()
        {
            ControlId = vulnId,
            VulnId = vulnId,
            Status = status,
            Severity = severity,
            VerifiedAt = verifiedAt,
            Metadata = new Dictionary<string, string>()
        };

    private static ComplianceDiff BuildSimpleDiff(string baselineLabel, string targetLabel)
    {
        var results = new[] { MakeResult("V-001", VerifyStatus.Pass) };
        return ComplianceDiffGenerator.ComputeDiff(results, results, baselineLabel, targetLabel);
    }
}

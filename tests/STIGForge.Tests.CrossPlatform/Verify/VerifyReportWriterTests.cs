using System.Text.Json;
using FluentAssertions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class VerifyReportWriterTests
{
    // ── WriteJson ──────────────────────────────────────────────────────────────

    [Fact]
    public void WriteJson_CreatesFile()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var report = MakeReport("Tool1",
            MakeResult("SV-001", "V-001", "Pass", "Tool1"),
            MakeResult("SV-002", "V-002", "Open", "Tool1"));

        VerifyReportWriter.WriteJson(path, report);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void WriteJson_ContentIsValidJson()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var report = MakeReport("SCAP", MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"));

        VerifyReportWriter.WriteJson(path, report);

        var json = File.ReadAllText(path);
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void WriteJson_EmptyResults_WritesEmptyArray()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var report = new VerifyReport { Tool = "T", Results = [] };

        VerifyReportWriter.WriteJson(path, report);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Results").GetArrayLength().Should().Be(0);
    }

    // ── WriteCsv ───────────────────────────────────────────────────────────────

    [Fact]
    public void WriteCsv_CreatesFile()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("results.csv");

        VerifyReportWriter.WriteCsv(path, new List<ControlResult>());

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void WriteCsv_HeaderRow_ContainsExpectedColumns()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("results.csv");

        VerifyReportWriter.WriteCsv(path, new List<ControlResult>());

        var firstLine = File.ReadLines(path).First();
        firstLine.Should().Contain("VulnId")
            .And.Contain("RuleId")
            .And.Contain("Status")
            .And.Contain("Tool");
    }

    [Fact]
    public void WriteCsv_WithResults_WritesDataRows()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("results.csv");
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-002", "V-002", "Open", "EvalSTIG")
        };

        VerifyReportWriter.WriteCsv(path, results);

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(3); // header + 2 data rows
        lines[1].Should().Contain("V-001").And.Contain("NotAFinding");
        lines[2].Should().Contain("V-002").And.Contain("Open");
    }

    [Fact]
    public void WriteCsv_CommaInTitle_EscapedWithQuotes()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("results.csv");
        var results = new List<ControlResult>
        {
            new() { RuleId = "SV-001", Title = "Control, with comma", Status = "Pass", Tool = "T" }
        };

        VerifyReportWriter.WriteCsv(path, results);

        var csv = File.ReadAllText(path);
        csv.Should().Contain("\"Control, with comma\"");
    }

    // ── BuildCoverageSummary ───────────────────────────────────────────────────

    [Fact]
    public void BuildCoverageSummary_EmptyResults_ReturnsEmpty()
    {
        var summaries = VerifyReportWriter.BuildCoverageSummary(new List<ControlResult>());
        summaries.Should().BeEmpty();
    }

    [Fact]
    public void BuildCoverageSummary_SingleTool_AllPass_100Percent()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-002", "V-002", "pass", "SCAP")
        };

        var summaries = VerifyReportWriter.BuildCoverageSummary(results);

        summaries.Should().ContainSingle(s => s.Tool == "SCAP");
        summaries[0].ClosedCount.Should().Be(2);
        summaries[0].OpenCount.Should().Be(0);
        summaries[0].ClosedPercent.Should().Be(100);
    }

    [Fact]
    public void BuildCoverageSummary_MixedStatuses_CountsCorrectly()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "Tool1"),
            MakeResult("SV-002", "V-002", "Open", "Tool1"),
            MakeResult("SV-003", "V-003", "not_applicable", "Tool1"),
            MakeResult("SV-004", "V-004", "not reviewed", "Tool1")
        };

        var summaries = VerifyReportWriter.BuildCoverageSummary(results);

        summaries.Should().ContainSingle();
        summaries[0].ClosedCount.Should().Be(2); // NotAFinding + not_applicable
        summaries[0].OpenCount.Should().Be(2);
        summaries[0].TotalCount.Should().Be(4);
    }

    [Fact]
    public void BuildCoverageSummary_MultipleTools_GroupedSeparately()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-002", "V-002", "NotAFinding", "EvalSTIG"),
            MakeResult("SV-003", "V-003", "Open", "EvalSTIG")
        };

        var summaries = VerifyReportWriter.BuildCoverageSummary(results);

        summaries.Should().HaveCount(2);
        summaries.Should().Contain(s => s.Tool == "SCAP" && s.TotalCount == 1);
        summaries.Should().Contain(s => s.Tool == "EvalSTIG" && s.TotalCount == 2);
    }

    [Fact]
    public void BuildCoverageSummary_NullOrEmptyTool_GroupedAsUnknown()
    {
        var results = new List<ControlResult>
        {
            new() { RuleId = "SV-001", Status = "NotAFinding", Tool = string.Empty }
        };

        var summaries = VerifyReportWriter.BuildCoverageSummary(results);

        summaries.Should().ContainSingle(s => s.Tool == "Unknown");
    }

    // ── BuildControlSourceMap ──────────────────────────────────────────────────

    [Fact]
    public void BuildControlSourceMap_ByRuleId_GroupsCorrectly()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-001", "V-001", "Open", "EvalSTIG")
        };

        var maps = VerifyReportWriter.BuildControlSourceMap(results);

        maps.Should().ContainSingle(m => m.ControlKey == "RULE:SV-001");
        maps[0].SourcesKey.Should().Contain("SCAP").And.Contain("EvalSTIG");
    }

    [Fact]
    public void BuildControlSourceMap_ByVulnIdWhenNoRuleId_GroupsCorrectly()
    {
        var results = new List<ControlResult>
        {
            new() { RuleId = null, VulnId = "V-100", Status = "NotAFinding", Tool = "T" }
        };

        var maps = VerifyReportWriter.BuildControlSourceMap(results);

        maps.Should().ContainSingle(m => m.ControlKey == "VULN:V-100");
    }

    [Fact]
    public void BuildControlSourceMap_ByTitleWhenNoRuleOrVulnId_GroupsCorrectly()
    {
        var results = new List<ControlResult>
        {
            new() { RuleId = null, VulnId = null, Title = "My Control", Status = "Open", Tool = "T" }
        };

        var maps = VerifyReportWriter.BuildControlSourceMap(results);

        maps.Should().ContainSingle(m => m.ControlKey.StartsWith("TITLE:"));
    }

    [Fact]
    public void BuildControlSourceMap_AllClosed_IsClosedTrue()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-001", "V-001", "pass", "EvalSTIG")
        };

        var maps = VerifyReportWriter.BuildControlSourceMap(results);

        maps.Should().ContainSingle().Which.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void BuildControlSourceMap_AnyOpen_IsClosedFalse()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-001", "V-001", "Open", "EvalSTIG")
        };

        var maps = VerifyReportWriter.BuildControlSourceMap(results);

        maps.Should().ContainSingle().Which.IsClosed.Should().BeFalse();
    }

    // ── BuildOverlapSummary ────────────────────────────────────────────────────

    [Fact]
    public void BuildOverlapSummary_EmptyResults_ReturnsEmpty()
    {
        var overlaps = VerifyReportWriter.BuildOverlapSummary(new List<ControlResult>());
        overlaps.Should().BeEmpty();
    }

    [Fact]
    public void BuildOverlapSummary_SingleSourceControl_SourceCount1()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP")
        };

        var overlaps = VerifyReportWriter.BuildOverlapSummary(results);

        overlaps.Should().ContainSingle();
        overlaps[0].SourceCount.Should().Be(1);
        overlaps[0].ControlsCount.Should().Be(1);
    }

    [Fact]
    public void BuildOverlapSummary_TwoSourcesSameControl_SourceCount2()
    {
        var results = new List<ControlResult>
        {
            MakeResult("SV-001", "V-001", "NotAFinding", "SCAP"),
            MakeResult("SV-001", "V-001", "NotAFinding", "EvalSTIG")
        };

        var overlaps = VerifyReportWriter.BuildOverlapSummary(results);

        overlaps.Should().ContainSingle();
        overlaps[0].SourcesKey.Should().Contain("|");
        overlaps[0].SourceCount.Should().Be(2);
    }

    // ── WriteOverlapSummary ────────────────────────────────────────────────────

    [Fact]
    public void WriteOverlapSummary_CreatesBothCsvAndJsonFiles()
    {
        using var tmp = new TempDirectory();
        var csvPath = tmp.File("overlap.csv");
        var jsonPath = tmp.File("overlap.json");
        var overlaps = new List<CoverageOverlap>
        {
            new() { SourcesKey = "SCAP|EvalSTIG", SourceCount = 2, ControlsCount = 5, ClosedCount = 3, OpenCount = 2 }
        };

        VerifyReportWriter.WriteOverlapSummary(csvPath, jsonPath, overlaps);

        File.Exists(csvPath).Should().BeTrue();
        File.Exists(jsonPath).Should().BeTrue();
    }

    [Fact]
    public void WriteOverlapSummary_CsvHasCorrectHeaders()
    {
        using var tmp = new TempDirectory();
        var csvPath = tmp.File("overlap.csv");
        var jsonPath = tmp.File("overlap.json");

        VerifyReportWriter.WriteOverlapSummary(csvPath, jsonPath, new List<CoverageOverlap>());

        var header = File.ReadLines(csvPath).First();
        header.Should().Contain("SourcesKey").And.Contain("SourceCount").And.Contain("ControlsCount");
    }

    [Fact]
    public void WriteOverlapSummary_JsonIsValidAndContainsData()
    {
        using var tmp = new TempDirectory();
        var overlaps = new List<CoverageOverlap>
        {
            new() { SourcesKey = "SCAP", SourceCount = 1, ControlsCount = 10, ClosedCount = 8, OpenCount = 2 }
        };

        VerifyReportWriter.WriteOverlapSummary(tmp.File("o.csv"), tmp.File("o.json"), overlaps);

        var json = File.ReadAllText(tmp.File("o.json"));
        json.Should().Contain("SCAP");
    }

    // ── WriteControlSourceMap ──────────────────────────────────────────────────

    [Fact]
    public void WriteControlSourceMap_CreatesFile()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("map.csv");
        var maps = new List<ControlSourceMap>
        {
            new() { ControlKey = "RULE:SV-001", VulnId = "V-001", RuleId = "SV-001", SourcesKey = "SCAP", IsClosed = true }
        };

        VerifyReportWriter.WriteControlSourceMap(path, maps);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void WriteControlSourceMap_HeaderContainsExpectedColumns()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("map.csv");

        VerifyReportWriter.WriteControlSourceMap(path, new List<ControlSourceMap>());

        var header = File.ReadLines(path).First();
        header.Should().Contain("ControlKey")
            .And.Contain("SourcesKey")
            .And.Contain("IsClosed");
    }

    [Fact]
    public void WriteControlSourceMap_DataRowContainsIsClosed()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("map.csv");
        var maps = new List<ControlSourceMap>
        {
            new() { ControlKey = "RULE:SV-001", SourcesKey = "SCAP", IsClosed = true }
        };

        VerifyReportWriter.WriteControlSourceMap(path, maps);

        var content = File.ReadAllText(path);
        content.Should().Contain("true");
    }

    // ── WriteCoverageSummary ───────────────────────────────────────────────────

    [Fact]
    public void WriteCoverageSummary_CreatesBothFiles()
    {
        using var tmp = new TempDirectory();
        var csvPath = tmp.File("cov.csv");
        var jsonPath = tmp.File("cov.json");
        var summaries = new List<CoverageSummary>
        {
            new() { Tool = "SCAP", ClosedCount = 8, OpenCount = 2, TotalCount = 10, ClosedPercent = 80 }
        };

        VerifyReportWriter.WriteCoverageSummary(csvPath, jsonPath, summaries);

        File.Exists(csvPath).Should().BeTrue();
        File.Exists(jsonPath).Should().BeTrue();
    }

    [Fact]
    public void WriteCoverageSummary_CsvHasCorrectHeaders()
    {
        using var tmp = new TempDirectory();
        var csvPath = tmp.File("cov.csv");
        var jsonPath = tmp.File("cov.json");

        VerifyReportWriter.WriteCoverageSummary(csvPath, jsonPath, new List<CoverageSummary>());

        var header = File.ReadLines(csvPath).First();
        header.Should().Contain("Tool")
            .And.Contain("ClosedCount")
            .And.Contain("TotalCount")
            .And.Contain("ClosedPercent");
    }

    [Fact]
    public void WriteCoverageSummary_DataRowContainsToolName()
    {
        using var tmp = new TempDirectory();
        var csvPath = tmp.File("cov.csv");
        var jsonPath = tmp.File("cov.json");
        var summaries = new List<CoverageSummary>
        {
            new() { Tool = "EvalSTIG", ClosedCount = 5, OpenCount = 5, TotalCount = 10, ClosedPercent = 50 }
        };

        VerifyReportWriter.WriteCoverageSummary(csvPath, jsonPath, summaries);

        var csv = File.ReadAllText(csvPath);
        csv.Should().Contain("EvalSTIG").And.Contain("50");
    }

    // ── IsClosed (exercised indirectly) ───────────────────────────────────────

    [Theory]
    [InlineData("NotAFinding", true)]
    [InlineData("pass", true)]
    [InlineData("not_applicable", true)]
    [InlineData("not applicable", true)]
    [InlineData("Open", false)]
    [InlineData("fail", false)]
    [InlineData("not_reviewed", false)]
    [InlineData("not reviewed", false)]
    [InlineData("unknown_status", false)]
    [InlineData(null, false)]
    public void BuildCoverageSummary_StatusMappings_MatchIsClosed(string? status, bool expectClosed)
    {
        var results = new List<ControlResult>
        {
            new() { RuleId = "SV-001", Status = status, Tool = "T" }
        };

        var summaries = VerifyReportWriter.BuildCoverageSummary(results);

        if (expectClosed)
            summaries.Single().ClosedCount.Should().Be(1);
        else
            summaries.Single().OpenCount.Should().Be(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ControlResult MakeResult(string ruleId, string vulnId, string status, string tool) =>
        new()
        {
            RuleId = ruleId,
            VulnId = vulnId,
            Status = status,
            Tool = tool,
            Title = $"Test Control {ruleId}",
            Severity = "medium",
            SourceFile = "test.ckl",
            VerifiedAt = DateTimeOffset.UtcNow
        };

    private static VerifyReport MakeReport(string tool, params ControlResult[] results) =>
        new()
        {
            Tool = tool,
            ToolVersion = "1.0",
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            OutputRoot = "/tmp",
            Results = results.ToList()
        };
}

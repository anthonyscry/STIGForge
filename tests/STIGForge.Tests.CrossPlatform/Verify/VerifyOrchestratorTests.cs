using System.Text.Json;
using FluentAssertions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class VerifyOrchestratorTests
{
    private static NormalizedVerifyResult MakeResult(string controlId, VerifyStatus status, string tool = "SCAP") =>
        new()
        {
            ControlId = controlId,
            Status = status,
            Tool = tool,
            SourceFile = "test.xml",
            EvidencePaths = [],
            Metadata = new Dictionary<string, string>()
        };

    private static NormalizedVerifyReport MakeReport(params NormalizedVerifyResult[] results) =>
        new()
        {
            Tool = "SCAP",
            Results = results,
            DiagnosticMessages = []
        };

    // ── SaveReport ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveReport_WritesFileToExpectedPath()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var report = new ConsolidatedVerifyReport { Results = [] };
        var orchestrator = new VerifyOrchestrator();

        await orchestrator.SaveReport(report, path);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task SaveReport_FileContentMatchesReport()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var result = MakeResult("V-001", VerifyStatus.Pass);
        var report = new ConsolidatedVerifyReport { Results = [result] };
        var orchestrator = new VerifyOrchestrator();

        await orchestrator.SaveReport(report, path);

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var resultsEl = doc.RootElement.GetProperty("results");
        resultsEl.GetArrayLength().Should().Be(1);
        resultsEl[0].GetProperty("controlId").GetString().Should().Be("V-001");
    }

    [Fact]
    public async Task SaveReport_OverwritesExistingFile()
    {
        using var tmp = new TempDirectory();
        var path = tmp.File("report.json");
        var orchestrator = new VerifyOrchestrator();

        var first = new ConsolidatedVerifyReport { Results = [MakeResult("V-001", VerifyStatus.Pass)] };
        await orchestrator.SaveReport(first, path);

        var second = new ConsolidatedVerifyReport { Results = [MakeResult("V-002", VerifyStatus.Fail)] };
        await orchestrator.SaveReport(second, path);

        var json = await File.ReadAllTextAsync(path);
        json.Should().Contain("V-002");
        json.Should().NotContain("V-001");
    }

    // ── MergeReports ───────────────────────────────────────────────────────────

    [Fact]
    public void MergeReports_DeduplicatesById()
    {
        var reportA = MakeReport(MakeResult("V-100", VerifyStatus.Pass, "SCAP"));
        var reportB = MakeReport(MakeResult("V-100", VerifyStatus.Pass, "Evaluate-STIG"));
        var orchestrator = new VerifyOrchestrator();

        var merged = orchestrator.MergeReports([reportA, reportB], []);

        merged.Results.Should().HaveCount(1, because: "duplicate control IDs should be merged into one entry");
        merged.Results[0].ControlId.Should().Be("V-100");
    }

    [Fact]
    public void MergeReports_PreservesAllUniqueEntries()
    {
        var reportA = MakeReport(MakeResult("V-100", VerifyStatus.Pass));
        var reportB = MakeReport(MakeResult("V-200", VerifyStatus.Fail));
        var orchestrator = new VerifyOrchestrator();

        var merged = orchestrator.MergeReports([reportA, reportB], []);

        merged.Results.Should().HaveCount(2);
        merged.Results.Select(r => r.ControlId).Should().BeEquivalentTo(["V-100", "V-200"]);
    }

    [Fact]
    public void MergeReports_EmptyInput_ReturnsEmptyList()
    {
        var orchestrator = new VerifyOrchestrator();

        var merged = orchestrator.MergeReports([], []);

        merged.Results.Should().BeEmpty();
        merged.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void MergeReports_ConflictingStatus_CklWinsOverScap()
    {
        var scapResult = MakeResult("V-100", VerifyStatus.Pass, "SCAP");
        var cklResult = MakeResult("V-100", VerifyStatus.Fail, "Manual CKL");
        var report = MakeReport(scapResult, cklResult);
        var orchestrator = new VerifyOrchestrator();

        var merged = orchestrator.MergeReports([report], []);

        merged.Results.Should().HaveCount(1);
        merged.Results[0].Status.Should().Be(VerifyStatus.Fail, because: "CKL has higher precedence than SCAP");
        merged.Conflicts.Should().HaveCount(1);
    }

    [Fact]
    public void MergeReports_DiagnosticErrorsIncluded()
    {
        var orchestrator = new VerifyOrchestrator();
        var errors = new[] { "File not found: foo.xml" };

        var merged = orchestrator.MergeReports([], errors);

        merged.DiagnosticMessages.Should().Contain("File not found: foo.xml");
    }
}

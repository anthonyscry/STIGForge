using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Tests.CrossPlatform.Helpers;
using STIGForge.Verify;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class VerificationWorkflowServiceTests
{
    private static VerificationWorkflowService BuildService()
    {
        var processRunner = new Mock<IProcessRunner>().Object;
        return new(new EvaluateStigRunner(processRunner), new ScapRunner(processRunner));
    }

    // ── argument guards ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        var svc = BuildService();

        Func<Task> act = () => svc.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_ThrowsArgumentException_WhenOutputRootIsEmpty()
    {
        var svc = BuildService();
        var request = new VerificationWorkflowRequest { OutputRoot = string.Empty };

        Func<Task> act = () => svc.RunAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OutputRoot*");
    }

    [Fact]
    public async Task RunAsync_ThrowsOperationCanceledException_WhenTokenPreCancelled()
    {
        var svc = BuildService();
        using var tmp = new TempDirectory();
        var request = new VerificationWorkflowRequest
        {
            OutputRoot = tmp.Path,
            EvaluateStig = new EvaluateStigWorkflowOptions { Enabled = false },
            Scap = new ScapWorkflowOptions { Enabled = false }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => svc.RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── both tools disabled ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_BothToolsDisabled_CreatesConsolidatedJsonFile()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        File.Exists(result.ConsolidatedJsonPath).Should().BeTrue();
        result.ConsolidatedJsonPath.Should().EndWith("consolidated-results.json");
    }

    [Fact]
    public async Task RunAsync_BothToolsDisabled_CreatesConsolidatedCsvFile()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        File.Exists(result.ConsolidatedCsvPath).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_BothToolsDisabled_CreatesCoverageFiles()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        File.Exists(result.CoverageSummaryJsonPath).Should().BeTrue();
        File.Exists(result.CoverageSummaryCsvPath).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_BothToolsDisabled_ReturnsTwoSkippedToolRuns()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.ToolRuns.Should().HaveCount(2);
        result.ToolRuns.Should().AllSatisfy(r => r.Executed.Should().BeFalse());
    }

    [Fact]
    public async Task RunAsync_BothToolsDisabled_AddsDiagnostic_WhenNoResultsFound()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("No CKL results were found"));
    }

    [Fact]
    public async Task RunAsync_BothToolsDisabled_ReturnsZeroConsolidatedResultCount()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.ConsolidatedResultCount.Should().Be(0);
    }

    // ── custom tool label ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UsesConsolidatedToolLabel_FromRequest()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);
        request.ConsolidatedToolLabel = "CustomLabel";

        var result = await svc.RunAsync(request, CancellationToken.None);

        var json = File.ReadAllText(result.ConsolidatedJsonPath);
        json.Should().Contain("CustomLabel");
    }

    // ── SCAP misconfiguration diagnostics ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_AddsDiagnostic_WhenScapEnabledButCommandPathEmpty()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);
        request.Scap = new ScapWorkflowOptions
        {
            Enabled = true,
            CommandPath = string.Empty,
            Arguments = "--some-args"
        };

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("CommandPath") || d.Contains("command path"));
    }

    [Fact]
    public async Task RunAsync_AddsDiagnostic_WhenScapEnabledButArgumentsEmpty()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);
        request.Scap = new ScapWorkflowOptions
        {
            Enabled = true,
            CommandPath = "/usr/bin/scc",
            Arguments = string.Empty
        };

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("arguments"));
    }

    [Fact]
    public async Task RunAsync_AddsDiagnostic_WhenEvaluateStigEnabledButToolRootEmpty()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);
        request.EvaluateStig = new EvaluateStigWorkflowOptions
        {
            Enabled = true,
            ToolRoot = string.Empty
        };

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.Diagnostics.Should().Contain(d => d.Contains("ToolRoot") || d.Contains("tool root"));
    }

    // ── static helper ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildConsolidatedJsonPath_ReturnsCorrectFileName()
    {
        var path = VerificationWorkflowService.BuildConsolidatedJsonPath("/output/dir");

        path.Should().EndWith("consolidated-results.json");
        path.Should().StartWith("/output/dir");
    }

    // ── timestamp sanity ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_StartedAtBeforeFinishedAt()
    {
        using var tmp = new TempDirectory();
        var svc = BuildService();
        var request = BuildBothDisabledRequest(tmp.Path);

        var result = await svc.RunAsync(request, CancellationToken.None);

        result.StartedAt.Should().BeOnOrBefore(result.FinishedAt);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static VerificationWorkflowRequest BuildBothDisabledRequest(string outputRoot) =>
        new()
        {
            OutputRoot = outputRoot,
            EvaluateStig = new EvaluateStigWorkflowOptions { Enabled = false },
            Scap = new ScapWorkflowOptions { Enabled = false }
        };
}

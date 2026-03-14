using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class VerificationWorkflowServiceAdditionalTests : IDisposable
{
    private readonly string _tempDir;

    public VerificationWorkflowServiceAdditionalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-vwf-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());

        var act = async () => await service.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public async Task RunAsync_EmptyOutputRoot_ThrowsArgumentException()
    {
        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var request = new VerificationWorkflowRequest { OutputRoot = "   " };

        var act = async () => await service.RunAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OutputRoot*");
    }

    [Fact]
    public async Task RunAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new VerificationWorkflowRequest
        {
            OutputRoot = Path.Combine(_tempDir, "output")
        };

        var act = async () => await service.RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── EvaluateStig exception path ──────────────────────────────────────────

    [Fact]
    public async Task RunAsync_EvaluateStigEnabledWithNonExistentToolRoot_CatchesExceptionAndAddsDiagnostic()
    {
        var outputRoot = Path.Combine(_tempDir, "evaluatestig-exc");
        Directory.CreateDirectory(outputRoot);

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var request = new VerificationWorkflowRequest
        {
            OutputRoot = outputRoot,
            EvaluateStig = new EvaluateStigWorkflowOptions
            {
                Enabled = true,
                ToolRoot = Path.Combine(_tempDir, "definitely", "not", "there")
            }
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        result.ToolRuns.Should().ContainSingle(r =>
            r.Tool == "Evaluate-STIG" && !r.Executed && r.ExitCode == -1);
        result.Diagnostics.Should().Contain(d => d.Contains("Evaluate-STIG execution failed", StringComparison.Ordinal));
    }

    // ── SCAP exception path (unsupported GUI binary) ──────────────────────────

    [Fact]
    public async Task RunAsync_ScapWithUnsupportedGuiBinary_CatchesExceptionAndAddsDiagnostic()
    {
        var outputRoot = Path.Combine(_tempDir, "scap-gui-exc");
        Directory.CreateDirectory(outputRoot);

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var request = new VerificationWorkflowRequest
        {
            OutputRoot = outputRoot,
            Scap = new ScapWorkflowOptions
            {
                Enabled = true,
                CommandPath = "scc.exe",  // unsupported GUI binary → throws before process starts
                Arguments = "--scan something",
                ToolLabel = "SCC"
            }
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        result.ToolRuns.Should().ContainSingle(r =>
            r.Tool == "SCC" && !r.Executed && r.ExitCode == -1);
        result.Diagnostics.Should().Contain(d => d.Contains("SCC execution failed", StringComparison.Ordinal));
    }

    // ── SCAP tool label defaults ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ScapEnabledWithNoToolLabel_DefaultsToScapLabel()
    {
        var outputRoot = Path.Combine(_tempDir, "scap-default-label");
        Directory.CreateDirectory(outputRoot);

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var request = new VerificationWorkflowRequest
        {
            OutputRoot = outputRoot,
            Scap = new ScapWorkflowOptions
            {
                Enabled = true,
                CommandPath = string.Empty,
                ToolLabel = string.Empty  // empty → defaults to "SCAP"
            }
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        result.ToolRuns.Should().ContainSingle(r =>
            string.Equals(r.Tool, "SCAP", StringComparison.OrdinalIgnoreCase));
    }

    // ── BuildConsolidatedJsonPath static helper ───────────────────────────────

    [Theory]
    [InlineData(@"C:\output", @"C:\output\consolidated-results.json")]
    [InlineData("/tmp/output", "/tmp/output/consolidated-results.json")]
    public void BuildConsolidatedJsonPath_ReturnsExpectedPath(string outputRoot, string expected)
    {
        var result = VerificationWorkflowService.BuildConsolidatedJsonPath(outputRoot);

        result.Should().Be(expected);
    }

    // ── ConsolidatedToolLabel is used when set ────────────────────────────────

    [Fact]
    public async Task RunAsync_WithCustomConsolidatedLabel_LabelIsUsedInReport()
    {
        var outputRoot = Path.Combine(_tempDir, "custom-label");
        Directory.CreateDirectory(outputRoot);
        WriteCkl(Path.Combine(outputRoot, "sample.ckl"));

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var request = new VerificationWorkflowRequest
        {
            OutputRoot = outputRoot,
            ConsolidatedToolLabel = "MyCustomLabel"
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        // Verify the JSON was written (report used the tool label)
        File.Exists(result.ConsolidatedJsonPath).Should().BeTrue();
        var json = File.ReadAllText(result.ConsolidatedJsonPath);
        json.Should().Contain("MyCustomLabel");
    }

    // ── timing fields populated ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Always_PopulatesStartedAtAndFinishedAt()
    {
        var outputRoot = Path.Combine(_tempDir, "timing");
        Directory.CreateDirectory(outputRoot);

        var before = DateTimeOffset.Now;
        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);
        var after = DateTimeOffset.Now;

        result.StartedAt.Should().BeOnOrBefore(after);
        result.FinishedAt.Should().BeOnOrAfter(before);
    }

    // ── "critical" severity maps to CatI ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithCriticalSeverityOpen_CountsAsCatI()
    {
        var outputRoot = Path.Combine(_tempDir, "critical-cat");
        Directory.CreateDirectory(outputRoot);
        WriteCkl(Path.Combine(outputRoot, "crit.ckl"), status: "Open", severity: "critical", vulnId: "V-9900", ruleId: "SV-9900");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.CatICount.Should().Be(1);
    }

    // ── status aliases ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PassAliases_CountAsPassCount()
    {
        var outputRoot = Path.Combine(_tempDir, "pass-aliases");
        Directory.CreateDirectory(outputRoot);

        WriteCkl(Path.Combine(outputRoot, "compliant.ckl"), status: "Compliant", vulnId: "V-PA1", ruleId: "SV-PA1");
        WriteCkl(Path.Combine(outputRoot, "pass.ckl"), status: "Pass", vulnId: "V-PA2", ruleId: "SV-PA2");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.PassCount.Should().Be(2);
        result.FailCount.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_FailAliases_CountAsFailCount()
    {
        var outputRoot = Path.Combine(_tempDir, "fail-aliases");
        Directory.CreateDirectory(outputRoot);

        WriteCkl(Path.Combine(outputRoot, "noncompliant.ckl"), status: "NonCompliant", vulnId: "V-FA1", ruleId: "SV-FA1");
        WriteCkl(Path.Combine(outputRoot, "fail.ckl"), status: "Fail", vulnId: "V-FA2", ruleId: "SV-FA2");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.FailCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_NotReviewedAliases_CountAsNotReviewedCount()
    {
        var outputRoot = Path.Combine(_tempDir, "notreviewed-aliases");
        Directory.CreateDirectory(outputRoot);

        WriteCkl(Path.Combine(outputRoot, "notchecked.ckl"), status: "NotChecked", vulnId: "V-NR1", ruleId: "SV-NR1");
        WriteCkl(Path.Combine(outputRoot, "notselected.ckl"), status: "NotSelected", vulnId: "V-NR2", ruleId: "SV-NR2");
        WriteCkl(Path.Combine(outputRoot, "unknown.ckl"), status: "Unknown", vulnId: "V-NR3", ruleId: "SV-NR3");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.NotReviewedCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_NaAlias_CountsAsNotApplicable()
    {
        var outputRoot = Path.Combine(_tempDir, "na-alias");
        Directory.CreateDirectory(outputRoot);

        WriteCkl(Path.Combine(outputRoot, "na.ckl"), status: "NA", vulnId: "V-NA1", ruleId: "SV-NA1");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.NotApplicableCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_EmptyStatus_CountsAsNotReviewed()
    {
        var outputRoot = Path.Combine(_tempDir, "empty-status");
        Directory.CreateDirectory(outputRoot);

        WriteCkl(Path.Combine(outputRoot, "empty.ckl"), status: "", vulnId: "V-ES1", ruleId: "SV-ES1");

        var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
        var result = await service.RunAsync(new VerificationWorkflowRequest { OutputRoot = outputRoot }, CancellationToken.None);

        result.NotReviewedCount.Should().Be(1);
    }

    private static void WriteCkl(
        string filePath,
        string status = "NotAFinding",
        string severity = "medium",
        string? vulnId = "V-9000",
        string? ruleId = "SV-9000")
    {
        File.WriteAllText(filePath, $"""
<CHECKLIST>
  <VULN>
    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{vulnId}</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{ruleId}</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Sample Control</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>{severity}</ATTRIBUTE_DATA></STIG_DATA>
    <STATUS>{status}</STATUS>
    <FINDING_DETAILS>none</FINDING_DETAILS>
    <COMMENTS>ok</COMMENTS>
  </VULN>
</CHECKLIST>
""");
    }
}

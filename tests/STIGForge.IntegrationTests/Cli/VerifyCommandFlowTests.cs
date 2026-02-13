using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Verify;

namespace STIGForge.IntegrationTests.Cli;

public sealed class VerifyCommandFlowTests : IDisposable
{
  private readonly string _tempDir;

  public VerifyCommandFlowTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-flow-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public async Task CliAndAppStyleRequests_ProduceEquivalentConsolidatedArtifacts()
  {
    var workflow = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());

    var cliRoot = Path.Combine(_tempDir, "cli");
    var appRoot = Path.Combine(_tempDir, "app");
    Directory.CreateDirectory(cliRoot);
    Directory.CreateDirectory(appRoot);

    WriteSampleCkl(Path.Combine(cliRoot, "sample.ckl"));
    WriteSampleCkl(Path.Combine(appRoot, "sample.ckl"));

    var cliResult = await workflow.RunAsync(new VerificationWorkflowRequest
    {
      OutputRoot = cliRoot,
      ConsolidatedToolLabel = "Evaluate-STIG"
    }, CancellationToken.None);

    var appResult = await workflow.RunAsync(new VerificationWorkflowRequest
    {
      OutputRoot = appRoot,
      ConsolidatedToolLabel = "Evaluate-STIG"
    }, CancellationToken.None);

    cliResult.ConsolidatedResultCount.Should().Be(1);
    appResult.ConsolidatedResultCount.Should().Be(1);

    var cliReport = VerifyReportReader.LoadFromJson(cliResult.ConsolidatedJsonPath);
    var appReport = VerifyReportReader.LoadFromJson(appResult.ConsolidatedJsonPath);

    cliReport.Results.Count.Should().Be(appReport.Results.Count);
    cliReport.Results[0].VulnId.Should().Be(appReport.Results[0].VulnId);
    cliReport.Results[0].Status.Should().Be(appReport.Results[0].Status);

    File.Exists(cliResult.ConsolidatedCsvPath).Should().BeTrue();
    File.Exists(appResult.ConsolidatedCsvPath).Should().BeTrue();
    File.Exists(cliResult.CoverageSummaryJsonPath).Should().BeTrue();
    File.Exists(appResult.CoverageSummaryJsonPath).Should().BeTrue();
  }

  [Fact]
  public async Task WorkflowService_WithMissingConfiguredTools_ReturnsDiagnosticsAndArtifacts()
  {
    var outputRoot = Path.Combine(_tempDir, "diagnostics");
    Directory.CreateDirectory(outputRoot);
    WriteSampleCkl(Path.Combine(outputRoot, "sample.ckl"));

    var workflow = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
    var result = await workflow.RunAsync(new VerificationWorkflowRequest
    {
      OutputRoot = outputRoot,
      ConsolidatedToolLabel = "Verification",
      EvaluateStig = new EvaluateStigWorkflowOptions
      {
        Enabled = true,
        ToolRoot = Path.Combine(_tempDir, "missing-eval")
      },
      Scap = new ScapWorkflowOptions
      {
        Enabled = true,
        CommandPath = Path.Combine(_tempDir, "missing-scap.exe"),
        ToolLabel = "SCAP"
      }
    }, CancellationToken.None);

    result.ConsolidatedResultCount.Should().Be(1);
    result.Diagnostics.Should().Contain(d => d.Contains("execution failed", StringComparison.OrdinalIgnoreCase));
    result.ToolRuns.Should().HaveCount(2);
    result.ToolRuns.Should().OnlyContain(r => !r.Executed);
    File.Exists(result.ConsolidatedJsonPath).Should().BeTrue();
  }

  private static void WriteSampleCkl(string path)
  {
    File.WriteAllText(path, """
<CHECKLIST>
  <VULN>
    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-1111</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-1111</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Sample Verify Flow</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>medium</ATTRIBUTE_DATA></STIG_DATA>
    <STATUS>NotAFinding</STATUS>
    <COMMENTS>ok</COMMENTS>
  </VULN>
</CHECKLIST>
""");
  }
}

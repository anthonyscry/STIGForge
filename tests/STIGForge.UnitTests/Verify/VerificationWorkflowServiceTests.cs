using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class VerificationWorkflowServiceTests : IDisposable
{
  private readonly string _tempDir;

  public VerificationWorkflowServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-workflow-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public async Task RunAsync_WithExistingCklAndNoTools_WritesConsolidatedArtifacts()
  {
    var outputRoot = Path.Combine(_tempDir, "output");
    Directory.CreateDirectory(outputRoot);
    WriteCkl(Path.Combine(outputRoot, "sample.ckl"));

    var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
    var request = new VerificationWorkflowRequest
    {
      OutputRoot = outputRoot,
      ConsolidatedToolLabel = "Verification"
    };

    var result = await service.RunAsync(request, CancellationToken.None);

    result.ConsolidatedResultCount.Should().Be(1);
    result.ConsolidatedJsonPath.Should().Be(Path.Combine(outputRoot, "consolidated-results.json"));
    result.ConsolidatedCsvPath.Should().Be(Path.Combine(outputRoot, "consolidated-results.csv"));
    result.CoverageSummaryJsonPath.Should().Be(Path.Combine(outputRoot, "coverage_summary.json"));
    result.CoverageSummaryCsvPath.Should().Be(Path.Combine(outputRoot, "coverage_summary.csv"));

    File.Exists(result.ConsolidatedJsonPath).Should().BeTrue();
    File.Exists(result.ConsolidatedCsvPath).Should().BeTrue();
    File.Exists(result.CoverageSummaryJsonPath).Should().BeTrue();
    File.Exists(result.CoverageSummaryCsvPath).Should().BeTrue();

    result.ToolRuns.Should().HaveCount(2);
    result.ToolRuns.Should().OnlyContain(r => !r.Executed);
    result.Diagnostics.Should().NotContain(d => d.Contains("No CKL results", StringComparison.Ordinal));
  }

  [Fact]
  public async Task RunAsync_WithNoCkls_AddsNoResultsDiagnostic()
  {
    var outputRoot = Path.Combine(_tempDir, "empty-output");
    Directory.CreateDirectory(outputRoot);

    var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
    var request = new VerificationWorkflowRequest
    {
      OutputRoot = outputRoot
    };

    var result = await service.RunAsync(request, CancellationToken.None);

    result.ConsolidatedResultCount.Should().Be(0);
    result.Diagnostics.Should().Contain(d => d.Contains("No CKL results", StringComparison.Ordinal));
    File.Exists(result.ConsolidatedJsonPath).Should().BeTrue();
    File.Exists(result.ConsolidatedCsvPath).Should().BeTrue();
  }

  [Fact]
  public async Task RunAsync_WithEnabledToolsMissingConfiguration_CapturesDiagnostics()
  {
    var outputRoot = Path.Combine(_tempDir, "diag-output");
    Directory.CreateDirectory(outputRoot);

    var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
    var request = new VerificationWorkflowRequest
    {
      OutputRoot = outputRoot,
      EvaluateStig = new EvaluateStigWorkflowOptions
      {
        Enabled = true,
        ToolRoot = string.Empty
      },
      Scap = new ScapWorkflowOptions
      {
        Enabled = true,
        CommandPath = string.Empty,
        ToolLabel = "SCC"
      }
    };

    var result = await service.RunAsync(request, CancellationToken.None);

    result.ToolRuns.Should().HaveCount(2);
    result.ToolRuns.Should().OnlyContain(r => !r.Executed);
    result.Diagnostics.Should().Contain(d => d.Contains("Evaluate-STIG enabled", StringComparison.Ordinal));
    result.Diagnostics.Should().Contain(d => d.Contains("SCAP enabled", StringComparison.Ordinal));
  }

  [Fact]
  public async Task RunAsync_WithScapEnabledAndEmptyArguments_AddsScapArgumentDiagnostic()
  {
    var outputRoot = Path.Combine(_tempDir, "scap-empty-args");
    Directory.CreateDirectory(outputRoot);

    var service = new VerificationWorkflowService(new EvaluateStigRunner(), new ScapRunner());
    var request = new VerificationWorkflowRequest
    {
      OutputRoot = outputRoot,
      Scap = new ScapWorkflowOptions
      {
        Enabled = true,
        CommandPath = "cscc.exe",
        Arguments = "   ",
        ToolLabel = "SCC"
      }
    };

    var result = await service.RunAsync(request, CancellationToken.None);

    result.ToolRuns.Should().ContainSingle(r => string.Equals(r.Tool, "SCC", StringComparison.OrdinalIgnoreCase)
      && !r.Executed);
    result.Diagnostics.Should().Contain(d => d.Contains("arguments were empty", StringComparison.OrdinalIgnoreCase));
  }

  private static void WriteCkl(string filePath)
  {
    File.WriteAllText(filePath, """
<CHECKLIST>
  <VULN>
    <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-9000</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-9000</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Sample Control</ATTRIBUTE_DATA></STIG_DATA>
    <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>medium</ATTRIBUTE_DATA></STIG_DATA>
    <STATUS>NotAFinding</STATUS>
    <FINDING_DETAILS>none</FINDING_DETAILS>
    <COMMENTS>ok</COMMENTS>
  </VULN>
</CHECKLIST>
""");
  }
}

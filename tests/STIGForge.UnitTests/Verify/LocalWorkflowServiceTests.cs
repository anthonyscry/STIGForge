using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class LocalWorkflowServiceTests : IDisposable
{
  private readonly string _tempRoot;

  public LocalWorkflowServiceTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-local-workflow-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task RunAsync_OrchestratesImportAndScanAndWritesMissionArtifact()
  {
    var importRoot = Path.Combine(_tempRoot, "import");
    var outputRoot = Path.Combine(_tempRoot, "output");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);

    await WriteImportZipAsync(importRoot, "SV-1000r1_rule");

    var consolidatedPath = Path.Combine(outputRoot, "consolidated-results.json");
    VerifyReportWriter.WriteJson(consolidatedPath, new VerifyReport
    {
      Tool = "Evaluate-STIG",
      OutputRoot = outputRoot,
      Results = new List<ControlResult>
      {
        new() { RuleId = "SV-1000r1_rule", Tool = "Evaluate-STIG", SourceFile = "mapped.ckl" },
        new() { RuleId = "SV-9999r1_rule", Tool = "Evaluate-STIG", SourceFile = "unmapped.ckl" }
      }
    });

    var verifyService = new FakeVerificationWorkflowService(consolidatedPath);
    var service = new LocalWorkflowService(
      verifyService,
      new ScannerEvidenceMapper());

    var result = await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot
    }, CancellationToken.None);

    verifyService.LastRequest.Should().NotBeNull();
    verifyService.LastRequest!.OutputRoot.Should().Be(outputRoot);

    result.Mission.CanonicalChecklist.Should().ContainSingle(i => i.RuleId == "SV-1000r1_rule");
    result.Mission.ScannerEvidence.Should().ContainSingle(i => i.RuleId == "SV-1000r1_rule");
    result.Mission.Unmapped.Should().ContainSingle();
    result.Diagnostics.Should().Contain(d => d.Contains("Unmapped scanner finding", StringComparison.Ordinal));
    result.Diagnostics.Should().Contain("scan-diagnostic");

    var missionPath = Path.Combine(outputRoot, "mission.json");
    File.Exists(missionPath).Should().BeTrue();

    var mission = JsonSerializer.Deserialize<LocalWorkflowMission>(await File.ReadAllTextAsync(missionPath));
    mission.Should().NotBeNull();
    mission!.CanonicalChecklist.Should().ContainSingle(i => i.RuleId == "SV-1000r1_rule");
  }

  private static async Task WriteImportZipAsync(string importRoot, string ruleId)
  {
    var zipPath = Path.Combine(importRoot, "benchmark.zip");
    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
    var entry = archive.CreateEntry("benchmark-xccdf.xml");
    await using var writer = new StreamWriter(entry.Open());
    await writer.WriteAsync(
      "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      + "<Benchmark xmlns=\"http://checklists.nist.gov/xccdf/1.2\" id=\"xccdf_org.test.benchmark\">"
      + "<Rule id=\"" + ruleId + "\" severity=\"medium\"><title>Rule</title></Rule>"
      + "</Benchmark>");
  }

  private sealed class FakeVerificationWorkflowService : IVerificationWorkflowService
  {
    private readonly string _consolidatedJsonPath;

    public FakeVerificationWorkflowService(string consolidatedJsonPath)
    {
      _consolidatedJsonPath = consolidatedJsonPath;
    }

    public VerificationWorkflowRequest? LastRequest { get; private set; }

    public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
    {
      LastRequest = request;
      return Task.FromResult(new VerificationWorkflowResult
      {
        ConsolidatedJsonPath = _consolidatedJsonPath,
        Diagnostics = new[] { "scan-diagnostic" }
      });
    }
  }

}

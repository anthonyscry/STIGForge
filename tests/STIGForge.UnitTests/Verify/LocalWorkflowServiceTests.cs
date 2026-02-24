using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Workflow;
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

    var toolRoot = CreateValidToolRoot("tool-ok");

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
    var scanner = new ImportInboxScanner(new TestHashingService());
    var service = new LocalWorkflowService(
      scanner,
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    var result = await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = toolRoot
    }, CancellationToken.None);

    verifyService.LastRequest.Should().NotBeNull();
    verifyService.LastRequest!.OutputRoot.Should().Be(outputRoot);
    verifyService.LastRequest.EvaluateStig.Enabled.Should().BeTrue();
    verifyService.LastRequest.EvaluateStig.ToolRoot.Should().Be(toolRoot);

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

    var scanResult = await scanner.ScanWithCanonicalChecklistAsync(importRoot, CancellationToken.None);
    result.Mission.CanonicalChecklist.Select(c => c.RuleId)
      .Should()
      .Equal(scanResult.CanonicalChecklist.Select(c => c.RuleId));
  }

  [Fact]
  public async Task RunAsync_WithInvalidToolRoot_FailsSetupBeforeScan()
  {
    var importRoot = Path.Combine(_tempRoot, "import-fail");
    var outputRoot = Path.Combine(_tempRoot, "output-fail");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);
    await WriteImportZipAsync(importRoot, "SV-2000r1_rule");

    var verifyService = new FakeVerificationWorkflowService(Path.Combine(outputRoot, "consolidated-results.json"));
    var service = new LocalWorkflowService(
      new ImportInboxScanner(new TestHashingService()),
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    Func<Task> act = async () => await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = Path.Combine(_tempRoot, "missing-tools")
    }, CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>();
    verifyService.LastRequest.Should().BeNull();
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

  private string CreateValidToolRoot(string name)
  {
    var root = Path.Combine(_tempRoot, "tools-" + name);
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "Evaluate-STIG.ps1"), "# test");
    return root;
  }

  private sealed class TestHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct) => Task.FromResult("sha-" + Path.GetFileName(path));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct) => Task.FromResult("sha-text");
  }

  private sealed class TestPathBuilder : IPathBuilder
  {
    private readonly string _root;

    public TestPathBuilder(string root)
    {
      _root = root;
    }

    public string GetAppDataRoot() => _root;
    public string GetContentPacksRoot() => _root;
    public string GetPackRoot(string packId) => _root;
    public string GetBundleRoot(string bundleId) => _root;
    public string GetLogsRoot() => _root;
    public string GetImportRoot() => _root;
    public string GetImportInboxRoot() => _root;
    public string GetImportIndexPath() => Path.Combine(_root, "index.json");
    public string GetToolsRoot() => Path.Combine(_root, "tools");
    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts) => _root;
  }

}

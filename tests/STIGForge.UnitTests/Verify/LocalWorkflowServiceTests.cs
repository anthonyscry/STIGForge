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

    var scanStartedAt = DateTimeOffset.Parse("2026-02-23T12:00:00Z");
    var scanFinishedAt = DateTimeOffset.Parse("2026-02-23T12:05:00Z");
    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      StartedAt = scanStartedAt,
      FinishedAt = scanFinishedAt,
      ConsolidatedJsonPath = consolidatedPath,
      ConsolidatedCsvPath = Path.Combine(outputRoot, "consolidated-results.csv"),
      CoverageSummaryJsonPath = Path.Combine(outputRoot, "coverage-summary.json"),
      CoverageSummaryCsvPath = Path.Combine(outputRoot, "coverage-summary.csv"),
      ToolRuns =
      [
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 0,
          StartedAt = scanStartedAt,
          FinishedAt = scanFinishedAt
        }
      ],
      Diagnostics = new[] { "scan-diagnostic" }
    });
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
    mission.Diagnostics.Should().Contain(d => d.Contains("Unmapped scanner finding", StringComparison.Ordinal));
    mission.Diagnostics.Should().Contain("scan-diagnostic");
    mission.StageMetadata.MissionJsonPath.Should().Be(missionPath);
    mission.StageMetadata.ConsolidatedJsonPath.Should().Be(consolidatedPath);
    mission.StageMetadata.StartedAt.Should().Be(scanStartedAt);
    mission.StageMetadata.FinishedAt.Should().Be(scanFinishedAt);

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

    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      ConsolidatedJsonPath = Path.Combine(outputRoot, "consolidated-results.json")
    });
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

  [Fact]
  public async Task RunAsync_WithNonZeroEvaluateStigExitCode_ThrowsInvalidOperationException()
  {
    var importRoot = Path.Combine(_tempRoot, "import-nonzero");
    var outputRoot = Path.Combine(_tempRoot, "output-nonzero");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);
    await WriteImportZipAsync(importRoot, "SV-3000r1_rule");

    var consolidatedPath = Path.Combine(outputRoot, "consolidated-results.json");
    VerifyReportWriter.WriteJson(consolidatedPath, new VerifyReport
    {
      Tool = "Evaluate-STIG",
      OutputRoot = outputRoot,
      Results =
      [
        new ControlResult { RuleId = "SV-3000r1_rule", Tool = "Evaluate-STIG", SourceFile = "source.ckl" }
      ]
    });

    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      ConsolidatedJsonPath = consolidatedPath,
      ToolRuns =
      [
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 5
        }
      ]
    });

    var service = new LocalWorkflowService(
      new ImportInboxScanner(new TestHashingService()),
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    Func<Task> act = async () => await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = CreateValidToolRoot("nonzero")
    }, CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*Evaluate-STIG*exit code*5*");
  }

  [Fact]
  public async Task RunAsync_WithUnexecutedEvaluateStigRunFailure_ThrowsInvalidOperationException()
  {
    var importRoot = Path.Combine(_tempRoot, "import-exec-failure");
    var outputRoot = Path.Combine(_tempRoot, "output-exec-failure");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);
    await WriteImportZipAsync(importRoot, "SV-3500r1_rule");

    var consolidatedPath = Path.Combine(outputRoot, "consolidated-results.json");
    VerifyReportWriter.WriteJson(consolidatedPath, new VerifyReport
    {
      Tool = "Evaluate-STIG",
      OutputRoot = outputRoot,
      Results =
      [
        new ControlResult { RuleId = "SV-3500r1_rule", Tool = "Evaluate-STIG", SourceFile = "source.ckl" }
      ]
    });

    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      ConsolidatedJsonPath = consolidatedPath,
      ToolRuns =
      [
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = false,
          ExitCode = -1,
          Error = "PowerShell invocation failed"
        }
      ]
    });

    var service = new LocalWorkflowService(
      new ImportInboxScanner(new TestHashingService()),
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    Func<Task> act = async () => await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = CreateValidToolRoot("exec-failure")
    }, CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*Evaluate-STIG*did not execute*PowerShell invocation failed*");
  }

  [Fact]
  public async Task RunAsync_WithMissingConsolidatedJson_ThrowsInvalidOperationException()
  {
    var importRoot = Path.Combine(_tempRoot, "import-missing-report");
    var outputRoot = Path.Combine(_tempRoot, "output-missing-report");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);
    await WriteImportZipAsync(importRoot, "SV-4000r1_rule");

    var missingPath = Path.Combine(outputRoot, "consolidated-results.json");
    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      ConsolidatedJsonPath = missingPath,
      ToolRuns =
      [
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 0
        }
      ]
    });

    var service = new LocalWorkflowService(
      new ImportInboxScanner(new TestHashingService()),
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    Func<Task> act = async () => await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = CreateValidToolRoot("missing-report")
    }, CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*consolidated-results.json*");
  }

  [Fact]
  public async Task RunAsync_WithUnreadableConsolidatedJson_ThrowsInvalidOperationException()
  {
    var importRoot = Path.Combine(_tempRoot, "import-bad-report");
    var outputRoot = Path.Combine(_tempRoot, "output-bad-report");
    Directory.CreateDirectory(importRoot);
    Directory.CreateDirectory(outputRoot);
    await WriteImportZipAsync(importRoot, "SV-5000r1_rule");

    var consolidatedPath = Path.Combine(outputRoot, "consolidated-results.json");
    await File.WriteAllTextAsync(consolidatedPath, "{ bad json");

    var verifyService = new FakeVerificationWorkflowService(new VerificationWorkflowResult
    {
      ConsolidatedJsonPath = consolidatedPath,
      ToolRuns =
      [
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 0
        }
      ]
    });

    var service = new LocalWorkflowService(
      new ImportInboxScanner(new TestHashingService()),
      new LocalSetupValidator(new TestPathBuilder(_tempRoot)),
      verifyService,
      new ScannerEvidenceMapper());

    Func<Task> act = async () => await service.RunAsync(new LocalWorkflowRequest
    {
      ImportRoot = importRoot,
      OutputRoot = outputRoot,
      ToolRoot = CreateValidToolRoot("bad-report")
    }, CancellationToken.None);

    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*Failed to read consolidated scanner report*");
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
    private readonly VerificationWorkflowResult _result;

    public FakeVerificationWorkflowService(VerificationWorkflowResult result)
    {
      _result = result;
    }

    public VerificationWorkflowRequest? LastRequest { get; private set; }

    public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
    {
      LastRequest = request;
      return Task.FromResult(_result);
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

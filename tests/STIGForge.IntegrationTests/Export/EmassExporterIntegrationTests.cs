using System.Text;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Export;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Verify;

namespace STIGForge.IntegrationTests.Export;

public sealed class EmassExporterIntegrationTests : IDisposable
{
  private readonly string _tempRoot;

  public EmassExporterIntegrationTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-emass-integration-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public async Task ExportAsync_WritesValidationReportsAndReturnsDiagnostics()
  {
    var bundleRoot = CreateBundleFixture("bundle-valid");
    var outputRoot = Path.Combine(_tempRoot, "export-valid");

    var exporter = new EmassExporter(new TestPathBuilder(_tempRoot), new Sha256HashingService());
    var result = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = outputRoot
    }, CancellationToken.None);

    result.ValidationResult.Should().NotBeNull();
    result.ValidationResult!.IsValid.Should().BeTrue();
    result.IsReadyForSubmission.Should().BeTrue();

    result.ValidationReportPath.Should().Be(Path.Combine(outputRoot, "00_Manifest", "validation_report.txt"));
    result.ValidationReportJsonPath.Should().Be(Path.Combine(outputRoot, "00_Manifest", "validation_report.json"));
    File.Exists(result.ValidationReportPath).Should().BeTrue();
    File.Exists(result.ValidationReportJsonPath).Should().BeTrue();

    using var reportJson = JsonDocument.Parse(File.ReadAllText(result.ValidationReportJsonPath));
    reportJson.RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();
    reportJson.RootElement.GetProperty("metrics").GetProperty("indexedControlCount").GetInt32().Should().BeGreaterThan(0);
    reportJson.RootElement.GetProperty("metrics").GetProperty("poamItemCount").GetInt32().Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task ValidatePackage_AfterTamper_ReturnsInvalid()
  {
    var bundleRoot = CreateBundleFixture("bundle-tampered");
    var outputRoot = Path.Combine(_tempRoot, "export-tampered");

    var exporter = new EmassExporter(new TestPathBuilder(_tempRoot), new Sha256HashingService());
    var result = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = outputRoot
    }, CancellationToken.None);

    File.AppendAllText(Path.Combine(result.OutputRoot, "03_POAM", "poam.csv"), Environment.NewLine + "tampered", Encoding.UTF8);

    var validator = new EmassPackageValidator();
    var tamperedValidation = validator.ValidatePackage(result.OutputRoot);

    tamperedValidation.IsValid.Should().BeFalse();
    tamperedValidation.Errors.Should().Contain(e => e.Contains("Hash mismatch", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ExportAsync_WhenValidationFails_ReturnsBlockedSubmissionReadiness()
  {
    var bundleRoot = CreateBundleFixture("bundle-invalid-readiness");
    var outputRoot = Path.Combine(_tempRoot, "export-invalid-readiness");

    var exporter = new EmassExporter(new TestPathBuilder(_tempRoot), new AlwaysInvalidHashingService());
    var result = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = outputRoot
    }, CancellationToken.None);

    result.ValidationResult.Should().NotBeNull();
    result.ValidationResult!.IsValid.Should().BeFalse();
    result.IsReadyForSubmission.Should().BeFalse();
    result.BlockingFailures.Should().NotBeEmpty();
  }

  private string CreateBundleFixture(string name)
  {
    var bundleRoot = Path.Combine(_tempRoot, name);
    Directory.CreateDirectory(bundleRoot);

    WriteBundleManifest(bundleRoot);
    WriteVerifyReport(bundleRoot);
    WriteEvidence(bundleRoot);

    return bundleRoot;
  }

  private static void WriteBundleManifest(string bundleRoot)
  {
    var manifestDir = Path.Combine(bundleRoot, "Manifest");
    Directory.CreateDirectory(manifestDir);

    var payload = new
    {
      bundleId = "bundle-int-1",
      run = new RunManifest
      {
        RunId = "run-1",
        SystemName = "WS-INT",
        OsTarget = OsTarget.Win11,
        RoleTemplate = RoleTemplate.MemberServer,
        ProfileId = "prof-1",
        ProfileName = "Profile 1",
        PackId = "pack-1",
        PackName = "Pack 1",
        Timestamp = DateTimeOffset.Parse("2026-02-08T00:00:00Z"),
        ToolVersion = "1.0"
      }
    };

    File.WriteAllText(
      Path.Combine(manifestDir, "manifest.json"),
      JsonSerializer.Serialize(payload, new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
      }),
      Encoding.UTF8);
  }

  private static void WriteVerifyReport(string bundleRoot)
  {
    var verifyDir = Path.Combine(bundleRoot, "Verify", "run1");
    Directory.CreateDirectory(verifyDir);

    var report = new VerifyReport
    {
      Tool = "SCAP",
      ToolVersion = "1.0",
      StartedAt = DateTimeOffset.Parse("2026-02-08T00:00:00Z"),
      FinishedAt = DateTimeOffset.Parse("2026-02-08T00:10:00Z"),
      OutputRoot = verifyDir,
      Results = new List<ControlResult>
      {
        new()
        {
          VulnId = "V-9001",
          RuleId = "SV-9001",
          Title = "Control Fail",
          Severity = "high",
          Status = "Open",
          Tool = "SCAP",
          SourceFile = "scan.xml"
        },
        new()
        {
          VulnId = "V-9002",
          RuleId = "SV-9002",
          Title = "Control Pass",
          Severity = "medium",
          Status = "NotAFinding",
          Tool = "SCAP",
          SourceFile = "scan.xml"
        }
      }
    };

    File.WriteAllText(
      Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
      }),
      Encoding.UTF8);

    File.WriteAllText(Path.Combine(verifyDir, "sample.ckl"), "<CHECKLIST />", Encoding.UTF8);
  }

  private static void WriteEvidence(string bundleRoot)
  {
    var evidenceDir = Path.Combine(bundleRoot, "Evidence", "by_control", "SV-9001");
    Directory.CreateDirectory(evidenceDir);
    File.WriteAllText(Path.Combine(evidenceDir, "proof.txt"), "proof", Encoding.UTF8);
  }

  private sealed class TestPathBuilder : IPathBuilder
  {
    private readonly string _root;

    public TestPathBuilder(string root)
    {
      _root = Path.Combine(root, ".stigforge-integration");
      Directory.CreateDirectory(_root);
    }

    public string GetAppDataRoot() => _root;

    public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");

    public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);

    public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);

    public string GetLogsRoot() => Path.Combine(_root, "logs");

    public string GetImportRoot() => Path.Combine(_root, "import");

    public string GetImportInboxRoot() => Path.Combine(GetImportRoot(), "inbox");

    public string GetImportIndexPath() => Path.Combine(GetImportRoot(), "inbox_index.json");

    public string GetToolsRoot() => Path.Combine(_root, "tools");

    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
      => Path.Combine(_root, "exports", "default");
  }

  private sealed class AlwaysInvalidHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult(new string('0', 64));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult(new string('0', 64));
  }
}

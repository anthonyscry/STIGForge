using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Export;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class EmassExporterConsistencyTests : IDisposable
{
  private readonly string _tempDir;

  public EmassExporterConsistencyTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-emass-consistency-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public async Task ExportAsync_RepeatedRuns_ProduceDeterministicIndexAndTrace()
  {
    var bundleRoot = CreateBundleFixture();
    var exporter = new EmassExporter(new FixedPathBuilder(_tempDir), new Sha256HashingService());

    var outputA = Path.Combine(_tempDir, "export-a");
    var outputB = Path.Combine(_tempDir, "export-b");

    var resultA = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = outputA
    }, CancellationToken.None);

    var resultB = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = outputB
    }, CancellationToken.None);

    File.ReadAllText(resultA.IndexPath).Should().Be(File.ReadAllText(resultB.IndexPath));

    using var manifestA = JsonDocument.Parse(File.ReadAllText(resultA.ManifestPath));
    using var manifestB = JsonDocument.Parse(File.ReadAllText(resultB.ManifestPath));

    manifestA.RootElement.GetProperty("exportTrace").GetRawText()
      .Should().Be(manifestB.RootElement.GetProperty("exportTrace").GetRawText());

    var trace = manifestA.RootElement.GetProperty("exportTrace");
    var sourceReports = trace.GetProperty("SourceReports")
      .EnumerateArray()
      .Select(e => (e.GetString() ?? string.Empty).Replace('\\', '/'))
      .ToList();

    sourceReports.Should().ContainInOrder(
      "Verify/a-run/consolidated-results.json",
      "Verify/z-run/consolidated-results.json");

    trace.GetProperty("ToolCounts").GetProperty("Evaluate-STIG").GetInt32().Should().Be(2);
    trace.GetProperty("ToolCounts").GetProperty("SCAP").GetInt32().Should().Be(2);
    trace.GetProperty("StatusTotals").GetProperty("Fail").GetInt32().Should().Be(1);
    trace.GetProperty("StatusTotals").GetProperty("Pass").GetInt32().Should().Be(2);
    trace.GetProperty("StatusTotals").GetProperty("NotApplicable").GetInt32().Should().Be(1);
    trace.GetProperty("TotalConsolidatedResults").GetInt32().Should().Be(4);
  }

  [Fact]
  public async Task ExportAsync_IndexRowsStayInStableControlOrder()
  {
    var bundleRoot = CreateBundleFixture();
    var exporter = new EmassExporter(new FixedPathBuilder(_tempDir), new Sha256HashingService());
    var output = Path.Combine(_tempDir, "export-index-order");

    var result = await exporter.ExportAsync(new ExportRequest
    {
      BundleRoot = bundleRoot,
      OutputRoot = output
    }, CancellationToken.None);

    var lines = File.ReadAllLines(result.IndexPath);
    lines.Should().HaveCount(4);
    lines[1].Should().Contain("SV-C1");
    lines[2].Should().Contain("SV-C2");
    lines[3].Should().Contain("SV-C3");
  }

  private string CreateBundleFixture()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    Directory.CreateDirectory(bundleRoot);

    WriteManifest(bundleRoot);
    WriteReport(Path.Combine(bundleRoot, "Verify", "z-run", "consolidated-results.json"), "SCAP", new List<ControlResult>
    {
      new() { VulnId = "V-C1", RuleId = "SV-C1", Title = "Control One", Severity = "high", Status = "Open", Tool = "SCAP", SourceFile = "scan-z.xml", VerifiedAt = DateTimeOffset.Parse("2026-02-08T09:00:00Z") },
      new() { VulnId = "V-C2", RuleId = "SV-C2", Title = "Control Two", Severity = "medium", Status = "NotAFinding", Tool = "SCAP", SourceFile = "scan-z.xml", VerifiedAt = DateTimeOffset.Parse("2026-02-08T09:00:00Z") }
    });

    WriteReport(Path.Combine(bundleRoot, "Verify", "a-run", "consolidated-results.json"), "Evaluate-STIG", new List<ControlResult>
    {
      new() { VulnId = "V-C1", RuleId = "SV-C1", Title = "Control One", Severity = "high", Status = "NotAFinding", Tool = "Evaluate-STIG", SourceFile = "scan-a.xml", VerifiedAt = DateTimeOffset.Parse("2026-02-08T08:00:00Z") },
      new() { VulnId = "V-C3", RuleId = "SV-C3", Title = "Control Three", Severity = "low", Status = "Not_Applicable", Tool = "Evaluate-STIG", SourceFile = "scan-a.xml", VerifiedAt = DateTimeOffset.Parse("2026-02-08T08:00:00Z") }
    });

    var evidenceDir = Path.Combine(bundleRoot, "Evidence", "by_control", "SV-C1");
    Directory.CreateDirectory(evidenceDir);
    File.WriteAllText(Path.Combine(evidenceDir, "z.txt"), "z");
    File.WriteAllText(Path.Combine(evidenceDir, "a.txt"), "a");

    return bundleRoot;
  }

  private static void WriteManifest(string bundleRoot)
  {
    var manifestDir = Path.Combine(bundleRoot, "Manifest");
    Directory.CreateDirectory(manifestDir);

    var run = new RunManifest
    {
      RunId = "run-1",
      SystemName = "WS-TEST",
      OsTarget = OsTarget.Win11,
      RoleTemplate = RoleTemplate.MemberServer,
      ProfileName = "Profile-1",
      PackName = "Pack-1",
      Timestamp = DateTimeOffset.Parse("2026-02-08T08:00:00Z")
    };

    var payload = new
    {
      bundleId = "bundle-1",
      run
    };

    File.WriteAllText(
      Path.Combine(manifestDir, "manifest.json"),
      JsonSerializer.Serialize(payload, new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
      }));
  }

  private static void WriteReport(string path, string tool, IReadOnlyList<ControlResult> results)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var report = new VerifyReport
    {
      Tool = tool,
      ToolVersion = "1.0",
      StartedAt = DateTimeOffset.Parse("2026-02-08T08:00:00Z"),
      FinishedAt = DateTimeOffset.Parse("2026-02-08T09:00:00Z"),
      OutputRoot = Path.GetDirectoryName(path)!,
      Results = results
    };

    File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true
    }));
  }

  private sealed class FixedPathBuilder : IPathBuilder
  {
    private readonly string _root;

    public FixedPathBuilder(string root)
    {
      _root = Path.Combine(root, ".stigforge-test");
      Directory.CreateDirectory(_root);
    }

    public string GetAppDataRoot() => _root;

    public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");

    public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);

    public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);

    public string GetLogsRoot() => Path.Combine(_root, "logs");

    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
      => Path.Combine(_root, "exports", "default");
  }
}

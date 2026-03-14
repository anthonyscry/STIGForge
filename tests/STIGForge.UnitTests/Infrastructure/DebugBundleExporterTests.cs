using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Telemetry;

namespace STIGForge.UnitTests.Infrastructure;

/// <summary>
/// Windows-specific tests for DebugBundleExporter.
/// Covers ZIP creation, artifact inclusion, manifest/system-info contents,
/// and graceful handling of missing directories.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DebugBundleExporterTests : IDisposable
{
  private readonly string _tempDir;
  private readonly Mock<IPathBuilder> _mockPaths;
  private readonly string _logsRoot;

  public DebugBundleExporterTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-dbg-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    _logsRoot = Path.Combine(_tempDir, "logs");
    Directory.CreateDirectory(_logsRoot);

    _mockPaths = new Mock<IPathBuilder>();
    _mockPaths.Setup(p => p.GetLogsRoot()).Returns(_logsRoot);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
  }

  // ── Constructor ─────────────────────────────────────────────────────────────

  [Fact]
  public void Constructor_ThrowsWhenPathsIsNull()
  {
    var act = () => new DebugBundleExporter(null!);
    act.Should().Throw<ArgumentNullException>().WithParameterName("paths");
  }

  // ── ExportBundle: null guard ─────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_ThrowsWhenRequestIsNull()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var act = () => exporter.ExportBundle(null!);
    act.Should().Throw<ArgumentNullException>();
  }

  // ── ExportBundle: output file ────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_CreatesZipFile()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var request = new DebugBundleRequest { IncludeDaysOfLogs = 7 };

    var result = exporter.ExportBundle(request);

    File.Exists(result.OutputPath).Should().BeTrue();
    result.OutputPath.Should().EndWith(".zip");
  }

  [Fact]
  public void ExportBundle_OutputPathContainsTimestampToken()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    Path.GetFileName(result.OutputPath).Should().StartWith("stigforge-debug-");
  }

  [Fact]
  public void ExportBundle_CreatesExportsSubdirectory()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    exporter.ExportBundle(new DebugBundleRequest());

    var exportsDir = Path.Combine(_logsRoot, "exports");
    Directory.Exists(exportsDir).Should().BeTrue();
  }

  [Fact]
  public void ExportBundle_SetsCreatedAtTimestamp()
  {
    var before = DateTimeOffset.UtcNow.AddSeconds(-2);
    var exporter = new DebugBundleExporter(_mockPaths.Object);

    var result = exporter.ExportBundle(new DebugBundleRequest());

    result.CreatedAt.Should().BeAfter(before);
    result.CreatedAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(2));
  }

  // ── ExportBundle: archive always contains system-info and manifest ───────────

  [Fact]
  public void ExportBundle_AlwaysContainsSystemInfoEntry()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    using var zip = ZipFile.OpenRead(result.OutputPath);
    zip.GetEntry("system-info.json").Should().NotBeNull("system-info.json is always included");
  }

  [Fact]
  public void ExportBundle_AlwaysContainsManifestEntry()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    using var zip = ZipFile.OpenRead(result.OutputPath);
    zip.GetEntry("manifest.json").Should().NotBeNull("manifest.json is always included");
  }

  [Fact]
  public void ExportBundle_FileCountIsAtLeastTwo()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    // system-info.json + manifest.json = 2 minimum
    result.FileCount.Should().BeGreaterThanOrEqualTo(2);
  }

  // ── system-info.json content ─────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_SystemInfo_ContainsMachineName()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    var json = ReadZipEntry(result.OutputPath, "system-info.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("MachineName").GetString()
       .Should().Be(Environment.MachineName);
  }

  [Fact]
  public void ExportBundle_SystemInfo_ContainsOSDescription()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    var json = ReadZipEntry(result.OutputPath, "system-info.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("OSDescription").GetString()
       .Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void ExportBundle_SystemInfo_ContainsProcessId()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    var json = ReadZipEntry(result.OutputPath, "system-info.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("ProcessId").GetInt32()
       .Should().Be(Environment.ProcessId);
  }

  // ── manifest.json content ────────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_Manifest_ContainsBundleRoot()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    Directory.CreateDirectory(bundleRoot);
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { BundleRoot = bundleRoot });

    var json = ReadZipEntry(result.OutputPath, "manifest.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("BundleRoot").GetString().Should().Be(bundleRoot);
  }

  [Fact]
  public void ExportBundle_Manifest_ContainsExportReason()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { ExportReason = "UnitTest" });

    var json = ReadZipEntry(result.OutputPath, "manifest.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("Reason").GetString().Should().Be("UnitTest");
  }

  [Fact]
  public void ExportBundle_Manifest_ContainsIncludeDaysOfLogs()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { IncludeDaysOfLogs = 14 });

    var json = ReadZipEntry(result.OutputPath, "manifest.json");
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("IncludeDaysOfLogs").GetInt32().Should().Be(14);
  }

  // ── Log inclusion ────────────────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_IncludesRecentLogFiles()
  {
    var logFile = Path.Combine(_logsRoot, "app.log");
    File.WriteAllText(logFile, "log content");

    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { IncludeDaysOfLogs = 7 });

    using var zip = ZipFile.OpenRead(result.OutputPath);
    var logEntries = zip.Entries.Where(e => e.FullName.StartsWith("logs/")).ToList();
    logEntries.Should().NotBeEmpty("recent log file should be included");
  }

  [Fact]
  public void ExportBundle_ExcludesOldLogFiles()
  {
    var logFile = Path.Combine(_logsRoot, "old.log");
    File.WriteAllText(logFile, "old content");
    File.SetLastWriteTimeUtc(logFile, DateTime.UtcNow.AddDays(-30));

    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { IncludeDaysOfLogs = 7 });

    using var zip = ZipFile.OpenRead(result.OutputPath);
    var oldEntry = zip.Entries.FirstOrDefault(e => e.Name == "old.log");
    oldEntry.Should().BeNull("log files older than the cutoff should not be included");
  }

  // ── Traces inclusion ─────────────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_IncludesTracesJsonWhenPresent()
  {
    var tracesPath = Path.Combine(_logsRoot, "traces.json");
    File.WriteAllText(tracesPath, "{\"spanId\":\"abc\"}");

    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest());

    using var zip = ZipFile.OpenRead(result.OutputPath);
    zip.GetEntry("traces/traces.json").Should().NotBeNull();
  }

  [Fact]
  public void ExportBundle_DoesNotFailWhenTracesJsonMissing()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var act = () => exporter.ExportBundle(new DebugBundleRequest());
    act.Should().NotThrow();
  }

  // ── Bundle root artifacts ────────────────────────────────────────────────────

  [Fact]
  public void ExportBundle_IncludesBundleVerifyJsonFiles()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    var verifyDir = Path.Combine(bundleRoot, "Verify");
    Directory.CreateDirectory(verifyDir);
    File.WriteAllText(Path.Combine(verifyDir, "result.json"), "{\"status\":\"pass\"}");

    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { BundleRoot = bundleRoot });

    using var zip = ZipFile.OpenRead(result.OutputPath);
    var verifyEntries = zip.Entries.Where(e => e.FullName.StartsWith("bundle/Verify/")).ToList();
    verifyEntries.Should().NotBeEmpty();
  }

  [Fact]
  public void ExportBundle_IncludesApplyRunJson()
  {
    var bundleRoot = Path.Combine(_tempDir, "bundle");
    var applyDir = Path.Combine(bundleRoot, "Apply");
    Directory.CreateDirectory(applyDir);
    File.WriteAllText(Path.Combine(applyDir, "apply_run.json"), "{\"runId\":\"test\"}");

    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var result = exporter.ExportBundle(new DebugBundleRequest { BundleRoot = bundleRoot });

    using var zip = ZipFile.OpenRead(result.OutputPath);
    zip.GetEntry("bundle/Apply/apply_run.json").Should().NotBeNull();
  }

  [Fact]
  public void ExportBundle_HandlesNullBundleRoot()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var act = () => exporter.ExportBundle(new DebugBundleRequest { BundleRoot = null });
    act.Should().NotThrow();
  }

  [Fact]
  public void ExportBundle_HandlesNonexistentBundleRoot()
  {
    var exporter = new DebugBundleExporter(_mockPaths.Object);
    var act = () => exporter.ExportBundle(new DebugBundleRequest
    {
      BundleRoot = Path.Combine(_tempDir, "nonexistent-bundle")
    });
    act.Should().NotThrow();
  }

  // ── DebugBundleRequest defaults ──────────────────────────────────────────────

  [Fact]
  public void DebugBundleRequest_DefaultIncludeDaysOfLogs_IsSeven()
  {
    var request = new DebugBundleRequest();
    request.IncludeDaysOfLogs.Should().Be(7);
  }

  [Fact]
  public void DebugBundleRequest_DefaultBundleRoot_IsNull()
  {
    var request = new DebugBundleRequest();
    request.BundleRoot.Should().BeNull();
  }

  // ── Helper ──────────────────────────────────────────────────────────────────

  private static string ReadZipEntry(string zipPath, string entryName)
  {
    using var zip = ZipFile.OpenRead(zipPath);
    var entry = zip.GetEntry(entryName)
                ?? throw new InvalidOperationException($"Entry '{entryName}' not found in ZIP.");
    using var stream = entry.Open();
    using var reader = new System.IO.StreamReader(stream);
    return reader.ReadToEnd();
  }
}

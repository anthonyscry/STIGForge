using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

public sealed class DebugBundleExporterTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mock<IPathBuilder> BuildPathsMock(string logsRoot)
    {
        var mock = new Mock<IPathBuilder>();
        mock.Setup(p => p.GetLogsRoot()).Returns(logsRoot);
        return mock;
    }

    private static DebugBundleExporter BuildExporter(string logsRoot) =>
        new(BuildPathsMock(logsRoot).Object);

    private static IEnumerable<string> ReadZipEntryNames(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    private static string ReadZipEntry(string archivePath, string entryName)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Entry '{entryName}' not found in archive.");
        using var stream = entry.Open();
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ── constructor guard ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenPathBuilderIsNull()
    {
        Action act = () => new DebugBundleExporter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("paths");
    }

    // ── basic export ──────────────────────────────────────────────────────────

    [Fact]
    public void ExportBundle_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        Action act = () => exporter.ExportBundle(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExportBundle_CreatesZipFile_InExportsSubdirectory()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);
        var request = new DebugBundleRequest { IncludeDaysOfLogs = 7 };

        var result = exporter.ExportBundle(request);

        File.Exists(result.OutputPath).Should().BeTrue();
        result.OutputPath.Should().EndWith(".zip");
    }

    [Fact]
    public void ExportBundle_ReturnsPositiveFileCount()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        var result = exporter.ExportBundle(new DebugBundleRequest());

        result.FileCount.Should().BeGreaterThan(0, "manifest.json and system-info.json are always added");
    }

    [Fact]
    public void ExportBundle_ContainsManifestJson()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        var result = exporter.ExportBundle(new DebugBundleRequest());

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain("manifest.json");
    }

    [Fact]
    public void ExportBundle_ContainsSystemInfoJson()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        var result = exporter.ExportBundle(new DebugBundleRequest());

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain("system-info.json");
    }

    [Fact]
    public void ExportBundle_ManifestJson_ContainsExportReason()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);
        var request = new DebugBundleRequest { ExportReason = "Investigating anomaly" };

        var result = exporter.ExportBundle(request);

        var manifest = ReadZipEntry(result.OutputPath, "manifest.json");
        manifest.Should().Contain("Investigating anomaly");
    }

    [Fact]
    public void ExportBundle_SystemInfoJson_ContainsMachineName()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        var result = exporter.ExportBundle(new DebugBundleRequest());

        var sysInfo = ReadZipEntry(result.OutputPath, "system-info.json");
        sysInfo.Should().Contain(Environment.MachineName);
    }

    // ── log inclusion ─────────────────────────────────────────────────────────

    [Fact]
    public void ExportBundle_IncludesRecentLogFiles()
    {
        using var tmp = new TempDirectory();
        var logFile = Path.Combine(tmp.Path, "app.log");
        File.WriteAllText(logFile, "log content");
        File.SetLastWriteTimeUtc(logFile, DateTime.UtcNow.AddMinutes(-5));

        var exporter = BuildExporter(tmp.Path);
        var result = exporter.ExportBundle(new DebugBundleRequest { IncludeDaysOfLogs = 7 });

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain(e => e.Contains("app.log"));
    }

    [Fact]
    public void ExportBundle_ExcludesOldLogFiles()
    {
        using var tmp = new TempDirectory();
        var logFile = Path.Combine(tmp.Path, "old.log");
        File.WriteAllText(logFile, "old log");
        File.SetLastWriteTimeUtc(logFile, DateTime.UtcNow.AddDays(-30));

        var exporter = BuildExporter(tmp.Path);
        var result = exporter.ExportBundle(new DebugBundleRequest { IncludeDaysOfLogs = 1 });

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().NotContain(e => e.Contains("old.log"));
    }

    // ── bundle root inclusion ─────────────────────────────────────────────────

    [Fact]
    public void ExportBundle_IncludesBundleApplyLogs_WhenPresent()
    {
        using var logsDir = new TempDirectory();
        using var bundleDir = new TempDirectory();

        var applyLogsDir = Path.Combine(bundleDir.Path, "Apply", "Logs");
        Directory.CreateDirectory(applyLogsDir);
        File.WriteAllText(Path.Combine(applyLogsDir, "run.txt"), "apply log");

        var exporter = BuildExporter(logsDir.Path);
        var result = exporter.ExportBundle(new DebugBundleRequest { BundleRoot = bundleDir.Path });

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain(e => e.Contains("run.txt") && e.Contains("bundle/Apply/Logs"));
    }

    [Fact]
    public void ExportBundle_IncludesBundleApplyRunJson_WhenPresent()
    {
        using var logsDir = new TempDirectory();
        using var bundleDir = new TempDirectory();

        var applyDir = Path.Combine(bundleDir.Path, "Apply");
        Directory.CreateDirectory(applyDir);
        File.WriteAllText(Path.Combine(applyDir, "apply_run.json"), "{\"status\":\"done\"}");

        var exporter = BuildExporter(logsDir.Path);
        var result = exporter.ExportBundle(new DebugBundleRequest { BundleRoot = bundleDir.Path });

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain("bundle/Apply/apply_run.json");
    }

    [Fact]
    public void ExportBundle_IncludesTracesJson_WhenPresent()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "traces.json"), "[{\"trace\":\"t1\"}]");

        var exporter = BuildExporter(tmp.Path);
        var result = exporter.ExportBundle(new DebugBundleRequest());

        var entries = ReadZipEntryNames(result.OutputPath);
        entries.Should().Contain("traces/traces.json");
    }

    [Fact]
    public void ExportBundle_DoesNotThrow_WhenBundleRootDoesNotExist()
    {
        using var tmp = new TempDirectory();
        var exporter = BuildExporter(tmp.Path);

        Action act = () => exporter.ExportBundle(new DebugBundleRequest
        {
            BundleRoot = Path.Combine(tmp.Path, "nonexistent-bundle")
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ExportBundle_SetsCreatedAt_ToApproximatelyUtcNow()
    {
        using var tmp = new TempDirectory();
        var before = DateTimeOffset.UtcNow;
        var exporter = BuildExporter(tmp.Path);

        var result = exporter.ExportBundle(new DebugBundleRequest());
        var after = DateTimeOffset.UtcNow;

        result.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}

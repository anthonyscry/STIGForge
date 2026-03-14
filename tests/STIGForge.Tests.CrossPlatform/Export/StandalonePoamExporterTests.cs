using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;
using VerifyControlResult = STIGForge.Verify.ControlResult;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class StandalonePoamExporterTests : IDisposable
{
    private readonly string _tempDir;

    public StandalonePoamExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "poam-exporter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Bundle helpers ────────────────────────────────────────────────────────

    private string NewBundleRoot()
    {
        var root = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteManifest(string bundleRoot, string systemName, string bundleId)
    {
        var manifestDir = Path.Combine(bundleRoot, "Manifest");
        Directory.CreateDirectory(manifestDir);
        var manifest = new
        {
            bundleId,
            run = new { systemName }
        };
        File.WriteAllText(
            Path.Combine(manifestDir, "manifest.json"),
            JsonSerializer.Serialize(manifest));
    }

    private static void WriteConsolidatedResults(string bundleRoot, IEnumerable<VerifyControlResult> results)
    {
        var verifyDir = Path.Combine(bundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);
        var report = new
        {
            Tool = "unit-test",
            ToolVersion = "1.0",
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            OutputRoot = verifyDir,
            Results = results
        };
        File.WriteAllText(
            Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(report));
    }

    private static VerifyControlResult FailResult(string id = "V-001") =>
        new() { VulnId = id, RuleId = "SV-001_rule", Title = "Test Finding", Severity = "medium", Status = "fail" };

    private static VerifyControlResult PassResult(string id = "V-002") =>
        new() { VulnId = id, Status = "pass", Severity = "low" };

    // ── ReadManifest tests ────────────────────────────────────────────────────

    [Fact]
    public void ReadManifest_MissingManifestFile_ReturnsNullTuple()
    {
        var root = NewBundleRoot();

        var (systemName, bundleId) = StandalonePoamExporter.ReadManifest(root);

        systemName.Should().BeNull();
        bundleId.Should().BeNull();
    }

    [Fact]
    public void ReadManifest_ValidManifest_ReturnsSystemNameAndBundleId()
    {
        var root = NewBundleRoot();
        WriteManifest(root, "My System", "bundle-abc");

        var (systemName, bundleId) = StandalonePoamExporter.ReadManifest(root);

        systemName.Should().Be("My System");
        bundleId.Should().Be("bundle-abc");
    }

    [Fact]
    public void ReadManifest_MalformedJson_ReturnsNull()
    {
        var root = NewBundleRoot();
        var manifestDir = Path.Combine(root, "Manifest");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{ not valid json }}}");

        var (systemName, bundleId) = StandalonePoamExporter.ReadManifest(root);

        systemName.Should().BeNull();
        bundleId.Should().BeNull();
    }

    // ── LoadAndNormalize tests ────────────────────────────────────────────────

    [Fact]
    public void LoadAndNormalize_EmptyVerifyDir_ReturnsEmptyList()
    {
        var root = NewBundleRoot();
        // No Verify directory at all
        var results = StandalonePoamExporter.LoadAndNormalize(root);

        results.Should().BeEmpty();
    }

    [Fact]
    public void LoadAndNormalize_ResultsJsonPresent_ReturnsNormalizedResults()
    {
        var root = NewBundleRoot();
        WriteConsolidatedResults(root, new[] { FailResult("V-001"), PassResult("V-002") });

        var results = StandalonePoamExporter.LoadAndNormalize(root);

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.VulnId == "V-001" && r.Status == VerifyStatus.Fail);
        results.Should().Contain(r => r.VulnId == "V-002" && r.Status == VerifyStatus.Pass);
    }

    // ── ExportPoam tests ──────────────────────────────────────────────────────

    [Fact]
    public void ExportPoam_MinimalBundle_ProducesOutput()
    {
        var root = NewBundleRoot();
        WriteManifest(root, "Test System", "bundle-test");
        WriteConsolidatedResults(root, new[] { FailResult("V-100") });

        var request = new PoamExportRequest
        {
            BundleRoot = root,
            OutputDirectory = Path.Combine(root, "output", "poam"),
            SystemName = "Test System"
        };

        var result = StandalonePoamExporter.ExportPoam(request);

        result.ItemCount.Should().BeGreaterThan(0);
        result.Message.Should().ContainEquivalentOf("success");
        File.Exists(Path.Combine(request.OutputDirectory!, "poam.json")).Should().BeTrue();
    }
}

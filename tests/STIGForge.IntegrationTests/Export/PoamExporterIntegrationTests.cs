using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.IntegrationTests.Export;

public class PoamExporterIntegrationTests : IDisposable
{
    private readonly string _testRoot;

    public PoamExporterIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "stigforge_test_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    private void CreateBundleWithResults(string bundleRoot, List<ControlResult> results)
    {
        var verifyDir = Path.Combine(bundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);

        var report = new VerifyReport
        {
            Tool = "TestTool",
            ToolVersion = "1.0",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FinishedAt = DateTimeOffset.UtcNow,
            OutputRoot = verifyDir,
            Results = results
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"), json);
    }

    [Fact]
    public void ExportPoam_WithResults_CreatesPoamFiles()
    {
        var bundleRoot = Path.Combine(_testRoot, "bundle1");
        Directory.CreateDirectory(bundleRoot);

        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-200001",
                RuleId = "SV-200001r1_rule",
                Title = "Open Finding 1",
                Severity = "high",
                Status = "Open",
                FindingDetails = "Issue found",
                Tool = "TestTool",
                SourceFile = "test.xml"
            },
            new()
            {
                VulnId = "V-200002",
                RuleId = "SV-200002r1_rule",
                Title = "Open Finding 2",
                Severity = "medium",
                Status = "Open",
                FindingDetails = "Another issue",
                Tool = "TestTool",
                SourceFile = "test.xml"
            }
        };

        CreateBundleWithResults(bundleRoot, results);

        var outputDir = Path.Combine(_testRoot, "poam-output");

        var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest
        {
            BundleRoot = bundleRoot,
            OutputDirectory = outputDir,
            SystemName = "TestSystem"
        });

        result.ItemCount.Should().BeGreaterThan(0);
        result.OutputDirectory.Should().Be(outputDir);

        File.Exists(Path.Combine(outputDir, "poam.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "poam.csv")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "poam_summary.txt")).Should().BeTrue();
    }

    [Fact]
    public void ExportPoam_EmptyBundle_ReturnsZeroCount()
    {
        // Bundle root exists but has no Verify directory
        var bundleRoot = Path.Combine(_testRoot, "bundle-empty");
        Directory.CreateDirectory(bundleRoot);

        var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest
        {
            BundleRoot = bundleRoot
        });

        result.ItemCount.Should().Be(0);
    }

    [Fact]
    public void ExportPoam_MissingBundleRoot_Throws()
    {
        var act = () => StandalonePoamExporter.ExportPoam(new PoamExportRequest
        {
            BundleRoot = string.Empty
        });

        act.Should().Throw<ArgumentException>();
    }
}

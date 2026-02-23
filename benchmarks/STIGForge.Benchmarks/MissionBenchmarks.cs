using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using STIGForge.Benchmarks.TestData;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.Benchmarks;

/// <summary>
/// Benchmarks for measuring mission phase durations at different scales.
/// Measures Build, Apply (placeholder), Verify (mocked), and Prove phases.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class MissionBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private string _tempDir = null!;
    private BundleBuilder _bundleBuilder = null!;
    private EmassExporter _emassExporter = null!;
    private IVerificationWorkflowService _verificationWorkflow = null!;

    // Test different scales: 100, 1K, 10K rules
    [Params(100, 1000, 10000)]
    public int RuleCount { get; set; }

    private IReadOnlyList<ControlRecord> _controls = null!;
    private Profile _profile = null!;
    private ContentPack _pack = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create temp directory
        _tempDir = Path.Combine(Path.GetTempPath(), $"stigforge-bench-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);

        // Build test service provider with mock services
        var services = new ServiceCollection();

        // Register mock/test implementations
        services.AddSingleton<IPathBuilder>(new TestPathBuilder(_tempDir));
        services.AddSingleton<IHashingService, TestHashingService>();
        services.AddSingleton<IClassificationScopeService, TestClassificationScopeService>();
        services.AddSingleton<IClock, TestClock>();
        services.AddSingleton<ReleaseAgeGate>();
        services.AddSingleton<OverlayConflictDetector>();
        services.AddSingleton<OverlayMergeService>();
        services.AddSingleton<BundleBuilder>();
        services.AddSingleton<EmassExporter>();

        // Mock verification workflow that returns immediately
        services.AddSingleton<IVerificationWorkflowService, MockVerificationWorkflowService>();

        _serviceProvider = services.BuildServiceProvider();

        // Get services
        _bundleBuilder = _serviceProvider.GetRequiredService<BundleBuilder>();
        _emassExporter = _serviceProvider.GetRequiredService<EmassExporter>();
        _verificationWorkflow = _serviceProvider.GetRequiredService<IVerificationWorkflowService>();

        // Generate test data
        _controls = GenerateTestBundle.GenerateControlRecords(RuleCount);

        // Create test profile and pack
        _profile = new Profile
        {
            ProfileId = "test-profile",
            Name = "Test Profile",
            OsTarget = OsTarget.Server2022,
            RoleTemplate = RoleTemplate.MemberServer,
            AutomationPolicy = new AutomationPolicy { NewRuleGraceDays = 0 }
        };

        _pack = new ContentPack
        {
            PackId = "test-pack",
            Name = "Test Pack",
            ReleaseDate = DateTimeOffset.UtcNow.AddDays(-30)
        };
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _serviceProvider?.Dispose();

        // Clean up temp directory
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Benchmark: Measure BundleBuilder.ExecuteAsync for Build phase.
    /// This creates the bundle directory structure and writes all manifest files.
    /// </summary>
    [Benchmark(Description = "Build phase - BundleBuilder.BuildAsync")]
    public async Task BuildPhase()
    {
        var bundleId = Guid.NewGuid().ToString("n");
        var bundleDir = Path.Combine(_tempDir, $"bundle-{bundleId}");

        var request = new BundleBuildRequest
        {
            BundleId = bundleId,
            OutputRoot = bundleDir,
            Profile = _profile,
            Pack = _pack,
            Controls = _controls.ToList(),
            ToolVersion = "1.0.0-benchmark"
        };

        var result = await _bundleBuilder.BuildAsync(request, CancellationToken.None);
    }

    /// <summary>
    /// Benchmark: Measure Apply phase (placeholder).
    /// NOTE: Real Apply requires PowerShell and system context.
    /// This is a placeholder that documents the expected measurement pattern.
    /// </summary>
    [Benchmark(Baseline = false, Description = "Apply phase - placeholder (requires PowerShell context)")]
    public void ApplyPhase()
    {
        // Placeholder: Real Apply phase benchmark would require:
        // 1. PowerShell host initialization
        // 2. ApplyRunner execution with actual scripts
        // 3. System state changes
        //
        // For baselining purposes, this documents that Apply cannot be
        // benchmarked in isolation without system context.
        //
        // To benchmark Apply in a real environment:
        // - Use a dedicated test VM or container
        // - Measure ApplyRunner.RunAsync() with real scripts
        // - Capture both duration and system resource usage

        // Simulated placeholder timing
        Thread.Sleep(1); // Minimal placeholder to avoid optimization
    }

    /// <summary>
    /// Benchmark: Measure VerificationWorkflowService with mock runners.
    /// Uses in-memory operations to isolate the workflow orchestration overhead.
    /// </summary>
    [Benchmark(Description = "Verify phase - VerificationWorkflowService (mocked)")]
    public async Task VerifyPhase()
    {
        // Create a test verify output directory
        var verifyDir = Path.Combine(_tempDir, $"verify-{Guid.NewGuid():n}");
        Directory.CreateDirectory(verifyDir);

        // Create minimal CKL files to simulate verification output
        // In real benchmarks, this would be done by actual verification tools
        var cklDir = Path.Combine(verifyDir, "CKL");
        Directory.CreateDirectory(cklDir);

        // Create a minimal CKL file for testing
        var cklPath = Path.Combine(cklDir, "test.ckl");
        await File.WriteAllTextAsync(cklPath, @"<?xml version=""1.0"" encoding=""UTF-8""?>
<CHECKLIST>
<ASSET><HOST_NAME>test</HOST_NAME></ASSET>
<STIGS><STIG><STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-100001</ATTRIBUTE_DATA></STIG_DATA>
<STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Test Rule</ATTRIBUTE_DATA></STIG_DATA>
<STIG_DATA><VULN_ATTRIBUTE>Vuln_Discussion</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Test Discussion</ATTRIBUTE_DATA></STIG_DATA>
<STIG_DATA><VULN_ATTRIBUTE>Check_Content</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Test Check</ATTRIBUTE_DATA></STIG_DATA>
<STIG_DATA><VULN_ATTRIBUTE>Fix_Text</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Test Fix</ATTRIBUTE_DATA></STIG_DATA>
<VULN><STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-100001r1_rule</ATTRIBUTE_DATA></STIG_DATA>
<STATUS>NotAFinding</STATUS><FINDING_DETAILS></FINDING_DETAILS><COMMENTS></COMMENTS></VULN></STIG>
</STIGS></CHECKLIST>");

        var request = new VerificationWorkflowRequest
        {
            OutputRoot = verifyDir,
            ConsolidatedToolLabel = "Benchmark",
            EvaluateStig = new EvaluateStigWorkflowOptions { Enabled = false },
            Scap = new ScapWorkflowOptions { Enabled = false }
        };

        var result = await _verificationWorkflow.RunAsync(request, CancellationToken.None);

        // Cleanup
        if (Directory.Exists(verifyDir))
        {
            try { Directory.Delete(verifyDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Benchmark: Measure EmassExporter.ExportAsync for Prove phase.
    /// This exports the bundle to eMASS-compatible format.
    /// </summary>
    [Benchmark(Description = "Prove phase - EmassExporter.ExportAsync")]
    public async Task ProvePhase()
    {
        // First build a bundle to export
        var bundleId = Guid.NewGuid().ToString("n");
        var bundleDir = Path.Combine(_tempDir, $"bundle-{bundleId}");

        var buildRequest = new BundleBuildRequest
        {
            BundleId = bundleId,
            OutputRoot = bundleDir,
            Profile = _profile,
            Pack = _pack,
            Controls = _controls.ToList(),
            ToolVersion = "1.0.0-benchmark"
        };

        var buildResult = await _bundleBuilder.BuildAsync(buildRequest, CancellationToken.None);

        // Create minimal consolidated results
        var verifyDir = Path.Combine(buildResult.BundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);

        var consolidatedPath = Path.Combine(verifyDir, "consolidated.json");
        var consolidatedResults = new
        {
            Results = _controls.Take(10).Select(c => new
            {
                RuleId = c.ExternalIds.RuleId,
                VulnId = c.ExternalIds.VulnId,
                Status = "NotAFinding",
                Tool = "Benchmark",
                Timestamp = DateTimeOffset.UtcNow
            }).ToList()
        };

        await File.WriteAllTextAsync(consolidatedPath,
            System.Text.Json.JsonSerializer.Serialize(consolidatedResults));

        // Export the bundle
        var exportDir = Path.Combine(_tempDir, $"export-{Guid.NewGuid():n}");

        var exportRequest = new ExportRequest
        {
            BundleRoot = buildResult.BundleRoot,
            OutputRoot = exportDir
        };

        var exportResult = await _emassExporter.ExportAsync(exportRequest, CancellationToken.None);

        // Cleanup
        try { if (Directory.Exists(bundleDir)) Directory.Delete(bundleDir, recursive: true); } catch { }
        try { if (Directory.Exists(exportDir)) Directory.Delete(exportDir, recursive: true); } catch { }
    }
}

#region Test Implementations

file sealed class TestPathBuilder : IPathBuilder
{
    private readonly string _root;

    public TestPathBuilder(string root) => _root = root;

    public string GetAppDataRoot() => _root;
    public string GetContentPacksRoot() => Path.Combine(_root, "packs");
    public string GetPackRoot(string packId) => Path.Combine(_root, "packs", packId);
    public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
    public string GetLogsRoot() => Path.Combine(_root, "logs");
    public string GetImportRoot() => Path.Combine(_root, "import");
    public string GetImportInboxRoot() => Path.Combine(_root, "import", "inbox");
    public string GetImportIndexPath() => Path.Combine(_root, "import", "index.json");
    public string GetToolsRoot() => Path.Combine(_root, "tools");
    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
        => Path.Combine(_root, "exports", $"{systemName}-{ts:yyyyMMddHHmmss}");
}

file sealed class TestHashingService : IHashingService
{
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
        => Task.FromResult("sha256:" + new string('0', 64));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
        => Task.FromResult("sha256:" + new string('0', 64));
}

file sealed class TestClassificationScopeService : IClassificationScopeService
{
    public CompiledControls Compile(Profile profile, IReadOnlyList<ControlRecord> controls)
    {
        var compiled = controls.Select(c => new CompiledControl(c, ControlStatus.Open, null, false, null)).ToList();
        return new CompiledControls(compiled, Array.Empty<CompiledControl>());
    }
}

file sealed class TestClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

file sealed class MockVerificationWorkflowService : IVerificationWorkflowService
{
    public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;

        // Create minimal artifact files
        Directory.CreateDirectory(request.OutputRoot);

        var artifacts = new
        {
            consolidatedJson = Path.Combine(request.OutputRoot, "consolidated.json"),
            consolidatedCsv = Path.Combine(request.OutputRoot, "consolidated.csv"),
            coverageJson = Path.Combine(request.OutputRoot, "coverage_summary.json"),
            coverageCsv = Path.Combine(request.OutputRoot, "coverage_summary.csv")
        };

        File.WriteAllText(artifacts.consolidatedJson, "{}");
        File.WriteAllText(artifacts.consolidatedCsv, "RuleId,Status\n");
        File.WriteAllText(artifacts.coverageJson, "{}");
        File.WriteAllText(artifacts.coverageCsv, "Status,Count\n");

        return Task.FromResult(new VerificationWorkflowResult
        {
            StartedAt = now,
            FinishedAt = now,
            ConsolidatedJsonPath = artifacts.consolidatedJson,
            ConsolidatedCsvPath = artifacts.consolidatedCsv,
            CoverageSummaryJsonPath = artifacts.coverageJson,
            CoverageSummaryCsvPath = artifacts.coverageCsv,
            ConsolidatedResultCount = 0,
            ToolRuns = Array.Empty<VerificationToolRunResult>(),
            Diagnostics = Array.Empty<string>()
        });
    }
}

#endregion

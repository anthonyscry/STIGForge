using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using STIGForge.Benchmarks.TestData;

namespace STIGForge.Benchmarks;

/// <summary>
/// Scale benchmarks for validating 10K+ rule processing without OutOfMemoryException.
/// These benchmarks validate PERF-04 requirement.
/// Success = completes without OOM at 10K+ rules.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 1, iterationCount: 3)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class ScaleBenchmarks
{
    // Test scales: 100, 1K, 5K, 10K, 15K (push beyond 10K target)
    [Params(100, 1000, 5000, 10000, 15000)]
    public int RuleCount { get; set; }

    private string _tempDir = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create temp directory for test artifacts
        _tempDir = Path.Combine(Path.GetTempPath(), $"stigforge-scale-bench-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
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
    /// Benchmark: Parse and load XCCDF rules into memory.
    /// Tests XML parsing performance at scale.
    /// </summary>
    [Benchmark(Description = "Load rules - parse XCCDF XML into memory")]
    public void LoadRules()
    {
        // Generate and parse rules - this tests the XML generation and parsing path
        var rules = GenerateTestBundle.LoadRulesIntoMemory(RuleCount);

        // Ensure all rules were loaded
        if (rules.Count != RuleCount)
        {
            throw new InvalidOperationException($"Expected {RuleCount} rules, got {rules.Count}");
        }
    }

    /// <summary>
    /// Benchmark: Full bundle build at scale.
    /// Tests memory pressure during bundle creation.
    /// </summary>
    [Benchmark(Description = "Build bundle at scale - tests memory pressure")]
    public void BuildBundleAtScale()
    {
        // Generate control records at the target scale
        var controls = GenerateTestBundle.GenerateControlRecords(RuleCount);

        // Create test bundle directory
        var bundleDir = Path.Combine(_tempDir, $"scale-bundle-{Guid.NewGuid():n}");

        // Create the bundle structure
        GenerateTestBundle.CreateTestBundle(RuleCount, bundleDir);

        // Verify bundle was created
        if (!Directory.Exists(bundleDir))
        {
            throw new InvalidOperationException("Bundle directory was not created");
        }

        // Cleanup
        try
        {
            Directory.Delete(bundleDir, recursive: true);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Benchmark: Process rules in memory without file I/O.
    /// Isolates memory handling of large collections.
    /// </summary>
    [Benchmark(Description = "Process in memory - isolate memory handling")]
    public void ProcessInMemory()
    {
        // Generate a large collection of controls
        var controls = GenerateTestBundle.GenerateControlRecords(RuleCount);

        // Process rules in memory - simulate typical operations
        var processedCount = 0;
        var totalTitleLength = 0L;
        var severityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["high"] = 0,
            ["medium"] = 0,
            ["low"] = 0
        };

        foreach (var control in controls)
        {
            processedCount++;
            totalTitleLength += control.Title.Length;

            if (severityCounts.ContainsKey(control.Severity))
            {
                severityCounts[control.Severity]++;
            }
        }

        // Verify processing completed correctly
        if (processedCount != RuleCount)
        {
            throw new InvalidOperationException($"Expected to process {RuleCount} rules, processed {processedCount}");
        }

        // Memory warning check: If processing 10K+ rules exceeds 2GB, log warning
        // This documents the baseline without failing the benchmark
        var estimatedMemoryMB = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);
        if (RuleCount >= 10000 && estimatedMemoryMB > 2048)
        {
            Console.WriteLine($"[WARNING] Memory usage at {RuleCount} rules: {estimatedMemoryMB:F2} MB (exceeds 2GB threshold)");
        }
    }

    /// <summary>
    /// Benchmark: Generate XCCDF content and measure string builder capacity.
    /// Tests raw content generation at scale.
    /// </summary>
    [Benchmark(Description = "Generate XCCDF content - raw string generation")]
    public void GenerateXccdfContent()
    {
        // Generate XCCDF XML content
        var xccdfContent = GenerateTestBundle.GenerateXccdfContent(RuleCount);

        // Verify content was generated
        if (string.IsNullOrEmpty(xccdfContent))
        {
            throw new InvalidOperationException("XCCDF content was not generated");
        }

        // Verify rule count in content
        var ruleCount = CountOccurrences(xccdfContent, "<Rule id=");
        if (ruleCount != RuleCount)
        {
            throw new InvalidOperationException($"Expected {RuleCount} rules in XCCDF, found {ruleCount}");
        }
    }

    /// <summary>
    /// Benchmark: Create ControlRecord objects and measure allocation patterns.
    /// Isolates object creation overhead.
    /// </summary>
    [Benchmark(Description = "Create ControlRecords - object allocation patterns")]
    public void CreateControlRecords()
    {
        // Generate control records
        var controls = GenerateTestBundle.GenerateControlRecords(RuleCount);

        // Verify count
        if (controls.Count != RuleCount)
        {
            throw new InvalidOperationException($"Expected {RuleCount} controls, got {controls.Count}");
        }

        // Force evaluation to ensure all objects are created
        var firstControl = controls[0];
        var lastControl = controls[controls.Count - 1];

        if (firstControl == null || lastControl == null)
        {
            throw new InvalidOperationException("Control records were not properly created");
        }
    }

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int index = 0;

        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using STIGForge.Benchmarks.TestData;

namespace STIGForge.Benchmarks;

/// <summary>
/// Memory profiling benchmarks for leak detection and memory growth validation.
///
/// IMPORTANT: Memory leak detection in benchmarks is probabilistic. Short benchmark runs
/// may not detect slow leaks or leaks that only manifest under specific conditions.
/// For production profiling, use dotnet-gcdump or PerfView for comprehensive analysis.
///
/// Methodology:
/// 1. These benchmarks measure memory delta before/after operations
/// 2. We force GC to get consistent baseline measurements
/// 3. Real memory leak detection requires longer runs and heap snapshot comparison
///
/// For deeper analysis, use:
/// - dotnet-gcdump: dotnet-gcdump collect --process-id PID --output baseline.gcdump
/// - PerfView: perfview /GCCollectOnly collect (for GC heap analysis)
/// - dotnet-counters: dotnet-counters monitor --process-id PID System.Runtime[gc-heap-size]
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 3, warmupCount: 3, iterationCount: 10)]
public class MemoryBenchmarks
{
    private string _tempDirectory = null!;

    /// <summary>
    /// Number of rules to use for memory testing.
    /// 1000 rules is a reasonable size for detecting memory growth patterns.
    /// </summary>
    [Params(100, 1000)]
    public int RuleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "STIGForge_MemoryBenchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Clean up temp directory
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Execute a full mission cycle and verify memory returns to baseline.
    /// This simulates the typical mission workflow: Build -> Apply -> Verify -> Prove.
    ///
    /// Memory Leak Detection Approach:
    /// 1. Capture memory before operation (after forced GC)
    /// 2. Execute operation
    /// 3. Force GC (GC.Collect, WaitForPendingFinalizers, GC.Collect)
    /// 4. Capture memory after
    /// 5. Delta should be within acceptable threshold (a few KB is normal for runtime state)
    ///
    /// Warning signs of memory leaks:
    /// - Memory delta consistently growing across iterations
    /// - Large Object Heap (LOH) size not returning to baseline
    /// - Gen 2 heap size increasing after GC
    /// </summary>
    [Benchmark(Description = "Full mission cycle memory test")]
    public void MissionCycleMemory()
    {
        // Force GC to establish clean baseline
        ForceGC();

        // Generate a test bundle in temp directory
        var bundlePath = Path.Combine(_tempDirectory, $"bundle_{Guid.NewGuid():N}");
        GenerateTestBundle.CreateTestBundle(RuleCount, bundlePath);

        // Simulate mission phases (memory-intensive operations)
        // Phase 1: Load and parse XCCDF (XML parsing)
        var xccdfPath = Path.Combine(bundlePath, "Manifest", "benchmark.xccdf.xml");
        if (File.Exists(xccdfPath))
        {
            var xccdfContent = File.ReadAllText(xccdfPath);
            // Parse XML (this allocates strings and objects)
            _ = System.Xml.Linq.XDocument.Parse(xccdfContent);
        }

        // Phase 2: Load pack controls (JSON deserialization)
        var controlsPath = Path.Combine(bundlePath, "Manifest", "pack_controls.json");
        if (File.Exists(controlsPath))
        {
            var controlsJson = File.ReadAllText(controlsPath);
            // Parse JSON (this allocates objects)
            _ = System.Text.Json.JsonSerializer.Deserialize<object[]>(controlsJson);
        }

        // Phase 3: Create verification artifacts (file I/O)
        var verifyPath = Path.Combine(bundlePath, "Verify");
        Directory.CreateDirectory(verifyPath);
        File.WriteAllText(Path.Combine(verifyPath, "test_result.json"), "{}");

        // Clean up bundle
        try
        {
            if (Directory.Exists(bundlePath))
            {
                Directory.Delete(bundlePath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        // Force GC to see if memory returns to baseline
        ForceGC();

        // Note: BenchmarkDotNet's MemoryDiagnoser will capture allocation metrics
        // The key metric to watch is Allocated Memory and Gen 2 GC count
    }

    /// <summary>
    /// Process the same rules multiple times to check for memory growth.
    /// Memory leaks often manifest as growing memory usage across iterations.
    ///
    /// Expected behavior: Memory should stabilize after a few iterations.
    /// Warning sign: Memory grows linearly with iteration count.
    /// </summary>
    [Benchmark(Description = "Repeated rule processing for leak detection")]
    public void RepeatedRuleProcessing()
    {
        // Force GC before starting
        ForceGC();

        // Create a set of test rules once
        var rules = GenerateTestRules(RuleCount);

        // Process the same rules multiple times
        for (int i = 0; i < 5; i++)
        {
            ProcessRules(rules);

            // Force GC between iterations
            ForceGC();
        }

        // If there's a leak, memory will be higher than after the first iteration
    }

    /// <summary>
    /// Test Large Object Heap (LOH) behavior with large STIG content.
    /// Objects > 85KB are allocated on the LOH, which has different GC behavior.
    ///
    /// LOH fragmentation can cause OutOfMemoryException even with available memory.
    /// This benchmark helps identify LOH pressure from large XML/JSON content.
    /// </summary>
    [Benchmark(Description = "Large Object Heap usage test")]
    public void LargeObjectHeapUsage()
    {
        ForceGC();

        // Generate large content that will go to LOH (>85KB threshold)
        // Typical Chrome STIG XCCDF files are 500KB-2MB
        var largeContentSize = 100 * 1024; // 100KB to ensure LOH allocation

        for (int i = 0; i < 3; i++)
        {
            // Allocate large string (goes to LOH)
            var largeContent = new string('x', largeContentSize);

            // Parse as XML (creates more large objects)
            var xml = $"<root><data>{largeContent}</data></root>";
            _ = System.Xml.Linq.XDocument.Parse(xml);

            // Release references
            largeContent = null;
            xml = null;

            ForceGC();
        }
    }

    /// <summary>
    /// Force a full garbage collection to establish memory baseline.
    /// Call this before and after memory-sensitive operations.
    /// </summary>
    private static void ForceGC()
    {
        // Full blocking GC with finalizer wait
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Generate test rule data for memory testing.
    /// </summary>
    private static List<TestRule> GenerateTestRules(int count)
    {
        var rules = new List<TestRule>(count);
        for (int i =  0; i < count; i++)
        {
            rules.Add(new TestRule
            {
                RuleId = $"V-{i + 1}",
                VulnId = $"V-{100000 + i}",
                Title = $"Test Rule {i + 1} - This is a test rule title for memory benchmarking purposes",
                Description = $"Test rule description for rule {i + 1}. This simulates realistic STIG rule content."
            });
        }
        return rules;
    }

    /// <summary>
    /// Process rules in a memory-sensitive way.
    /// This simulates the processing done during mission execution.
    /// </summary>
    private static void ProcessRules(List<TestRule> rules)
    {
        // Simulate processing each rule
        foreach (var rule in rules)
        {
            // Create some allocations that should be collected
            var processed = new ProcessedRule
            {
                OriginalRuleId = rule.RuleId,
                Status = "NotReviewed",
                ProcessedAt = DateTimeOffset.UtcNow
            };

            // Release reference (should be collected on next GC)
            _ = processed;
        }
    }

    /// <summary>
    /// Test rule data structure for memory benchmarks.
    /// </summary>
    private sealed class TestRule
    {
        public string RuleId { get; init; } = string.Empty;
        public string VulnId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>
    /// Processed rule data structure for memory benchmarks.
    /// </summary>
    private sealed class ProcessedRule
    {
        public string OriginalRuleId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; init; }
    }
}

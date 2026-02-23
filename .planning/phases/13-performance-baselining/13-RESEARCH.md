# Phase 13: Performance Baselining - Research

**Researched:** 2026-02-22
**Domain:** .NET 8 Performance Measurement, Benchmarking, and Memory Profiling
**Confidence:** HIGH

## Summary

This phase establishes documented performance baselines for STIGForge Next, validating that the system meets startup time targets (< 3s cold, < 1s warm), handles 10K+ rules without memory issues, and provides measurable mission duration expectations. The phase builds on the telemetry infrastructure from Phase 12 (MissionTracingService, TraceFileListener) to collect real-world performance data, then uses BenchmarkDotNet for reproducible microbenchmarks and dotnet-trace/dotnet-counters for profiling.

**Primary recommendation:** Use BenchmarkDotNet for reproducible startup and mission benchmarks, dotnet-counters for real-time memory/CPU monitoring during scale testing, and PerfView for deep memory leak analysis. Establish baselines in a benchmark project separate from test assemblies.

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| OBSV-03 | Performance metrics collected (startup time, mission duration, memory usage) | PerformanceInstrumenter service with EventCounters; existing MissionTracingService duration capture; dotnet-counters for real-time metrics |
| PERF-01 | Cold startup time baseline established (< 3s target) | BenchmarkDotNet with RunStrategy.ColdStart; process-per-iteration measurement; WPF splash screen timing |
| PERF-02 | Warm startup time baseline established (< 1s target) | BenchmarkDotNet with default (warm) strategy; measure from App.OnStartup to MainWindow.Loaded |
| PERF-03 | Mission duration baselines documented for each mission type | BenchmarkDotNet IJobAsyncTask patterns; measure Build, Apply, Verify, Prove phases with 100/1K/10K rule datasets |
| PERF-04 | Scale testing validates 10K+ rule processing without OOM | dotnet-counters memory monitoring; dotnet-gcdump for heap snapshots; Chrome STIG test fixtures |
| PERF-05 | Memory profile baseline established with leak detection | PerfView GC heap analysis; dotnet-gcdump compare snapshots; memory diagnosters in BenchmarkDotNet |
| PERF-06 | I/O bottlenecks identified and documented | PerfView disk I/O analysis; Windows Performance Monitor counters; dotnet-trace for file operation profiling |

</phase_requirements>

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **BenchmarkDotNet** | 0.15.2 | Microbenchmarking framework | Canonical .NET benchmarking tool. Statistical analysis, memory diagnosis, .NET 8 support, reproducible results with warmup/iteration control. |
| **System.Diagnostics.PerformanceCounter** | (built-in .NET 8) | Windows performance counters | Built-in, no package needed. Access to disk I/O, memory, CPU counters for bottleneck identification. |

### Supporting (Global Tools)

| Tool | Purpose | When to Use |
|------|---------|-------------|
| **dotnet-counters** | Real-time performance monitoring | During scale testing (10K+ rules), memory profiling, startup analysis. Live CPU, GC, thread pool metrics. |
| **dotnet-trace** | Production trace collection | CPU profiling, GC behavior, method-level timing. Generates .nettrace files for PerfView/VS analysis. |
| **dotnet-gcdump** | GC heap snapshot collection | Memory leak detection. Compare heap snapshots before/after operations. |
| **dotnet-dump** | Full memory dump collection | Crash analysis, deep memory investigation. SOS debugging commands. |
| **PerfView** | ETW-based analysis | Deep memory analysis, GC investigation, I/O profiling. Microsoft's free performance analyzer. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| BenchmarkDotNet | NBench | NBench is less actively maintained. BenchmarkDotNet is the industry standard with better documentation. |
| dotnet-counters | Windows Performance Monitor | PerfMon has richer UI but requires manual configuration. dotnet-counters is CLI-friendly for CI/CD. |
| PerfView | dotMemory (JetBrains) | dotMemory has better UX but requires license. PerfView is free and sufficient for baseline analysis. |
| dotnet-gcdump | dotnet-dump | gcdump is lightweight and analyzable without debug symbols. Use full dump only for crash analysis. |

**Installation:**

```bash
# BenchmarkDotNet (add to new benchmark project)
dotnet new console -n STIGForge.Benchmarks -o benchmarks/STIGForge.Benchmarks
dotnet add benchmarks/STIGForge.Benchmarks package BenchmarkDotNet --version 0.15.2

# Global diagnostic tools (if not already installed)
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-gcdump
dotnet tool install -g dotnet-dump

# PerfView (download from Microsoft)
# https://github.com/microsoft/perfview/releases
```

## Architecture Patterns

### Recommended Project Structure

```
benchmarks/
└── STIGForge.Benchmarks/
    ├── STIGForge.Benchmarks.csproj
    ├── Program.cs                    # BenchmarkSwitcher entry point
    ├── StartupBenchmarks.cs          # Cold/warm startup timing
    ├── MissionBenchmarks.cs          # Build/Apply/Verify/Prove phases
    ├── ScaleBenchmarks.cs            # 100/1K/10K rule processing
    ├── MemoryBenchmarks.cs           # Allocation and GC pressure
    └── TestData/
        └── Chrome_STIG_10K_Rules.xml # Large test fixture

docs/
└── performance/
    ├── BASELINES.md                  # Documented baselines
    ├── MEMORY_PROFILE.md             # Memory characteristics
    └── IO_BOTTLENECKS.md             # I/O analysis results
```

### Pattern 1: Startup Time Benchmark

**What:** Measure cold and warm startup times for WPF application
**When to use:** Establishing PERF-01 and PERF-02 baselines

```csharp
// Source: BenchmarkDotNet documentation + WPF patterns
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 3, warmupCount: 1, iterationCount: 5)]
public class StartupBenchmarks
{
    private string _testBundlePath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _testBundlePath = CreateMinimalTestBundle();
    }

    // Cold start: new process per iteration
    [Benchmark(Description = "Cold startup time")]
    [ColdStart]
    public void ColdStartup()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "STIGForge.App.exe",
            Arguments = $"--bundle \"{_testBundlePath}\" --exit-after-load",
            UseShellExecute = false
        });
        process?.WaitForExit();
    }

    // Warm start: measured inside already-running process
    [Benchmark(Description = "Warm startup time (internal)")]
    public void WarmStartup()
    {
        // This measures internal initialization after process is warm
        // Real warm startup requires external orchestration
    }
}
```

### Pattern 2: Mission Duration Benchmark

**What:** Measure mission phase durations with varying scales
**When to use:** Establishing PERF-03 baselines

```csharp
// Source: BenchmarkDotNet async patterns
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class MissionBenchmarks
{
    private IServiceProvider _services = null!;
    private string _bundle100 = null!;
    private string _bundle1000 = null!;
    private string _bundle10000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _services = BuildTestServiceProvider();
        _bundle100 = CreateTestBundle(ruleCount: 100);
        _bundle1000 = CreateTestBundle(ruleCount: 1000);
        _bundle10000 = CreateTestBundle(ruleCount: 10000);
    }

    [Params(100, 1000, 10000)]
    public int RuleCount { get; set; }

    [Benchmark(Description = "Build phase")]
    public async Task BuildPhase()
    {
        var orchestrator = _services.GetRequiredService<BuildOrchestrator>();
        var bundle = RuleCount switch
        {
            100 => _bundle100,
            1000 => _bundle1000,
            _ => _bundle10000
        };
        await orchestrator.ExecuteAsync(bundle);
    }

    [Benchmark(Description = "Apply phase")]
    public async Task ApplyPhase()
    {
        // Similar pattern for apply
    }
}
```

### Pattern 3: Memory Leak Detection

**What:** Capture and compare heap snapshots to detect memory leaks
**When to use:** Establishing PERF-05 baseline

```bash
# Capture baseline heap snapshot
dotnet-gcdump collect --process-id <PID> --output baseline.gcdump

# Run operation that may leak (e.g., process 10K rules)
# ... execute mission ...

# Capture post-operation snapshot
dotnet-gcdump collect --process-id <PID> --output after_mission.gcdump

# Analyze with dotnet-gcdump
dotnet-gcdump analyze baseline.gcdump
dotnet-gcdump analyze after_mission.gcdump

# Or use PerfView for deeper analysis
perfview /GCCollectOnly collect
```

### Pattern 4: Real-time Monitoring with dotnet-counters

**What:** Monitor memory and CPU during scale testing
**When to use:** PERF-04 scale testing, PERF-05 memory profiling

```bash
# Start monitoring a running process
dotnet-counters monitor --process-id <PID> \
    --counters System.Runtime[gc-heap-size,gc-alloc-rate,cpu-usage,working-set]

# Or monitor with refresh interval
dotnet-counters monitor --process-id <PID> --refresh-interval 1 \
    System.Runtime

# Export to file for analysis
dotnet-counters collect --process-id <PID> --output-format json \
    --output counters.json
```

### Anti-Patterns to Avoid

- **Benchmarking with synthetic data only:** Must use realistic STIG data (Chrome STIG with 10K+ rules). Synthetic benchmarks don't reflect real XML parsing complexity.
- **Running benchmarks in Debug mode:** Always use Release mode with optimizations enabled. BenchmarkDotNet warns about this.
- **Ignoring JIT warmup:** First runs are always slower. BenchmarkDotNet handles warmup automatically; manual timing does not.
- **Single-sample measurements:** Performance varies due to GC, CPU throttling, background processes. Always use statistical analysis with multiple iterations.
- **Profiling on developer machines only:** Hardware varies. Document hardware specs with baseline measurements.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Timing microbenchmarks | Stopwatch-based loops | BenchmarkDotNet | Statistical analysis, warmup, outlier detection, memory diagnosis |
| Memory profiling | Custom object counters | dotnet-gcdump, PerfView | GC heap analysis requires CLR internals knowledge |
| Real-time metrics | Custom performance counters | dotnet-counters | Built-in EventCounter integration, no code changes |
| I/O profiling | File system timing | dotnet-trace, PerfView | ETW-based, captures kernel-level I/O without instrumentation |
| Baseline comparison | Spreadsheet tracking | BenchmarkDotNet + BaselineComparer | Automated diff detection, statistical significance |

**Key insight:** .NET diagnostics tools are mature and battle-tested. Custom timing code introduces measurement errors, doesn't handle warmup, and lacks statistical rigor.

## Common Pitfalls

### Pitfall 1: Non-Deterministic Performance Results

**What goes wrong:** Benchmark results vary 20%+ between runs, making it impossible to establish reliable baselines or detect regressions.

**Why it happens:** No warmup period, running on shared hardware, GC not controlled, background processes interfering, thermal throttling.

**How to avoid:**
1. Use BenchmarkDotNet with proper configuration (launchCount: 3+, warmupCount: 3+, iterationCount: 10+)
2. Run on dedicated or quiet hardware (avoid CI agents under load)
3. Close unnecessary applications during benchmarking
4. Use `[MemoryDiagnoser]` to track GC behavior
5. Document hardware specs with baseline measurements

**Warning signs:** Mean/Max spread > 2x, confidence intervals overlapping zero, results change on re-run

### Pitfall 2: Benchmarking Without Realistic Data

**What goes wrong:** Benchmarks pass with synthetic data but fail with real STIG files. Startup time measured with empty bundle doesn't reflect production.

**Why it happens:** Creating test fixtures is tedious, real STIGs are large, XML parsing has unexpected complexity not captured by synthetic tests.

**How to avoid:**
1. Use actual Chrome STIG or similar large STIG as test fixture
2. Create bundles with varying sizes (100, 1K, 10K rules)
3. Include realistic file I/O (read from disk, not in-memory)
4. Test both happy path and edge cases (missing files, large files)

**Warning signs:** Benchmark shows 50ms but production shows 500ms, memory usage higher in production than tests

### Pitfall 3: Missing Memory Leak During Scale Testing

**What goes wrong:** Scale test passes without OOM but memory keeps growing. Leak only detected after hours of production use.

**Why it happens:** 10K rules fit in available memory, leak is slow, GC delays the crash, test doesn't run long enough.

**How to avoid:**
1. Use dotnet-gcdump to capture snapshots before and after operations
2. Compare heap object counts (should return to baseline)
3. Run PerfView GC analysis during scale testing
4. Implement memory assertions in scale tests (fail if memory grows > threshold)
5. Test repeated mission cycles (not just single mission)

**Warning signs:** Memory doesn't return to baseline after GC, object counts grow with each iteration, LOH size increases

### Pitfall 4: WPF-Specific Startup Bottlenecks Missed

**What goes wrong:** Startup benchmark shows good time but UI is unresponsive for seconds. WPF dispatcher operations not captured.

**Why it happens:** Benchmark measures process start, not UI responsiveness. WPF initialization happens asynchronously. Splash screen obscures actual load time.

**How to avoid:**
1. Measure time to MainWindow.Loaded event, not just process start
2. Use dispatcher timestamp capture in App.OnStartup
3. Profile with PerfView to identify dispatcher queue blocking
4. Document "perceived" startup time (splash screen to interactive)

**Warning signs:** Benchmark shows < 1s but UI frozen for 3s, dispatcher queue grows during startup, UI tests timeout

### Pitfall 5: I/O Bottleneck Misidentification

**What goes wrong:** Time spent optimizing SQLite queries when actual bottleneck is XML parsing or file system contention.

**Why it happens:** Intuition about bottlenecks is often wrong. I/O has multiple layers (disk, OS cache, SQLite page cache).

**How to avoid:**
1. Profile first with dotnet-trace to see actual time distribution
2. Use PerfView disk I/O analysis to identify file system bottlenecks
3. Measure with realistic file locations (network drives vs. local SSD)
4. Compare WAL vs. journal mode for SQLite
5. Document findings before optimizing

**Warning signs:** Optimization shows no improvement, profiler shows different hot path than expected, I/O time varies wildly

## Code Examples

### BenchmarkDotNet Configuration

```csharp
// Source: BenchmarkDotNet documentation
// Program.cs - Entry point for benchmark runner

using BenchmarkDotNet.Running;
using STIGForge.Benchmarks;

var switcher = new BenchmarkSwitcher(new[]
{
    typeof(StartupBenchmarks),
    typeof(MissionBenchmarks),
    typeof(ScaleBenchmarks),
    typeof(MemoryBenchmarks)
});

// Run specific benchmark: dotnet run -c Release --filter "*Startup*"
// Run all: dotnet run -c Release
switcher.Run(args);
```

### Performance Baseline Documentation Template

```markdown
# Performance Baselines

**Date:** 2026-02-22
**Hardware:** Intel i7-12700K, 32GB RAM, NVMe SSD
**Runtime:** .NET 8.0.x, Windows 11

## Startup Time

| Metric | Target | Baseline | Status |
|--------|--------|----------|--------|
| Cold startup | < 3s | TBD | Pending |
| Warm startup | < 1s | TBD | Pending |
| Perceived (splash to interactive) | < 2s | TBD | Pending |

## Mission Duration (by scale)

| Mission | 100 rules | 1K rules | 10K rules |
|---------|-----------|----------|-----------|
| Build | TBD | TBD | TBD |
| Apply | TBD | TBD | TBD |
| Verify | TBD | TBD | TBD |
| Prove | TBD | TBD | TBD |

## Memory Profile

| Metric | Baseline | Notes |
|--------|----------|-------|
| Idle memory | TBD | After startup, no mission |
| Peak (10K rules) | TBD | During mission execution |
| Post-mission | TBD | After GC, should return to idle |
| LOH size | TBD | Large object heap |

## I/O Characteristics

| Operation | Rate | Notes |
|-----------|------|-------|
| XML parsing | TBD | Rules/second |
| SQLite writes | TBD | Transactions/second |
| File reads | TBD | MB/second |
```

### PerformanceInstrumenter Service (for OBSV-03)

```csharp
// Source: Based on existing MissionTracingService pattern
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace STIGForge.Infrastructure.Telemetry;

/// <summary>
/// Collects performance metrics for mission execution and startup.
/// Uses .NET 8 built-in Meter for OpenTelemetry compatibility.
/// </summary>
public sealed class PerformanceInstrumenter
{
    private static readonly Meter Meter = new("STIGForge.Performance");
    private static readonly Counter<long> MissionsCompleted = Meter.CreateCounter<long>("missions.completed");
    private static readonly Histogram<double> MissionDuration = Meter.CreateHistogram<double>("mission.duration_ms");
    private static readonly Histogram<double> StartupTime = Meter.CreateHistogram<double>("startup.duration_ms");
    private static readonly Counter<long> RulesProcessed = Meter.CreateCounter<long>("rules.processed");

    public void RecordMissionCompleted(string missionType, int ruleCount, double durationMs)
    {
        MissionsCompleted.Add(1, new KeyValuePair<string, object?>("mission.type", missionType));
        MissionDuration.Record(durationMs, new KeyValuePair<string, object?>("mission.type", missionType));
        RulesProcessed.Add(ruleCount);
    }

    public void RecordStartupTime(double durationMs, bool isColdStart)
    {
        StartupTime.Record(durationMs, new KeyValuePair<string, object?>("startup.cold", isColdStart));
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Stopwatch-based timing | BenchmarkDotNet statistical analysis | 2018+ | Reproducible results, handles warmup |
| Windows Performance Monitor only | dotnet-counters CLI | .NET Core 3.0+ | Cross-platform, CI-friendly |
| Full memory dumps | dotnet-gcdump lightweight snapshots | .NET 6+ | Faster analysis, smaller files |
| Manual baseline tracking | BenchmarkDotNet BaselineComparer | BenchmarkDotNet 0.13+ | Automated regression detection |
| Custom performance counters | EventCounters + System.Diagnostics.Metrics | .NET 5+ | OpenTelemetry compatible |

**Deprecated/outdated:**
- **Stopwatch in loops:** Use BenchmarkDotNet instead. Manual timing lacks warmup, statistical analysis, and outlier detection.
- **PerformanceCounter (Windows-only):** Use dotnet-counters or EventCounters for cross-platform support.

## Open Questions

1. **Chrome STIG availability for 10K+ rule test fixture**
   - What we know: Chrome STIG is mentioned in project context as test fixture
   - What's unclear: Exact rule count, where to obtain, licensing considerations
   - Recommendation: Check existing test fixtures directory, or generate synthetic STIG with realistic complexity

2. **WPF cold start measurement methodology**
   - What we know: BenchmarkDotNet supports ColdStart strategy, WPF has async initialization
   - What's unclear: How to reliably measure "UI interactive" time vs. process start time
   - Recommendation: Use process exit with --exit-after-load flag for consistent measurement, document methodology

3. **CI/CD integration for performance regression detection**
   - What we know: BenchmarkDotNet can export results to JSON/Markdown
   - What's unclear: Whether to run benchmarks in CI or separate performance lab
   - Recommendation: Start with manual baseline documentation, add CI integration after methodology is validated

## Sources

### Primary (HIGH confidence)
- BenchmarkDotNet Documentation (https://benchmarkdotnet.org/) - Official docs, .NET 8 support verified
- .NET Diagnostics Tools (https://learn.microsoft.com/en-us/dotnet/core/diagnostics/) - Official Microsoft docs for dotnet-counters, dotnet-trace, dotnet-gcdump
- PerfView Tutorial (https://learn.microsoft.com/en-us/shows/perfview-tutorial/) - Microsoft's official performance analysis tool
- STIGForge Codebase - MissionTracingService, TraceFileListener, LoggingConfiguration verified by reading source

### Secondary (MEDIUM confidence)
- .NET 8 Performance Improvements (https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/) - Performance patterns and expectations
- SQLite Performance Benchmarks 2025 (https://sqlite.org/whentouse.html) - Transaction optimization patterns
- WPF Performance Optimization (https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware) - WPF-specific considerations

### Tertiary (LOW confidence)
- Industry startup time benchmarks - Varies by application complexity, STIGForge-specific baselines needed
- Memory leak patterns in WPF applications - General patterns, requires project-specific validation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All tools are canonical .NET ecosystem tools with active maintenance and .NET 8 support
- Architecture: HIGH - BenchmarkDotNet patterns are well-documented; existing telemetry infrastructure provides foundation
- Pitfalls: MEDIUM - Based on industry research; some STIGForge-specific scenarios (10K+ rules, WPF) need validation

**Research date:** 2026-02-22
**Valid until:** 30 days (stable tooling, but .NET 9 may change patterns in late 2026)

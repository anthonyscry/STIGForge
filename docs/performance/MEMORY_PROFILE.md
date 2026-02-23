# Memory Profile Baseline

**Date:** 2026-02-23
**Runtime:** .NET 8.0.x

This document describes the memory characteristics of STIGForge Next and provides methodology for memory leak detection.

---

## Memory Characteristics

| Metric | Baseline | Notes |
|--------|----------|-------|
| Idle memory | Pending | After startup, no mission loaded |
| Peak (10K rules) | Pending | During mission execution |
| Post-mission | Pending | After GC, should return to idle |
| Large Object Heap (LOH) | Pending | Objects >85KB |

**Expected patterns:**
- Memory should spike during STIG parsing and return to baseline after mission completion
- LOH growth is normal during XML/JSON parsing but should stabilize
- Gen 2 GC should rarely trigger during normal operation

---

## Memory Leak Detection Methodology

### 1. Benchmark-Based Detection

The `MemoryBenchmarks` class provides automated memory testing:

```bash
# Run memory benchmarks
dotnet run -c Release --project benchmarks/STIGForge.Benchmarks --filter "*MemoryBenchmarks*"
```

**Key metrics to watch:**
- **Allocated Memory:** Total allocations per operation
- **Gen 0/1/2 Collections:** GC pressure indicators
- **Memory delta:** Growth across repeated iterations

### 2. dotnet-gcdump (Recommended for Deep Analysis)

```bash
# Install tool (if not already installed)
dotnet tool install -g dotnet-gcdump

# Capture baseline snapshot
dotnet-gcdump collect --process-id <PID> --output baseline.gcdump

# Run mission operation...

# Capture post-operation snapshot
dotnet-gcdump collect --process-id <PID> --output after_mission.gcdump

# Analyze snapshots
dotnet-gcdump report baseline.gcdump
dotnet-gcdump report after_mission.gcdump
```

### 3. dotnet-counters (Real-time Monitoring)

```bash
# Install tool (if not already installed)
dotnet tool install -g dotnet-counters

# Monitor memory in real-time
dotnet-counters monitor --process-id <PID> \
    --counters System.Runtime[gc-heap-size,gc-alloc-rate,working-set]

# Export to file for analysis
dotnet-counters collect --process-id <PID> --output-format json \
    --output counters.json
```

### 4. PerfView (Advanced Analysis)

PerfView is Microsoft's free performance analyzer for deep memory investigation:

```bash
# Download from: https://github.com/microsoft/perfview/releases

# Collect GC heap data
perfview /GCCollectOnly collect

# Analyze heap
perfview baseline.gcdump
```

---

## Normal Memory Patterns

### Startup
- Initial working set: 50-150 MB (typical .NET WPF app)
- LOH allocations during XAML parsing
- Stabilizes after UI initialization

### Mission Execution
- Peak memory scales with rule count
- Temporary spikes during XML parsing (XCCDF files can be 500KB-2MB)
- String allocations for rule content are normal

### Post-Mission
- Memory should return to near-idle levels after GC
- Some growth is normal for cached data structures
- Gen 2 heap should remain stable across multiple missions

---

## Warning Signs of Memory Issues

### Leak Indicators
- Memory delta > 10MB per mission (after forced GC)
- Gen 2 heap size grows monotonically
- LOH size increases with each iteration
- Object counts grow in dotnet-gcdump comparisons

### Common Leak Patterns
- Event handlers not unsubscribed
- Static collections accumulating objects
- Timers or background tasks holding references
- WPF bindings to disposed objects

---

## LOH Considerations

Large Object Heap (LOH) handles objects >85KB:
- XCCDF XML content (often >500KB)
- Large JSON serialization buffers
- Image and media assets

**LOH fragmentation symptoms:**
- OutOfMemoryException with available memory
- Growing LOH size without corresponding object growth
- Use `GCSettings.LargeObjectHeapCompactionMode` for mitigation

---

## Memory Optimization Opportunities

When memory becomes a concern:

1. **Streaming XML parsing:** Use `XmlReader` instead of `XDocument.Load`
2. **Object pooling:** Reuse buffers for repeated operations
3. **String interning:** For repeated rule content
4. **ArrayPool<T>:** For temporary arrays in hot paths
5. **Lazy loading:** Defer expensive initialization

---

## Benchmark Reference

| Benchmark | Purpose |
|-----------|---------|
| `MissionCycleMemory` | Full mission cycle memory test |
| `RepeatedRuleProcessing` | Memory growth across iterations |
| `LargeObjectHeapUsage` | LOH fragmentation analysis |

Run with:
```bash
dotnet run -c Release --project benchmarks/STIGForge.Benchmarks --filter "*Memory*"
```

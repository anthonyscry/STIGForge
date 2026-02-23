# I/O Bottlenecks Analysis

**Date:** 2026-02-23
**Status:** Analysis complete, measurements pending

This document identifies I/O bottlenecks in STIGForge Next and provides methodology for I/O profiling.

---

## I/O Characteristics

| Operation | Rate | Notes |
|-----------|------|-------|
| XML parsing (XCCDF) | Pending | Rules/second |
| SQLite writes | Pending | Transactions/second |
| File reads (bundle assets) | Pending | MB/second |
| Evidence file generation | Pending | Files/second |

---

## Known Bottlenecks (Architecture Analysis)

### 1. XML Parsing of Large XCCDF Files

**Location:** Content import pipeline
**Characteristics:**
- XCCDF files can be 500KB-2MB
- `XDocument.Load` loads entire file into memory
- String allocations for every element and attribute

**Mitigation opportunities:**
- Streaming `XmlReader` for large files
- Async loading with cancellation support
- Caching parsed structures

### 2. SQLite Transaction Patterns During Apply

**Location:** `BundleOrchestrator`, `MissionRunRepository`
**Characteristics:**
- Multiple small transactions during mission execution
- WAL mode recommended for concurrent access
- Sync mode affects durability vs. performance

**Current configuration:**
- Data Source path: `%ProgramData%/STIGForge/data/stigforge.db`
- Connection pooling enabled

**Mitigation opportunities:**
- Batch inserts during timeline event recording
- Pre-allocated journal file
- Tune `synchronous` and `journal_mode` PRAGMA

### 3. File System Contention During Bundle Creation

**Location:** `BundleBuilder`, `VerificationArtifactAggregationService`
**Characteristics:**
- Multiple concurrent file writes (Manifest, Apply scripts, Evidence)
- Directory creation overhead
- Antivirus scanning on Windows can add latency

**Mitigation opportunities:**
- Parallel file writes where safe
- Pre-create directory structure
- Buffered writes for small files

---

## Profiling Methodology

### 1. dotnet-trace (CPU and I/O)

```bash
# Install tool (if not already installed)
dotnet tool install -g dotnet-trace

# Collect trace during mission execution
dotnet-trace collect --process-id <PID> --duration 00:01:00

# Analyze with Visual Studio or PerfView
# The .nettrace file shows I/O wait times
```

### 2. PerfView Disk I/O Analysis

```bash
# Collect disk I/O data
perfview /DiskIO collect

# Analyze file system activity
perfview trace.nettrace
```

### 3. Windows Performance Monitor

Built-in Windows counters for I/O analysis:
- PhysicalDisk\Disk Read Bytes/sec
- PhysicalDisk\Disk Write Bytes/sec
- PhysicalDisk\Avg. Disk sec/Read
- PhysicalDisk\Avg. Disk sec/Write

### 4. Custom Instrumentation

The `MissionTracingService` and `PerformanceInstrumenter` provide activity tracking:
- Phase durations logged to `traces.json`
- File operations wrapped in Activity spans
- Custom metrics for I/O-bound operations

---

## I/O Hot Paths

### Mission Execution Flow

```
BundleOrchestrator.OrchestrateAsync
├── LoadOverlayDecisions (File.ReadAllText + JSON deserialize)
├── LoadBundleControls (File.ReadAllText + JSON deserialize)
├── Apply Phase
│   ├── PowerShell script execution (Process.Start)
│   └── File system writes (logs, markers)
├── Verify Phase
│   ├── Evaluate-STIG execution (Process.Start)
│   ├── SCAP execution (Process.Start)
│   └── Report file reads
└── Evidence Phase
    └── WriteCoverageArtifacts (JSON file writes)
```

### Content Import Flow

```
ContentPackImporter.ImportAsync
├── Extract ZIP archive
├── Parse XCCDF XML (memory-intensive)
├── Parse additional XML (DISA STIG format)
└── SQLite bulk insert (transaction-bound)
```

---

## Future Optimization Opportunities

These are documented for future consideration - not implemented in this phase:

### High Impact
1. **Streaming XML parsing:** Replace `XDocument.Load` with `XmlReader` for large files
2. **SQLite batch operations:** Aggregate timeline events before insert
3. **Parallel bundle generation:** Multi-threaded file creation

### Medium Impact
1. **File handle pooling:** Reuse file handles for repeated operations
2. **Memory-mapped files:** For large STIG content access
3. **Compressed storage:** Reduce disk I/O for bundle assets

### Low Impact
1. **Async file operations:** Already using async where beneficial
2. **Buffer size tuning:** .NET defaults are generally optimal
3. **Directory pre-allocation:** Minor improvement on SSD

---

## Measurement Commands

When running baseline measurements:

```bash
# Profile with dotnet-trace during mission
dotnet-trace collect -p <PID> --providers Microsoft-Windows-Kernel-File

# Monitor disk I/O in real-time
dotnet-counters monitor -p <PID> System.Runtime[io-reads,io-writes]

# Generate test bundle for I/O testing
# (Creates files to measure write performance)
```

---

## Related Documentation

- [BASELINES.md](./BASELINES.md) - Overall performance baselines
- [MEMORY_PROFILE.md](./MEMORY_PROFILE.md) - Memory characteristics

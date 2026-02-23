# Phase 12: Observability Integration - Research

**Researched:** 2026-02-22
**Domain:** .NET 8 observability, OpenTelemetry Activity tracing, Serilog correlation, offline diagnostics
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
No `*-CONTEXT.md` file exists for this phase. Use provided objective/requirements as the lock source:
- Phase must address requirement IDs: OBSV-02, OBSV-05, OBSV-06
- Success criteria defined:
  1. Mission lifecycle (Build -> Apply -> Verify -> Prove) emits trace spans that can be correlated
  2. Trace IDs propagate across PowerShell process boundaries during mission execution
  3. Debug export bundles can be created and contain all diagnostics needed for offline support

### Claude's Discretion
No explicit discretion section exists. Recommended discretion boundaries for planning:
- Choose specific ActivitySource naming convention (e.g., "STIGForge.Missions")
- Choose trace export format (JSON files for offline-first, OTLP optional)
- Choose debug bundle structure and contents

### Deferred Ideas (OUT OF SCOPE)
- Real-time streaming telemetry (offline-first architecture)
- Cloud-based observability platforms (OBSV-07, OBSV-08 in v1.2)
- Deterministic replay from traces (v1.2+)
- Compliance-specific metrics (v1.2+)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| OBSV-02 | Mission-level tracing spans Build -> Apply -> Verify -> Prove lifecycle | System.Diagnostics.ActivitySource pattern with Activity spans; existing BundleOrchestrator provides lifecycle phases |
| OBSV-05 | Debug export bundles create portable diagnostics for offline support | Local file-based export pattern combining logs, traces, and bundle artifacts into ZIP archive |
| OBSV-06 | Trace IDs propagate across PowerShell process boundaries | Environment variable propagation pattern (STIGFORGE_TRACE_ID); PowerShell scripts receive via $env:STIGFORGE_TRACE_ID |
</phase_requirements>

## Summary

This phase adds end-to-end mission observability by instrumenting the mission lifecycle with OpenTelemetry-compatible Activity tracing, propagating trace context across PowerShell process boundaries, and enabling creation of portable debug export bundles for offline support scenarios. The existing infrastructure provides a solid foundation: CorrelationIdEnricher already uses Activity.Current for Serilog correlation, and the BundleOrchestrator already tracks mission phases via MissionTimelineEvent records.

The key technical challenge is PowerShell 5.1 process boundary propagation. Unlike modern HTTP/gRPC scenarios where W3C trace context propagates automatically via headers, PowerShell processes launched by ApplyRunner need explicit trace context injection via environment variables. The recommended pattern is: capture Activity.Current.Context at launch time, serialize TraceId/SpanId to STIGFORGE_TRACE_ID/STIGFORGE_PARENT_SPAN_ID environment variables, and have child processes either (a) pass them through to further children or (b) create linked activities. Debug export bundles aggregate all diagnostic artifacts (logs, traces, bundle manifests, verification results) into a timestamped ZIP file.

**Primary recommendation:** Implement a MissionTracingService with ActivitySource that wraps BundleOrchestrator phases; modify ApplyRunner PowerShell process creation to inject trace context via environment variables; create DebugBundleExporter that aggregates logs, traces, and bundle artifacts.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Diagnostics.Activity | (built-in .NET 8) | Trace span creation | Native W3C trace context support; already used by CorrelationIdEnricher |
| System.Diagnostics.ActivitySource | (built-in .NET 8) | Tracer factory | .NET standard for OpenTelemetry-compatible tracing |
| Serilog | 4.3.0 (existing) | Structured logging | Already configured with CorrelationIdEnricher that uses Activity.Current |
| System.IO.Compression | (built-in .NET 8) | ZIP archive creation | Native .NET support for debug bundles |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| OpenTelemetry.Exporter.Console | 1.12.0 (optional) | Development debugging | During development to visualize trace output |
| Serilog.Expressions | 5.0+ (optional) | Advanced log filtering | If debug bundles need filtered log content |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Diagnostics.Activity | OpenTelemetry SDK full tracing | Activity is built-in, zero dependencies; full SDK needed only for OTLP export |
| Environment variable propagation | WCF/HTTP header propagation | PowerShell 5.1 has no native W3C trace context support; env vars are universal |
| JSON trace files | OTLP protocol files | JSON is human-readable and tool-agnostic; OTLP requires collector infrastructure |
| Custom debug bundles | dotnet-dump + manual collection | Bundles are automated and mission-scoped; dumps are ad-hoc and larger |

**No new NuGet packages required for core functionality.** Optional packages for enhanced debugging:
```bash
# Optional - for development trace visualization
dotnet add package OpenTelemetry.Exporter.Console --version 1.12.0
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── STIGForge.Infrastructure/
│   ├── Logging/
│   │   ├── CorrelationIdEnricher.cs    # EXISTS - uses Activity.Current
│   │   └── LoggingConfiguration.cs     # EXISTS - LevelSwitch configuration
│   └── Telemetry/                      # NEW
│       ├── MissionTracingService.cs    # ActivitySource management, span creation
│       ├── ActivitySourceNames.cs      # Centralized source/span names
│       └── DebugBundleExporter.cs      # Aggregates diagnostics into ZIP
├── STIGForge.Apply/
│   └── ApplyRunner.cs                  # MODIFY - inject trace context to PowerShell
└── STIGForge.Build/
    └── BundleOrchestrator.cs           # MODIFY - wrap phases with Activity spans
```

### Pattern 1: ActivitySource for Mission Tracing
**What:** Create a centralized ActivitySource for mission operations and use Activity spans for each phase.
**When to use:** All mission lifecycle operations (Build, Apply, Verify, Prove).
**Example:**
```csharp
// Source: Microsoft Learn Activity documentation + OpenTelemetry .NET patterns
// Infrastructure/Telemetry/ActivitySourceNames.cs
namespace STIGForge.Infrastructure.Telemetry;

public static class ActivitySourceNames
{
    public const string Missions = "STIGForge.Missions";

    // Span names for lifecycle phases
    public const string BuildPhase = "build";
    public const string ApplyPhase = "apply";
    public const string VerifyPhase = "verify";
    public const string ProvePhase = "prove";
}

// Infrastructure/Telemetry/MissionTracingService.cs
using System.Diagnostics;

namespace STIGForge.Infrastructure.Telemetry;

public sealed class MissionTracingService
{
    private static readonly ActivitySource ActivitySource = new(ActivitySourceNames.Missions);

    public Activity? StartMissionSpan(string bundleRoot, string runId)
    {
        var activity = ActivitySource.StartActivity("mission", ActivityKind.Server);
        if (activity != null)
        {
            activity.SetTag("bundle.root", bundleRoot);
            activity.SetTag("mission.run_id", runId);
            activity.SetTag("mission.started_at", DateTimeOffset.UtcNow.ToString("o"));
        }
        return activity;
    }

    public Activity? StartPhaseSpan(string phaseName, string bundleRoot)
    {
        var activity = ActivitySource.StartActivity(phaseName, ActivityKind.Internal);
        if (activity != null)
        {
            activity.SetTag("phase.name", phaseName);
            activity.SetTag("bundle.root", bundleRoot);
        }
        return activity;
    }

    public void AddPhaseEvent(Activity? activity, string eventName, string? message = null)
    {
        activity?.AddEvent(new ActivityEvent(eventName, tags: new ActivityTagsCollection
        {
            ["message"] = message ?? string.Empty
        }));
    }

    /// <summary>
    /// Gets the current trace context for propagation to child processes.
    /// Returns null if no active Activity context exists.
    /// </summary>
    public static TraceContext? GetCurrentContext()
    {
        var activity = Activity.Current;
        if (activity == null) return null;

        return new TraceContext
        {
            TraceId = activity.TraceId.ToString(),
            SpanId = activity.SpanId.ToString(),
            TraceFlags = activity.ActivityTraceFlags.ToString()
        };
    }
}

public sealed class TraceContext
{
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string TraceFlags { get; init; } = string.Empty;
}
```

### Pattern 2: Trace Context Propagation to PowerShell
**What:** Inject W3C trace context into PowerShell process environment variables for cross-process correlation.
**When to use:** All PowerShell process launches in ApplyRunner.
**Example:**
```csharp
// Source: W3C TraceContext propagation pattern + PowerShell environment variable pattern
// In ApplyRunner.cs - modify RunScriptAsync, RunDscAsync, RunPowerStigCompileAsync

private static void InjectTraceContext(ProcessStartInfo psi)
{
    var context = MissionTracingService.GetCurrentContext();
    if (context != null)
    {
        // W3C TraceContext propagation via environment variables
        psi.Environment["STIGFORGE_TRACE_ID"] = context.TraceId;
        psi.Environment["STIGFORGE_PARENT_SPAN_ID"] = context.SpanId;
        psi.Environment["STIGFORGE_TRACE_FLAGS"] = context.TraceFlags;

        // Child PowerShell scripts can access via:
        // $traceId = $env:STIGFORGE_TRACE_ID
        // $parentSpanId = $env:STIGFORGE_PARENT_SPAN_ID
    }
}

// Usage in RunScriptAsync:
var psi = new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = arguments,
    // ... other properties
};

// Inject existing context
InjectTraceContext(psi);

// Add standard STIGForge environment variables
psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();
```

**PowerShell script pattern for logging with trace context:**
```powershell
# In RunApply.ps1 or other PowerShell scripts
function Write-TraceLog {
    param([string]$Message, [string]$Level = "Information")

    $traceId = $env:STIGFORGE_TRACE_ID
    $parentSpanId = $env:STIGFORGE_PARENT_SPAN_ID

    $logEntry = @{
        Timestamp = (Get-Date -Format "o")
        Level = $Level
        Message = $Message
        TraceId = $traceId
        ParentSpanId = $parentSpanId
        BundleRoot = $env:STIGFORGE_BUNDLE_ROOT
    }

    # Write to file with trace context for correlation
    $logEntry | ConvertTo-Json -Compress | Add-Content -Path "$env:STIGFORGE_APPLY_LOG_DIR\script-trace.log"
}
```

### Pattern 3: Mission Lifecycle Span Wrapping
**What:** Wrap each mission phase in an Activity span that logs start/end and propagates to child operations.
**When to use:** BundleOrchestrator.OrchestrateAsync - wrap Build, Apply, Verify, Prove phases.
**Example:**
```csharp
// Source: OpenTelemetry semantic conventions + STIGForge mission lifecycle
// In BundleOrchestrator.cs

private readonly MissionTracingService _tracing;

public async Task OrchestrateAsync(OrchestrateRequest request, CancellationToken ct)
{
    // Start root mission span
    using var missionActivity = _tracing.StartMissionSpan(root, runId);

    try
    {
        // --- Apply phase ---
        using var applyActivity = _tracing.StartPhaseSpan(ActivitySourceNames.ApplyPhase, root);
        await AppendEventAsync(runId, ++seq, MissionPhase.Apply, "apply", MissionEventStatus.Started, null, ct);

        try
        {
            applyResult = await _apply.RunAsync(new ApplyRequest
            {
                BundleRoot = root,
                // ... other properties
                TraceId = Activity.Current?.TraceId.ToString(), // Pass to ApplyRunner
                ParentSpanId = Activity.Current?.SpanId.ToString()
            }, ct);

            applyActivity?.SetStatus(ActivityStatusCode.Ok);
            _tracing.AddPhaseEvent(applyActivity, "apply_completed", $"Steps={applyResult.Steps.Count}");
        }
        catch (Exception ex)
        {
            applyActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            applyActivity?.SetTag("error.type", ex.GetType().FullName);
            throw;
        }

        // --- Verify phase ---
        using var verifyActivity = _tracing.StartPhaseSpan(ActivitySourceNames.VerifyPhase, root);
        // ... verification logic with span wrapping

        // --- Evidence/Prove phase ---
        using var proveActivity = _tracing.StartPhaseSpan(ActivitySourceNames.ProvePhase, root);
        // ... evidence aggregation with span wrapping
    }
    catch (Exception ex)
    {
        missionActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

### Pattern 4: Debug Export Bundle Creation
**What:** Aggregate all diagnostic artifacts (logs, traces, bundle manifests) into a portable ZIP file for offline support.
**When to use:** On-demand export via CLI command or WPF button; post-mission for archival.
**Example:**
```csharp
// Source: System.IO.Compression documentation + STIGForge diagnostic artifact patterns
// Infrastructure/Telemetry/DebugBundleExporter.cs

using System.Diagnostics;
using System.IO.Compression;

namespace STIGForge.Infrastructure.Telemetry;

public sealed class DebugBundleExporter
{
    private readonly IPathBuilder _paths;

    public DebugBundleExporter(IPathBuilder paths)
    {
        _paths = paths;
    }

    public DebugBundleResult ExportBundle(DebugBundleRequest request)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var bundleName = $"stigforge-debug-{timestamp}.zip";
        var outputPath = Path.Combine(_paths.GetLogsRoot(), "exports", bundleName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        // 1. Add application logs (last N days or all)
        AddLogsToArchive(archive, _paths.GetLogsRoot(), request.IncludeDaysOfLogs);

        // 2. Add bundle-specific logs if bundle root provided
        if (!string.IsNullOrWhiteSpace(request.BundleRoot))
        {
            AddBundleLogsToArchive(archive, request.BundleRoot);
        }

        // 3. Add trace spans (if we implement trace file output)
        AddTracesToArchive(archive, _paths.GetLogsRoot());

        // 4. Add system info
        AddSystemInfoToArchive(archive);

        // 5. Add manifest summary
        AddManifestToArchive(archive, request);

        return new DebugBundleResult
        {
            OutputPath = outputPath,
            FileCount = archive.Entries.Count,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void AddLogsToArchive(ZipArchive archive, string logsRoot, int days)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        foreach (var file in Directory.GetFiles(logsRoot, "*.log", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            if (info.LastWriteTimeUtc >= cutoff.DateTime)
            {
                var entryName = $"logs/{Path.GetRelativePath(logsRoot, file)}";
                archive.CreateEntryFromFile(file, entryName);
            }
        }
    }

    private void AddBundleLogsToArchive(ZipArchive archive, string bundleRoot)
    {
        // Add Apply/Logs
        var applyLogs = Path.Combine(bundleRoot, "Apply", "Logs");
        if (Directory.Exists(applyLogs))
        {
            foreach (var file in Directory.GetFiles(applyLogs, "*", SearchOption.AllDirectories))
            {
                var entryName = $"bundle/Apply/Logs/{Path.GetFileName(file)}";
                archive.CreateEntryFromFile(file, entryName);
            }
        }

        // Add Verify output
        var verifyRoot = Path.Combine(bundleRoot, "Verify");
        if (Directory.Exists(verifyRoot))
        {
            foreach (var file in Directory.GetFiles(verifyRoot, "*.json", SearchOption.AllDirectories))
            {
                var entryName = $"bundle/Verify/{Path.GetRelativePath(verifyRoot, file)}";
                archive.CreateEntryFromFile(file, entryName);
            }
        }

        // Add mission run record if available
        var applyRun = Path.Combine(bundleRoot, "Apply", "apply_run.json");
        if (File.Exists(applyRun))
        {
            archive.CreateEntryFromFile(applyRun, "bundle/Apply/apply_run.json");
        }
    }

    private void AddTracesToArchive(ZipArchive archive, string logsRoot)
    {
        var tracesPath = Path.Combine(logsRoot, "traces.json");
        if (File.Exists(tracesPath))
        {
            archive.CreateEntryFromFile(tracesPath, "traces/traces.json");
        }
    }

    private void AddSystemInfoToArchive(ZipArchive archive)
    {
        var entry = archive.CreateEntry("system-info.json");
        using var writer = new StreamWriter(entry.Open());
        using var json = System.Text.Json.JsonSerializer.Create();

        var info = new
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            OSVersion = Environment.OSVersion.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Timestamp = DateTimeOffset.UtcNow.ToString("o"),
            ProcessId = Environment.ProcessId
        };

        writer.Write(System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private void AddManifestToArchive(ZipArchive archive, DebugBundleRequest request)
    {
        var entry = archive.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());

        var manifest = new
        {
            ExportedAt = DateTimeOffset.UtcNow.ToString("o"),
            BundleRoot = request.BundleRoot,
            IncludeDaysOfLogs = request.IncludeDaysOfLogs,
            Reason = request.ExportReason,
            STIGForgeVersion = typeof(DebugBundleExporter).Assembly.GetName().Version?.ToString()
        };

        writer.Write(System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}

public sealed class DebugBundleRequest
{
    public string? BundleRoot { get; set; }
    public int IncludeDaysOfLogs { get; set; } = 7;
    public string? ExportReason { get; set; }
}

public sealed class DebugBundleResult
{
    public string OutputPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

### Pattern 5: Activity Listener for Trace File Output
**What:** Configure an ActivityListener to write trace spans to a local JSON file for offline analysis.
**When to use:** Application startup to capture all activities for the mission.
**Example:**
```csharp
// Source: System.Diagnostics.ActivityListener documentation
// In TelemetryService or host configuration

public static void ConfigureLocalTraceOutput(string logsRoot)
{
    var tracesPath = Path.Combine(logsRoot, "traces.json");

    ActivitySource.AddActivityListener(new ActivityListener
    {
        ShouldListenTo = source => source.Name.StartsWith("STIGForge"),
        SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData,
        Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,

        ActivityStopped = activity =>
        {
            var span = new
            {
                traceId = activity.TraceId.ToString(),
                spanId = activity.SpanId.ToString(),
                parentSpanId = activity.ParentSpanId.ToString(),
                operationName = activity.OperationName,
                kind = activity.Kind.ToString(),
                startTime = activity.StartTimeUtc.ToString("o"),
                duration = activity.Duration.TotalMilliseconds,
                status = activity.Status.ToString(),
                statusDescription = activity.StatusDescription,
                tags = activity.Tags?.ToDictionary(t => t.Key, t => t.Value),
                events = activity.Events.Select(e => new
                {
                    name = e.Name,
                    timestamp = e.Timestamp.ToString("o"),
                    tags = e.Tags?.ToDictionary(t => t.Key, t => t.Value)
                }).ToList()
            };

            var line = System.Text.Json.JsonSerializer.Serialize(span);
            lock (tracesPath)
            {
                File.AppendAllText(tracesPath, line + Environment.NewLine);
            }
        }
    });
}
```

### Anti-Patterns to Avoid
- **Starting Activity without disposing:** Activities must be disposed to end the span; use `using` pattern.
- **Env var propagation without fallback:** PowerShell scripts may run outside STIGForge context; handle missing env vars gracefully.
- **Synchronous trace file writes in hot path:** Use buffered writes or background queue for trace output.
- **Hardcoded paths in debug bundles:** Use IPathBuilder and relative paths for portability.
- **Including sensitive data in traces:** Never log credentials, keys, or PII in trace spans.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Trace span creation | Custom span tracking | `System.Diagnostics.ActivitySource` | W3C standard, OTel compatible, built-in |
| Correlation IDs | Custom ID generation | `Activity.Current.TraceId` | Consistent with W3C trace context |
| Process boundary propagation | Custom IPC protocol | Environment variables | Simple, universal, PowerShell-compatible |
| Archive creation | Custom tar/zip code | `System.IO.Compression.ZipFile` | Native .NET, cross-platform |
| Activity lifecycle | Manual start/stop tracking | `using` pattern with `ActivitySource.StartActivity` | Guaranteed disposal |

**Key insight:** The .NET 8 built-in Activity API provides everything needed for mission tracing. No OpenTelemetry SDK packages are required for the core functionality.

## Common Pitfalls

### Pitfall 1: Lost Trace Context Across Process Boundaries
**What goes wrong:** Trace ID is not available in PowerShell child processes, breaking correlation.
**Why it happens:** PowerShell 5.1 does not natively support W3C trace context; the parent process must explicitly pass context.
**How to avoid:** Inject STIGFORGE_TRACE_ID and STIGFORGE_PARENT_SPAN_ID environment variables at ProcessStartInfo creation. Document that PowerShell scripts should read these for their own logging.
**Warning signs:** Logs from PowerShell scripts have no TraceId; cannot correlate script output with parent mission span.

### Pitfall 2: Activity Not Disposed (Span Never Ends)
**What goes wrong:** Duration shows as infinite or spans never appear in trace output.
**Why it happens:** Activity.Dispose() was never called; the span is left "open".
**How to avoid:** Always use `using var activity = ActivitySource.StartActivity(...)` pattern. Never store Activity in a field without explicit lifecycle management.
**Warning signs:** Trace files show spans with missing end times; duration is zero or negative.

### Pitfall 3: Debug Bundle Missing Key Artifacts
**What goes wrong:** Support cannot diagnose issue because critical logs or manifests are missing from export bundle.
**Why it happens:** Bundle export logic doesn't include all relevant paths or applies incorrect filters.
**How to avoid:** Include a manifest.json in every bundle listing what was included/excluded. Create a checklist of required artifacts: application logs, bundle logs, verify output, apply_run.json, system info.
**Warning signs:** Support requests missing context; operators cannot find expected files in bundle.

### Pitfall 4: Serilog CorrelationId Not Matching Activity TraceId
**What goes wrong:** Serilog logs show one CorrelationId while traces show a different TraceId.
**Why it happens:** Activity was started after the log was written, or Activity.Current was null when the enricher ran.
**How to avoid:** Ensure Activity is started before any logging within the span. The CorrelationIdEnricher already handles this correctly by reading Activity.Current.
**Warning signs:** Log correlation analysis shows mismatches between CorrelationId and TraceId.

## Code Examples

Verified patterns from official sources:

### ActivitySource with Disposable Pattern
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing
using System.Diagnostics;

public class MissionRunner
{
    private static readonly ActivitySource ActivitySource = new("STIGForge.Missions");

    public async Task RunMissionAsync(string bundleRoot, CancellationToken ct)
    {
        // Start activity with automatic disposal
        using var activity = ActivitySource.StartActivity("mission", ActivityKind.Server);
        if (activity != null)
        {
            activity.SetTag("bundle.root", bundleRoot);
            activity.SetTag("mission.start_time", DateTimeOffset.UtcNow.ToString("o"));
        }

        try
        {
            await ExecuteMissionPhases(bundleRoot, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            throw;
        }
        // Activity automatically disposed here, ending the span
    }
}
```

### Environment Variable Propagation
```csharp
// Source: W3C TraceContext propagation + ProcessStartInfo environment pattern
public ProcessStartInfo CreatePowerShellProcess(string script, string? traceId, string? parentSpanId)
{
    var psi = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    // Propagate trace context
    if (!string.IsNullOrEmpty(traceId))
        psi.Environment["STIGFORGE_TRACE_ID"] = traceId;

    if (!string.IsNullOrEmpty(parentSpanId))
        psi.Environment["STIGFORGE_PARENT_SPAN_ID"] = parentSpanId;

    return psi;
}
```

### ActivityListener for File Output
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitylistener
public void ConfigureTraceListener(string outputPath)
{
    ActivitySource.AddActivityListener(new ActivityListener
    {
        ShouldListenTo = source => source.Name == "STIGForge.Missions",
        SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        ActivityStopped = activity =>
        {
            var record = new
            {
                traceId = activity.TraceId.ToString(),
                spanId = activity.SpanId.ToString(),
                operationName = activity.OperationName,
                startTimeUtc = activity.StartTimeUtc,
                durationMs = activity.Duration.TotalMilliseconds,
                status = activity.Status.ToString()
            };

            File.AppendAllText(outputPath,
                JsonSerializer.Serialize(record) + Environment.NewLine);
        }
    });
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Custom correlation ID services | Activity.Current (W3C standard) | .NET 5+ | Standard trace context, OTel compatibility |
| HTTP header-only propagation | Environment variable propagation | PowerShell scenarios | Cross-process correlation without HTTP |
| Local-only logging | Correlated logs + traces | OpenTelemetry era | End-to-end visibility |
| Manual diagnostic collection | Automated debug bundles | 2025+ observability patterns | Faster support resolution |

**Deprecated/outdated:**
- Correlation IDs not based on W3C TraceContext: Use Activity.TraceId for standard format
- Custom span tracking without ActivitySource: Activity is the .NET standard for tracing
- Separate log and trace correlation: Single TraceId should correlate both

## Open Questions

1. **Should traces be exported in OTLP format for future collector integration?**
   - What we know: JSON files work for offline scenarios; OTLP is standard for cloud observability
   - What's unclear: Whether v1.2+ will require collector-based export
   - Recommendation: Start with JSON for v1.1; design ActivityListener to support OTLP export later

2. **Should debug bundle creation be automatic after each mission?**
   - What we know: OBSV-05 requires on-demand bundles; automatic could increase storage
   - What's unclear: Storage impact for high-frequency missions
   - Recommendation: Implement on-demand first; add automatic option with configurable retention

3. **How to handle PowerShell scripts that start their own activities?**
   - What we know: PowerShell scripts receive trace context via env vars
   - What's unclear: Whether scripts should create child spans vs just logging with parent context
   - Recommendation: Document env var pattern; child span creation is optional and advanced

## Sources

### Primary (HIGH confidence)
- Microsoft Learn: System.Diagnostics.Activity (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity) - Activity API reference
- Microsoft Learn: Distributed Tracing in .NET (https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing) - W3C trace context
- OpenTelemetry .NET Documentation (https://opentelemetry.io/docs/instrumentation/net/) - ActivitySource patterns
- Repository code: `src/STIGForge.Infrastructure/Logging/CorrelationIdEnricher.cs` - Existing Activity.Current integration
- Repository code: `src/STIGForge.Apply/ApplyRunner.cs` - PowerShell process creation with env vars

### Secondary (MEDIUM confidence)
- Repository code: `src/STIGForge.Build/BundleOrchestrator.cs` - Mission lifecycle phases
- Repository code: `src/STIGForge.App/App.xaml.cs` - Host configuration patterns
- System.IO.Compression documentation (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zipfile) - Archive creation

### Tertiary (LOW confidence)
- PowerShell logging best practices 2025 - General patterns for script logging with trace context

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All core functionality uses built-in .NET 8 APIs; no new packages required
- Architecture: HIGH - Patterns follow official Microsoft and OpenTelemetry documentation
- Pitfalls: MEDIUM - PowerShell boundary propagation is well-understood but requires testing

**Research date:** 2026-02-22
**Valid until:** 2026-03-24 (30 days; stable .NET 8 Activity API)

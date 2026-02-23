# Architecture Research: v1.1 Operational Maturity Integration

**Domain:** .NET 8 WPF/SQLite Desktop Application with Operational Maturity Enhancements
**Researched:** 2026-02-22
**Overall Confidence:** HIGH

## Executive Summary

STIGForge Next's existing architecture is well-suited for operational maturity enhancements. The layered architecture with dependency injection, existing Serilog logging, and xUnit testing provides strong integration points for observability, coverage, and error handling improvements. **Key finding: Most v1.1 capabilities require NEW components with MINIMAL modifications to existing services**, preserving the existing architectural contracts while extending observability and reliability.

**Critical integration insight:** The existing service registration pattern in both WPF (`App.xaml.cs`) and CLI (`CliHostFactory.cs`) enables clean addition of new cross-cutting services (telemetry, error catalog, performance instrumentation) without disrupting existing components.

## Existing Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Presentation Layer (WPF/CLI)                     │
├─────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐│
│  │ MainView    │  │ MainWindow  │  │ CLI         │  │ ViewModels  ││
│  │ Model       │  │ (XAML)      │  │ Commands    │  │ (MVVM)       ││
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘│
│         │                │                │                │        │
└─────────┼────────────────┼────────────────┼────────────────┼───────┘
          │                │                │                │
┌─────────┴────────────────┴────────────────┴────────────────┴───────┐
│                    Orchestration Layer                             │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │  MainViewModel (UI coordination) + Orchestrators            │  │
│  └─────────────────────────────────────────────────────────────┘  │
└────────────────┬───────────────────────────────────────────────────┘
                 │
┌────────────────┴───────────────────────────────────────────────────┐
│                      Service Layer                                 │
├───────────────────────────────────────────────────────────────────┤
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌──────────┐   │
│  │ Build   │ │ Apply   │ │ Verify  │ │ Export  │ │ Content  │   │
│  │ Service │ │ Runner  │ │Service  │ │Exporter │ │ Importer │   │
│  └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘ └────┬─────┘   │
└───────┼──────────┼──────────┼──────────┼───────────┼────────────┘
        │          │          │          │           │
┌───────┴──────────┴──────────┴──────────┴───────────┴─────────────┐
│                    Domain Layer (Core)                            │
│  Models, Contracts, Service Interfaces, Abstractions               │
└────────────────────────────────────────────────────────────────────┘
        │
┌───────┴───────────────────────────────────────────────────────────┐
│                  Infrastructure Layer                              │
├───────────────────────────────────────────────────────────────────┤
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌──────────┐   │
│  │ SQLite  │ │ Serilog │ │ Process │ │ Hashing │ │ Path      │   │
│  │ Repos   │ │ Logging │ │ Runner  │ │ Service │ │ Builder   │   │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └──────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

## v1.1 Integration Architecture

### NEW Components (Additions Only)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    v1.1 NEW Components (Top Layer)                  │
├─────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │ OpenTelemetry│  │ Error        │  │ Performance  │             │
│  │ Service      │  │ Catalog      │  │ Instrumenter │             │
│  │ (NEW)        │  │ Service      │  │ Service      │             │
│  │              │  │ (NEW)        │  │ (NEW)        │             │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘             │
│         │                  │                  │                     │
└─────────┼──────────────────┼──────────────────┼─────────────────────┘
          │                  │                  │
          ▼                  ▼                  ▼
┌─────────┴──────────────────┴──────────────────┴─────────────────────┐
│                 MODIFIED Integration Points                          │
│  (Minor changes to existing files for DI registration)              │
└─────────────────────────────────────────────────────────────────────┘
```

## Integration Points Analysis

### 1. OpenTelemetry Integration

**Target:** Add structured logging, metrics, and tracing to existing Serilog setup.

**NEW Components:**
- `src/STIGForge.Infrastructure/Telemetry/TelemetryService.cs` - OpenTelemetry configuration and management
- `src/STIGForge.Infrastructure/Telemetry/ActivitySourceFactory.cs` - ActivitySource creation for mission tracing
- `src/STIGForge.Core/Abstractions/ITelemetryService.cs` - Telemetry abstraction interface

**MODIFIED Components:**
- `src/STIGForge.App/App.xaml.cs` - Add OpenTelemetry service registration (3-5 lines)
- `src/STIGForge.Cli/CliHostFactory.cs` - Add OpenTelemetry service registration (3-5 lines)
- `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj` - Add package references

**NuGet Packages Required:**
```xml
<PackageReference Include="OpenTelemetry" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
<PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.1" />
```

**Integration Pattern:**
```csharp
// In App.xaml.cs ConfigureServices
services.AddSingleton<ITelemetryService, TelemetryService>();
services.AddSingleton<ActivitySource>(sp =>
    sp.GetRequiredService<ITelemetryService>().CreateActivitySource("STIGForge"));

// Existing Serilog remains unchanged - OpenTelemetry reads from Serilog
```

**Why This Approach:**
- **Zero breaking changes:** Existing Serilog configuration remains functional
- **Layered addition:** OpenTelemetry wraps Serilog, doesn't replace it
- **Graceful degradation:** If telemetry backend is unavailable, app continues normally
- **Offline-first:** OTLP exporter can buffer locally when offline

### 2. Test Coverage Enhancement

**Target:** Achieve 80% line coverage on critical assemblies with Coverlet.

**NEW Components:**
- `tests/STIGForge.UnitTests/Coverage/` - New test coverage gap fills
- `tests/STIGForge.Coverage/STIGForge.Coverage.csproj` - Dedicated coverage verification project
- `.config/coverlet.runsettings` - Coverage configuration with 80% threshold

**MODIFIED Components:**
- `tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj` - Add coverlet configuration properties
- `.github/workflows/ci.yml` - Add coverage reporting step
- `Directory.Build.props` - Add coverage collection properties

**Coverage Configuration:**
```xml
<!-- Add to STIGForge.UnitTests.csproj -->
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura</CoverletOutputFormat>
  <Threshold>80</Threshold>
  <ThresholdType>line</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="coverlet.collector" Version="6.0.4" />
  <PackageReference Include="ReportGenerator" Version="5.4.1" />
</ItemGroup>
```

**Build Order Dependency:**
1. **First:** Coverlet configuration in `.csproj` files (enables collection)
2. **Second:** Gap-fill tests for uncovered critical paths
3. **Third:** CI workflow integration for enforcement
4. **Last:** HTML report generation (optional)

**Critical Assemblies for 80% Coverage:**
- `STIGForge.Build` - Bundle compilation logic
- `STIGForge.Apply` - DSC execution and reboot coordination
- `STIGForge.Verify` - Verification workflow orchestration
- `STIGForge.Infrastructure` - Storage and system integration

### 3. Performance Instrumentation

**Target:** Add mission pipeline timing and performance metrics.

**NEW Components:**
- `src/STIGForge.Infrastructure/Performance/PerformanceInstrumenter.cs` - Metric collection
- `src/STIGForge.Infrastructure/Performance/MissionMetrics.cs` - Mission-specific metrics model
- `src/STIGForge.Core/Abstractions/IPerformanceInstrumenter.cs` - Performance instrumentation interface
- `tests/STIGForge.PerformanceTests/` - BenchmarkDotNet performance benchmark project

**MODIFIED Components:**
- Service layer classes (`ApplyRunner`, `BundleBuilder`, etc.) - Add instrumentation calls
- `src/STIGForge.App/App.xaml.cs` - Register performance instrumenter

**Instrumentation Pattern:**
```csharp
// In service methods
using var activity = _activitySource.StartActivity("ApplyBundle");
activity?.SetTag("bundle.id", bundleId);
activity?.SetTag("control.count", controlCount);

var sw = ValueStopwatch.StartNew();
// ... existing logic ...
var elapsed = sw.GetElapsedTime().TotalMilliseconds;

_performanceInstrumenter.RecordMissionPhase("Apply", elapsed);
```

**BenchmarkDotNet Integration:**
```xml
<!-- New test project -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>
</Project>
```

**Performance Scenarios to Benchmark:**
- Bundle compilation with 1K, 5K, 10K rules
- SQLite query performance for large control sets
- WPF UI binding with large observable collections
- Startup time (cold vs warm)

### 4. Error Catalog and Recovery Flows

**Target:** Centralized error definitions with recovery guidance.

**NEW Components:**
- `src/STIGForge.Core/Errors/` - Error catalog definitions
  - `StigForgeException.cs` - Base exception with error code
  - `ErrorCatalog.cs` - Error code registry with recovery guidance
  - `RecoveryActions.cs` - Recovery action definitions
- `src/STIGForge.Infrastructure/Errors/ErrorRecoveryService.cs` - Recovery orchestration
- `src/STIGForge.Core/Abstractions/IErrorRecoveryService.cs` - Recovery interface

**MODIFIED Components:**
- Existing exception classes - Inherit from `StigForgeException` instead of `Exception`
- Service layer error handling - Use error catalog codes
- `MainViewModel.cs` - Add recovery flow UI integration

**Error Catalog Pattern:**
```csharp
// New base exception
public abstract class StigForgeException : Exception
{
    public string ErrorCode { get; }
    public RecoveryAction Recovery { get; }
    public string RecoveryGuidance { get; }

    protected StigForgeException(
        string errorCode,
        RecoveryAction recovery,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        Recovery = recovery;
        RecoveryGuidance = ErrorCatalog.GetGuidance(errorCode);
    }
}

// Error catalog
public static class ErrorCatalog
{
    public static readonly ErrorDefinition BundleNotFound = new(
        "BUNDLE_001",
        "Bundle directory not found",
        RecoveryAction.RetryWithCorrectPath,
        "Verify the bundle path matches the bundle ID from the build phase");

    // ... 50+ error definitions
}
```

**Recovery Flow Integration:**
```csharp
// In MainViewModel error handlers
catch (StigForgeException sfe)
{
    _logger.LogError(sfe, "Mission error: {ErrorCode}", sfe.ErrorCode);

    var recovery = await _errorRecoveryService.GetRecoveryOptionsAsync(sfe.ErrorCode);
    if (recovery.CanAutoRecover)
    {
        StatusText = $"Attempting recovery: {recovery.Action}";
        await _errorRecoveryService.AttemptRecoveryAsync(sfe);
    }
    else
    {
        // Show recovery dialog
        await ShowRecoveryDialogAsync(sfe, recovery);
    }
}
```

## Build Order and Dependencies

```
┌─────────────────────────────────────────────────────────────┐
│ Phase 1: Foundation (No Dependencies)                        │
├─────────────────────────────────────────────────────────────┤
│ 1. Add OpenTelemetry packages to Infrastructure.csproj      │
│ 2. Create ITelemetryService interface in Core               │
│ 3. Create TelemetryService in Infrastructure/Telemetry      │
│ 4. Add BenchmarkDotNet performance project                  │
│ 5. Create error catalog base classes in Core/Errors         │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase 2: Service Registration (Requires Phase 1)            │
├─────────────────────────────────────────────────────────────┤
│ 1. Modify App.xaml.cs to register telemetry service         │
│ 2. Modify CliHostFactory.cs to register telemetry service   │
│ 3. Register error recovery services                         │
│ 4. Register performance instrumenter                        │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase 3: Instrumentation (Requires Phase 2)                 │
├─────────────────────────────────────────────────────────────┤
│ 1. Add ActivitySource usage to service layer                │
│ 2. Add performance metrics to mission pipeline              │
│ 3. Replace existing exceptions with catalog exceptions      │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase 4: Test Coverage (Independent, can run parallel)      │
├─────────────────────────────────────────────────────────────┤
│ 1. Configure Coverlet in test projects                      │
│ 2. Add gap-fill tests for uncovered code                    │
│ 3. Add coverage thresholds to Directory.Build.props         │
│ 4. Update CI workflow for coverage enforcement              │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase 5: UI Integration (Requires all phases)               │
├─────────────────────────────────────────────────────────────┤
│ 1. Add error recovery dialogs to WPF                        │
│ 2. Add performance visualization to dashboard               │
│ 3. Add telemetry export command to CLI                      │
└─────────────────────────────────────────────────────────────┘
```

## Component Modification Matrix

| Component | Type | Change Scope | Risk Level |
|-----------|------|--------------|------------|
| `App.xaml.cs` | MODIFY | Add 5-10 DI registrations | LOW |
| `CliHostFactory.cs` | MODIFY | Add 5-10 DI registrations | LOW |
| `STIGForge.Infrastructure.csproj` | MODIFY | Add package references | LOW |
| `STIGForge.UnitTests.csproj` | MODIFY | Add coverlet config | LOW |
| Service layer classes | MODIFY | Add instrumentation calls | LOW |
| Existing exceptions | MODIFY | Inherit from new base | MEDIUM |
| `Core/Abstractions/` | ADD | New interfaces | NONE |
| `Infrastructure/Telemetry/` | ADD | New telemetry service | NONE |
| `Infrastructure/Performance/` | ADD | New instrumenter | NONE |
| `Core/Errors/` | ADD | New error catalog | NONE |
| `Infrastructure/Errors/` | ADD | New recovery service | NONE |
| `tests/STIGForge.PerformanceTests/` | ADD | New benchmark project | NONE |
| `MainViewModel.cs` | MODIFY | Add recovery flow UI | LOW |

## Architectural Patterns to Follow

### Pattern 1: Decorator Pattern for Telemetry

**What:** Wrap existing service calls with telemetry collection without modifying core logic.

**When to use:** Adding observability to existing services without breaking changes.

**Example:**
```csharp
// Existing service remains unchanged
public class ApplyRunner : IApplyRunner
{
    private readonly ITelemetryService _telemetry;
    private readonly ActivitySource _activitySource;

    public async Task<ApplyResult> ApplyAsync(BundleManifest manifest, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("ApplyBundle");
        activity?.SetTag("bundle.id", manifest.BundleId);

        var result = await ApplyInternalAsync(manifest, ct);

        activity?.SetTag("result.status", result.Status);
        _telemetry.RecordMetric("apply.duration", result.DurationMs);

        return result;
    }

    // Existing logic unchanged in ApplyInternalAsync
}
```

### Pattern 2: Error Catalog Strategy Pattern

**What:** Centralized error definitions with recovery strategy selection.

**When to use:** Standardizing error handling across WPF and CLI surfaces.

**Example:**
```csharp
public interface IErrorRecoveryService
{
    Task<RecoveryResult> AttemptRecoveryAsync(StigForgeException exception);
    Task<bool> CanRecoverAsync(string errorCode);
    string GetRecoveryGuidance(string errorCode);
}

// Recovery strategies
public interface IRecoveryStrategy
{
    Task<RecoveryResult> AttemptRecoveryAsync(StigForgeException exception);
}

public class RetryRecoveryStrategy : IRecoveryStrategy
{
    public async Task<RecoveryResult> AttemptRecoveryAsync(StigForgeException exception)
    {
        // Retry logic specific to error type
    }
}
```

### Pattern 3: Provider Pattern for Performance Counters

**What:** Abstract performance counter collection for multiple backends (OpenTelemetry, local file, memory).

**When to use:** Supporting multiple telemetry destinations without hard-coding.

**Example:**
```csharp
public interface IPerformanceInstrumenter
{
    void RecordMissionPhase(string phase, double durationMs);
    void RecordCounter(string name, int value);
    void RecordGauge(string name, double value);
}

// Multiple implementations
public class OpenTelemetryInstrumenter : IPerformanceInstrumenter { }
public class LocalFileInstrumenter : IPerformanceInstrumenter { }
public class CompositeInstrumenter : IPerformanceInstrumenter
{
    private readonly IPerformanceInstrumenter[] _instrumenters;
    // Writes to all registered instrumenters
}
```

## Anti-Patterns to Avoid

### Anti-Pattern 1: Direct Serilog Replacement

**What people do:** Remove Serilog and replace with OpenTelemetry logging directly.

**Why it's wrong:** Breaks existing file-based logging that works offline. OpenTelemetry requires a collector backend.

**Do this instead:** Keep Serilog for file logs, add OpenTelemetry as an additional sink. Let OpenTelemetry read Serilog events via `ReadFrom.Source()`.

### Anti-Pattern 2: Coverage-Driven Test Writing

**What people do:** Write tests solely to hit coverage numbers without testing meaningful behavior.

**Why it's wrong:** Creates brittle tests that break refactoring and provide false confidence.

**Do this instead:** Focus on **critical path coverage** - test business logic, error conditions, and integration points. Accept 80% on core assemblies, don't chase 100%.

### Anti-Pattern 3: Blocking Telemetry Initialization

**What people do:** Make telemetry backend connection mandatory for app startup.

**Why it's wrong:** Offline-first app becomes unusable when network/telemetry is unavailable.

**Do this instead:** Use `OtlpExporter` with retry policy and local buffering. Fail gracefully if telemetry backend is unreachable.

### Anti-Pattern 4: Exception Type Proliferation

**What people do:** Create unique exception classes for every error scenario.

**Why it's wrong:** Explosion of exception types makes error handling complex and recovery logic scattered.

**Do this instead:** Use error codes with a single `StigForgeException` base. Centralize recovery guidance in the error catalog, not in exception classes.

## Scaling Considerations

| Scale | Performance Implications | Architecture Adjustments |
|-------|--------------------------|--------------------------|
| 1K rules | Minimal - SQLite handles easily | No changes needed |
| 10K rules | Bundle compilation ~2-5s | Add async compilation with progress reporting |
| 50K+ rules | SQLite queries slow, memory pressure | Consider SQLite WAL mode, pagination in UI, indexed queries |
| 1M+ audit entries | Audit trail queries slow | Add audit trail partitioning by date, archive old entries |

### Scaling Priorities for v1.1

1. **First bottleneck:** SQLite query performance with large control sets
   - **Fix:** Add appropriate indexes on `controls` table (already in schema)
   - **Metrics:** Monitor query duration with performance instrumenter

2. **Second bottleneck:** WPF UI binding with large observable collections
   - **Fix:** Virtualization in ListBox/DataGrid controls
   - **Metrics:** Monitor UI thread time with dispatcher instrumentation

3. **Third bottleneck:** Log file rotation
   - **Fix:** Configure Serilog rolling interval and size limit
   - **Metrics:** Monitor log file size and write rate

## Observability Data Flow

```
┌───────────────────────────────────────────────────────────────┐
│                    Application Layer                          │
│  Service methods emit ActivitySource events and metrics       │
└────────────────────────┬──────────────────────────────────────┘
                         │
                         ▼
┌───────────────────────────────────────────────────────────────┐
│                  OpenTelemetry SDK                            │
│  Batches events, applies sampling, manages Activity lifecycle │
└────────────────────────┬──────────────────────────────────────┘
                         │
         ┌───────────────┴───────────────┐
         ▼                               ▼
┌──────────────────────┐        ┌──────────────────────┐
│   OTLP Exporter      │        │   Serilog Sink       │
│   (Network/Collector)│        │   (Local File)       │
│   - Optional         │        │   - Required         │
│   - Batches on send  │        │   - Offline-first    │
└──────────────────────┘        └──────────────────────┘
         │                               │
         ▼                               ▼
┌──────────────────────┐        ┌──────────────────────┐
│   Backend Collector  │        │   Log Files          │
│   (Jaeger/Tempo/etc) │        │   stigforge.log      │
│   - Optional         │        │   startup-trace.log  │
└──────────────────────┘        └──────────────────────┘
```

## Error Handling Flow

```
┌───────────────────────────────────────────────────────────────┐
│                    Service Layer                              │
│  Exception occurs → Wrap in StigForgeException                │
└────────────────────────┬──────────────────────────────────────┘
                         │
                         ▼
┌───────────────────────────────────────────────────────────────┐
│                  Orchestration Layer                           │
│  MainViewModel catch → IErrorRecoveryService.GetRecoveryOptions│
└────────────────────────┬──────────────────────────────────────┘
                         │
         ┌───────────────┴───────────────┐
         ▼                               ▼
┌──────────────────────┐        ┌──────────────────────┐
│   Auto-Recovery      │        │   Manual Recovery    │
│   - Retry with       │        │   - Show dialog      │
│     different params │        │   - Display guidance │
│   - Clear cache      │        │   - Log action       │
└──────────────────────┘        └──────────────────────┘
         │                               │
         └───────────────┬───────────────┘
                         ▼
┌───────────────────────────────────────────────────────────────┐
│                  Audit Trail                                  │
│  All errors and recovery attempts logged to audit_trail       │
└───────────────────────────────────────────────────────────────┘
```

## Sources

### Official Documentation
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/) - HIGH confidence, official source
- [Serilog Documentation](https://serilog.net/) - HIGH confidence, official source
- [Coverlet Documentation](https://github.com/coverlet-coverage/coverlet) - HIGH confidence, official repository
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/) - HIGH confidence, official source
- [.NET 8 ActivitySource API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitysource) - HIGH confidence, Microsoft Learn

### Best Practices (2025)
- [.NET 8 OpenTelemetry + Serilog Integration](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-dotnet) - HIGH confidence
- [xUnit + Coverlet Coverage Configuration](https://coverlet-coverage.github.io/coverlet/) - HIGH confidence
- [.NET 8 Performance Improvements](https://learn.microsoft.com/en-us/dotnet/core/performance/performance-tips) - HIGH confidence

### STIGForge Codebase Analysis
- `/src/STIGForge.App/App.xaml.cs` - Service registration pattern (read directly)
- `/src/STIGForge.Core/Abstractions/Services.cs` - Existing service interfaces (read directly)
- `/src/STIGForge.Content/Models/ParsingException.cs` - Existing exception pattern (read directly)
- `/tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj` - Test configuration (read directly)
- `/.github/workflows/ci.yml` - Existing CI pattern (read directly)

---

**Architecture research complete: 2026-02-22**
**Integration feasibility:** CONFIRMED - All v1.1 capabilities can integrate with minimal disruption to existing architecture
**Recommended approach:** Parallel development of new components, phased integration via DI registration

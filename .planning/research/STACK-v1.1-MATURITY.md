# Stack Research: v1.1 Operational Maturity

**Domain:** Windows Desktop Application - Operational Hardening
**Researched:** 2026-02-22
**Confidence:** HIGH

## Summary

This stack research covers **NEW capabilities only** for v1.1 operational maturity. STIGForge Next v1.0 already has .NET 8, WPF, PowerShell 5.1 interop, SQLite persistence, Serilog logging, and xUnit testing. These additions provide 80% test coverage, observability, performance optimization, and error ergonomics without redundancy.

## Recommended Stack Additions

### Testing & Coverage

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **Coverlet MSBuild** | 6.0.4 | Code coverage collection | Already in use via `coverlet.collector` - MSBuild version gives deterministic coverage files and CI control. Cross-platform, actively maintained, .NET 8 compatible. |
| **ReportGenerator** | 5.4+ (global tool) | Coverage report visualization | Converts Cobertura XML to human-readable HTML reports with historical trends. Standard in .NET ecosystem. |
| **Stryker.NET** | 4.0+ (global tool) | Mutation testing for test quality | Goes beyond coverage to verify tests actually catch bugs. Supports .NET 8, C# 13, parallel execution, CI quality gates. |
| **xunit.runner.visualstudio** | 3.1.5 | Test execution (already in use) | Already installed - no change needed. |

**Key Insight:** Coverlet is already being used via `coverlet.collector` v6.0.4. The MSBuild package provides the same functionality but with more control for CI automation. ReportGenerator is the de facto standard for coverage visualization.

### Observability

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **Serilog.Enrichers.FromLogContext** | (included in Serilog 4.x) | Structured log context propagation | Already available in Serilog 4.3.0. Enables `LogContext.PushProperty()` for request/mission-scoped properties. |
| **Serilog.Expressions** | 5.0+ | Advanced log filtering and routing | Enables complex filter expressions for debug exports, log levels by component, and structured querying. |
| **OpenTelemetry** | 1.12.0+ | Metrics & distributed tracing | Industry-standard for observability. Integrates with .NET 8's built-in `System.Diagnostics.Activity` and `System.Diagnostics.Metrics`. |
| **OpenTelemetry.Extensions.Hosting** | 1.12.0+ | OpenTelemetry DI integration | Simplifies OpenTelemetry setup with `Microsoft.Extensions.Hosting` (already in CLI stack). |
| **System.Diagnostics.DiagnosticSource** | (built-in .NET 8) | Activity & Metrics API | Built into .NET 8 - no package needed. Use `ActivitySource` for tracing, `Meter` for metrics. |

**Key Insight:** Serilog is already installed (4.3.0 in Infrastructure, 10.0.0 Extensions.Hosting in CLI). No logging package additions needed - only enrichers for better structured logging. OpenTelemetry is additive for metrics/tracing, not replacing Serilog.

### Performance Profiling

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **BenchmarkDotNet** | 0.15.2 | Microbenchmarking | Canonical .NET benchmarking tool. Measures throughput, memory allocation, GC. Supports .NET 8 with AOT scenarios. |
| **dotnet-counters** | (global tool) | Real-time performance monitoring | Built-in .NET tool for monitoring CPU, GC, thread pool, exception rates. Zero overhead production monitoring. |
| **dotnet-trace** | (global tool) | Production trace collection | Cross-platform tracing using EventPipe. Generates .nettrace files for PerfView/VS analysis. |
| **PerfView** | (standalone) | Memory & CPU analysis | Microsoft's free performance analyzer. Best for GC heap analysis, JIT compiler behavior, memory leaks. |

**Key Insight:** BenchmarkDotNet is for development-time regression testing. dotnet-counters/dotnet-trace are for production monitoring without code changes. PerfView complements these with deep-dive analysis.

### Error Ergonomics

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **Polly** | 8.4.2 | Transient fault resilience | Standard .NET resilience library for retry, circuit breaker, timeout, fallback. Critical for WinRM fleet operations reliability. |
| **Microsoft.Extensions.Http.Resilience** | 8.10.0 | HTTP resilience with Polly | Microsoft's opinionated Polly wrapper for IHttpClientFactory. Simpler API, built for .NET 8. |
| **Serilog.Exceptions** | 9.0+ | Exception property enrichment | Automatically enriches logs with exception properties (stack trace, inner exceptions, custom properties). Reduces manual error logging boilerplate. |
| **Custom Error Catalog** | (internal) | Centralized error definitions | Not a package - a pattern. Define error codes, messages, recovery actions in one place for consistent UX. |

**Key Insight:** Polly is the industry standard for resilience in .NET. For WinRM fleet operations, retry/circuit breaker patterns prevent cascading failures. Serilog.Exceptions reduces manual error logging code.

### WPF-Specific Testing

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **xUnit** | 2.9.3 (already in use) | Test framework with STA support | Assembly-level `[assembly: STAThread]` attribute enables WPF UI testing. No additional package needed. |
| **Moq** | 4.20.72 (already in use) | Mocking framework | Already in integration tests. No change needed. |
| **FluentAssertions** | 8.8.0 (already in use) | Fluent test assertions | Already installed. No change needed. |

**Key Insight:** No WPF testing packages needed - xUnit 2.9+ has native STA thread support via `[assembly: STAThread]`. WPF controls can be tested directly with this configuration.

### Diagnostic Export

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| **dotnet-dump** | (global tool) | Crash dump collection | Built-in .NET tool for collecting memory dumps on crashes/exceptions. Works offline. |
| **Microsoft.Diagnostics.NET.Client** | 0.15+ | Programmatic diagnostics | Library for collecting dumps, traces, and perf data programmatically. Enables "Export Debug Bundle" feature. |

**Key Insight:** For offline diagnostic export, dotnet-dump and structured log export (Serilog file sink) provide crash context without external services.

## Installation

### Testing & Coverage

```bash
# Core coverage (already using coverlet.collector 6.0.4)
# Add MSBuild package for CI control
dotnet add package coverlet.msbuild --version 6.0.4

# Report generation (global tool)
dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.4.0

# Mutation testing (global tool)
dotnet tool install -g dotnet-stryker --version 4.0.0
```

### Observability

```bash
# Serilog enrichers (no new packages - use existing Serilog 4.3.0)
# Expressions for advanced filtering
dotnet add package Serilog.Expressions --version 5.0.0

# OpenTelemetry metrics & tracing
dotnet add package OpenTelemetry --version 1.12.0
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.12.0

# Exception enrichment
dotnet add package Serilog.Exceptions --version 9.0.0
```

### Performance

```bash
# BenchmarkDotNet for dev-time benchmarking
dotnet add package BenchmarkDotNet --version 0.15.2

# Diagnostic tools (global tools)
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-dump

# PerfView (download from Microsoft)
# https://learn.microsoft.com/en-us/shows/perfview-tutorial
```

### Error Ergonomics

```bash
# Polly for resilience
dotnet add package Polly --version 8.4.2

# HTTP resilience (optional, if using HttpClient)
dotnet add package Microsoft.Extensions.Http.Resilience --version 8.10.0

# Serilog exception enrichment
dotnet add package Serilog.Exceptions --version 9.0.0
```

### Diagnostic Export

```bash
# Programmatic diagnostics access
dotnet add package Microsoft.Diagnostics.NET.Client --version 0.15.0
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| **Coverlet** (open source) | dotCover (JetBrains commercial) | If team has JetBrains ReSharper licenses and wants IDE-integrated coverage. |
| **OpenTelemetry** | Application Insights | If cloud-connected telemetry is acceptable (violates offline-first constraint). |
| **BenchmarkDotNet** | NUnit.Performance | If using NUnit instead of xUnit (not applicable - STIGForge uses xUnit). |
| **Polly** | Manual retry logic | Only for extremely simple retry scenarios. Polly is superior for production. |
| **dotnet-counters/dotnet-trace** | Windows Performance Analyzer (WPA) | WPA has richer UI but Windows-only and steeper learning curve. dotnet tools are cross-platform. |
| **Serilog.Exceptions** | Manual exception logging | If exception enrichment needs are minimal. Not recommended - the package eliminates boilerplate. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| **dotCover** (commercial) | Unnecessary cost. Coverlet provides equivalent coverage for free. | Coverlet MSBuild 6.0.4 |
| **Application Insights** | Violates offline-first constraint. Requires Azure connectivity. | Serilog file sinks + OpenTelemetry metrics |
| **NUnit** | Already standardized on xUnit. Adding NUnit creates fragmentation. | xUnit 2.9.3 (already in use) |
| **SpecFlow/FluentValidation** | Over-engineering for current needs. Validation is UI-bound in WPF. | WPF IDataErrorInfo (built-in) |
| **log4net/NLog** | Already standardized on Serilog. Adding multiple loggers creates complexity. | Serilog 4.3.0 (already in use) |
| **AutoFixture** | Adds complexity for marginal value. Manual test data setup is clearer. | Manual test setup with builders |
| **Moq** (additional usage) | Already using Moq 4.20.72 in integration tests. No change needed. | Continue with Moq 4.20.72 |

## Integration with Existing Stack

### Serilog Integration
```
Current: Serilog 4.3.0 (Infrastructure), Serilog.Extensions.Hosting 10.0.0 (CLI)
Additions: Serilog.Expressions 5.0.0, Serilog.Exceptions 9.0.0
Integration: Use existing WriteTo.File sinks. Add enrichers to LoggerConfiguration.
```

### xUnit Integration
```
Current: xUnit 2.9.3, xunit.runner.visualstudio 3.1.5, coverlet.collector 6.0.4
Additions: Assembly-level [STAThread] for WPF testing
Integration: Add to AssemblyInfo.cs: [assembly: CollectionBehavior(DisableTestParallelization = true)]
                     [assembly: STAThread]
```

### OpenTelemetry + Serilog
```
Pattern: Serilog for structured logging (text payloads)
        OpenTelemetry for metrics + tracing (numerical + span data)
Integration: Use Activity.Current to enrich Serilog logs with TraceId/SpanId
           Serilog doesn't interfere with OpenTelemetry - complementary concerns
```

### Polly + WinRM
```
Pattern: Wrap WinRM PowerShell invocations with Polly retry policies
        Circuit breaker prevents hammering failed fleet nodes
Integration: Use Policy.WrapAsync() for retry + circuit breaker
           Export circuit state to metrics for monitoring
```

### WPF Performance + Dispatcher
```
Pattern: Use dotnet-counters to monitor dispatcher queue length
        Benchmark critical UI operations with BenchmarkDotNet
Integration: Add custom EventCounter for dispatcher operations
           Profile STA thread blocking with PerfView
```

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| Coverlet MSBuild 6.0.4 | .NET 8.0 | Fully compatible, actively maintained |
| ReportGenerator 5.4.0 | Coverage from Coverlet 6.x | Standard combination |
| Stryker.NET 4.0.0 | .NET 8.0, xUnit 2.9.3 | Supports C# 13 syntax |
| OpenTelemetry 1.12.0 | .NET 8.0, Serilog 4.x | Uses built-in System.Diagnostics APIs |
| BenchmarkDotNet 0.15.2 | .NET 8.0 | Supports .NET 8, AOT scenarios |
| Polly 8.4.2 | .NET 8.0 | .NET Standard 2.0+ compatible |
| Serilog.Expressions 5.0.0 | Serilog 4.3.0 | Minor version alignment |
| Serilog.Exceptions 9.0.0 | Serilog 4.3.0 | Compatible major versions |

## Quality Gates Configuration

### Stryker.NET Mutation Testing
```json
{
  "stryker-config": {
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 60
    },
    "mutation-level": "Standard",
    "reporters": ["html", "json", "progress"],
    "coverage-analysis": "perTest"
  }
}
```
**Rationale:** 80% mutation score ensures tests catch real bugs, not just achieve coverage. Lower thresholds during transition period.

### Coverlet Coverage
```xml
<!-- Collect coverage via coverlet.collector (already configured) -->
<!-- Generate HTML reports with ReportGenerator -->
reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage-reports
```
**Rationale:** HTML reports provide historical trend tracking. Cobertura is standard format.

### OpenTelemetry Metrics
```csharp
// Collect critical operational metrics
var meter = new Meter("STIGForge");
var missionCounter = meter.CreateCounter<int>("missions.completed");
var ruleProcessingTime = meter.CreateHistogram<double>("rules.processing_duration_ms");
var winrmRetryCounter = meter.CreateCounter<int>("winrm.retries");
```
**Rationale:** These metrics directly measure mission success, performance, and WinRM reliability.

## Stack Patterns by Variant

**If measuring mission processing performance:**
- Use BenchmarkDotNet for microbenchmarking rule application logic
- Use dotnet-counters for real-time monitoring during manual testing
- Because: BenchmarkDotNet gives reproducible lab measurements; dotnet-counters gives production insight

**If testing WinRM fleet operations:**
- Use Polly retry with exponential backoff
- Use Polly circuit breaker to prevent cascading failures
- Export circuit state to OpenTelemetry metrics
- Because: Fleet operations have transient failures; circuit breakers prevent resource exhaustion

**If debugging WPF UI responsiveness:**
- Use PerfView to analyze dispatcher queue blocking
- Use dotnet-counters to monitor real-time CPU/memory
- Use BenchmarkDotNet to measure ViewModel operations
- Because: WPF performance issues are usually STA thread blocking; these tools identify the source

**If exporting diagnostic bundles for offline analysis:**
- Use Serilog file sinks for structured logs
- Use dotnet-dump for crash dumps
- Include OpenTelemetry metrics export
- Because: Offline environments need self-contained diagnostic data

**If adding mutation testing:**
- Start with Stryker.NET on critical assemblies only (Core, Apply, Verify)
- Use `coverage-analysis: perTest` to reduce execution time
- Set break threshold at 60% initially, target 80%
- Because: Full-project mutation testing is expensive; incremental adoption is practical

## Sources

- Coverlet Documentation (MEDIUM) — [Coverlet GitHub](https://github.com/coverlet-coverage/coverlet) - Verified active maintenance, .NET 8 support
- ReportGenerator (MEDIUM) — [ReportGenerator GitHub](https://github.com/danielpalme/ReportGenerator) - Standard for coverage visualization
- Stryker.NET (MEDIUM) — [Stryker.NET Documentation](https://stryker-mutator.io/) - .NET 8 support confirmed, quality gate patterns
- OpenTelemetry .NET (HIGH) — [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/) - Official docs, .NET 8 integration
- BenchmarkDotNet (HIGH) — [BenchmarkDotNet GitHub](https://github.com/dotnet/BenchmarkDotNet) - Official repository, .NET 8 support verified
- dotnet-counters/dotnet-trace (HIGH) — [.NET Diagnostics Documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) - Official Microsoft docs
- Polly (HIGH) — [Polly Documentation](https://www.pollyproj.org/) - Official site, .NET 8 compatibility confirmed
- Serilog (HIGH) — [Serilog Documentation](https://serilog.net/) - Official docs, enricher patterns
- WPF Testing with xUnit (MEDIUM) — Web search 2026-02-22 - STA thread configuration patterns
- Serilog.Exceptions (MEDIUM) — [Serilog.Exceptions GitHub](https://github.com/catchmethewind/Serilog.Exceptions) - Exception enrichment patterns
- Performance Profiling Tools (MEDIUM) — Web search 2026-02-22 - PerfView, dotMemory comparison
- WPF Dispatcher Performance (MEDIUM) — Web search 2026-02-22 - Dispatcher optimization patterns

---
*Stack research for: STIGForge Next v1.1 Operational Maturity*
*Researched: 2026-02-22*

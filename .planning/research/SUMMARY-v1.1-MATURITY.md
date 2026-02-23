# Project Research Summary: v1.1 Operational Maturity

**Project:** STIGForge Next
**Domain:** Offline-first Windows compliance tooling - Operational Maturity Enhancements
**Researched:** 2026-02-22
**Confidence:** HIGH

## Executive Summary

STIGForge Next v1.1 focuses on operational maturity: achieving 80% test coverage, production-grade observability, performance baselines, and error ergonomics. The research confirms that the existing .NET 8/WPF architecture is well-suited for these enhancements - most capabilities require NEW components with MINIMAL modifications to existing services. The key insight is that all four maturity domains (testing, observability, performance, error UX) can be developed in parallel with clean integration points through the existing DI container.

The recommended approach prioritizes fixing the known flaky test first, then establishing foundation services (telemetry, error catalog), followed by instrumentation of critical paths, and finally test coverage expansion. This order respects the feature dependencies discovered: structured logging is prerequisite for tracing, error codes precede error documentation, and test isolation must be resolved before enforcing coverage thresholds. The dominant risk is "coverage theater" - writing tests that hit coverage numbers without validating meaningful behavior. Mutation testing with Stryker.NET and assertion effectiveness reviews are essential prevention measures.

## Key Findings

### Recommended Stack Additions

The existing stack (.NET 8, WPF, Serilog 4.3.0, xUnit 2.9.3, Coverlet 6.0.4, SQLite) requires targeted additions, not replacements. Key additions are OpenTelemetry for metrics/tracing, BenchmarkDotNet for performance baselining, Polly for resilience, and Stryker.NET for mutation testing.

**Core technologies:**
- **Coverlet MSBuild 6.0.4**: Code coverage - already in use via collector, MSBuild version provides CI control
- **Stryker.NET 4.0+**: Mutation testing - ensures tests catch real bugs, not just achieve coverage
- **OpenTelemetry 1.12.0+**: Metrics & tracing - industry standard, integrates with .NET 8 System.Diagnostics
- **BenchmarkDotNet 0.15.2**: Microbenchmarking - canonical .NET tool for performance regression detection
- **Polly 8.4.2**: Transient fault resilience - critical for WinRM fleet operations reliability
- **Serilog.Expressions 5.0+**: Advanced log filtering - enables debug export bundles

### Expected Features

The feature landscape is organized by maturity domain with clear MVP boundaries.

**Must have (table stakes for v1.1):**
- **80% line coverage on critical assemblies** (Build, Apply, Verify, Infrastructure) - enterprise compliance expectation
- **Structured logging with correlation IDs** - foundation for all observability
- **Human-readable error messages** - operators cannot act on raw exceptions
- **Mission time baselines** - predictable execution windows for scheduling
- **Test isolation (fix flaky test)** - `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` blocks coverage enforcement

**Should have (competitive advantage):**
- **Mission-level tracing (Build->Apply->Verify->Prove)** - end-to-end observability for audit trails
- **Offline observability bundles** - portable diagnostics unique value proposition
- **Error catalog documentation** - searchable error codes with resolutions
- **Scale testing (10K+ rules)** - validate large STIG handling without OOM

**Defer (v2+):**
- **AI-assisted error triage** - requires data collection and model training
- **Deterministic replay from traces** - advanced debugging, nice-to-have
- **Predictive failure detection** - ML-based, needs historical data

### Architecture Approach

The existing layered architecture with dependency injection enables clean addition of cross-cutting services without disrupting existing components. The integration strategy is "add, don't replace" - OpenTelemetry wraps Serilog, error catalog extends existing exceptions, performance instrumenter decorates service calls.

**Major components to add:**
1. **TelemetryService** (Infrastructure/Telemetry/) - OpenTelemetry configuration, ActivitySource management
2. **ErrorCatalog** (Core/Errors/) - Centralized error definitions with recovery guidance
3. **PerformanceInstrumenter** (Infrastructure/Performance/) - Metric collection for mission phases
4. **ErrorRecoveryService** (Infrastructure/Errors/) - Recovery orchestration with strategy pattern

**Integration points (minimal changes):**
- `App.xaml.cs` / `CliHostFactory.cs` - Add 5-10 DI registrations each
- Service layer classes - Add instrumentation calls (decorator pattern)
- Existing exceptions - Inherit from `StigForgeException` base

### Critical Pitfalls

1. **Coverage Theater** - Tests written to hit coverage targets without validating behavior. Prevention: Mutation testing with Stryker.NET, assertion effectiveness reviews, behavior-focused testing over execution paths.

2. **Flaky Test Pandemic** - The pre-existing flaky test must be fixed FIRST before any coverage enforcement. Prevention: IAsyncLifetime pattern, poll-over-sleep, file system test fixtures with unique temp directories.

3. **Telemetry Correlation Breaking** - Trace IDs lost across PowerShell 5.1 process boundaries. Prevention: Explicit TraceId propagation through PowerShell command arguments, OpenTelemetry Activity integration throughout.

4. **Premature Performance Optimization** - Optimizing without profiling data wastes time. Prevention: Profile first with dotnet-trace/dotnet-counters, benchmark real workloads (10K+ rules), establish baselines before optimizing.

5. **Breaking Determinism** - Logging/telemetry changes output formats in compliance artifacts. Prevention: Telemetry separation from business logic outputs, deterministic clocks, stable ordering, side-effect isolation.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Foundation and Test Stability
**Rationale:** Flaky test fix is prerequisite for coverage enforcement. Foundation services enable all subsequent phases.
**Delivers:** Reliable test suite, telemetry/error catalog infrastructure ready for integration
**Addresses:** Fix flaky test, structured logging, error catalog base classes
**Avoids:** Coverage theater (can't enforce with unreliable tests), telemetry correlation breaking (foundation for propagation)

### Phase 2: Observability Integration
**Rationale:** Structured logging must be in place before mission tracing. Correlation IDs enable trace correlation.
**Delivers:** End-to-end mission tracing, correlation across process boundaries, debug export capability
**Uses:** OpenTelemetry, Serilog.Expressions, Serilog.Exceptions
**Implements:** TelemetryService, ActivitySource instrumentation in service layer
**Avoids:** Logging spam and missing context (structured properties), ignoring offline constraints (local telemetry sinks)

### Phase 3: Performance Baselining
**Rationale:** Need telemetry infrastructure in place to measure performance. Benchmarks require stable test environment.
**Delivers:** Startup time baselines, mission duration baselines, 10K+ rule scale validation, memory profile
**Uses:** BenchmarkDotNet, dotnet-counters, dotnet-trace, PerfView
**Implements:** PerformanceInstrumenter, BenchmarkDotNet project
**Avoids:** Premature optimization (profile first), non-deterministic performance testing (proper warmup, statistical significance)

### Phase 4: Test Coverage Expansion
**Rationale:** Can run parallel with Phase 2-3. Coverage gates enforce quality after foundation is stable.
**Delivers:** 80% line coverage on critical assemblies, mutation testing quality gates, CI enforcement
**Uses:** Coverlet MSBuild, ReportGenerator, Stryker.NET
**Avoids:** Coverage theater (mutation testing), testing implementation instead of behavior (black-box patterns)

### Phase 5: Error UX Integration
**Rationale:** Error catalog must exist before UI integration. Recovery flows depend on error definitions.
**Delivers:** Human-readable error messages, recovery guidance, error catalog documentation, WPF recovery dialogs
**Uses:** ErrorCatalog, ErrorRecoveryService, recovery strategy pattern
**Avoids:** Over-engineered error catalogs (error-first design, recovery-focused)

### Phase Ordering Rationale

- **Flaky test first:** Cannot enforce coverage thresholds with unreliable tests. The pre-existing `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` test is a blocker.
- **Foundation before instrumentation:** TelemetryService and ErrorCatalog are prerequisites for service layer instrumentation and recovery flows.
- **Observability before performance:** Need tracing in place to measure mission phases accurately.
- **Coverage can parallelize:** Test coverage work is independent of observability/performance, can proceed in parallel after flaky test fix.
- **Error UX last:** Requires error catalog to exist, builds on recovery service infrastructure.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Performance):** WPF dispatcher optimization patterns for large observable collections, SQLite WAL mode configuration for 10K+ rule performance
- **Phase 5 (Error UX):** Usability testing with actual operators for error message effectiveness

Phases with standard patterns (skip research-phase):
- **Phase 1 (Foundation):** Well-documented xUnit async patterns, Serilog configuration, OpenTelemetry setup
- **Phase 2 (Observability):** Standard OpenTelemetry integration patterns, ActivitySource usage
- **Phase 4 (Coverage):** Coverlet configuration well-documented, mutation testing patterns established

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All recommended packages verified with official sources, .NET 8 compatibility confirmed |
| Features | MEDIUM | Feature prioritization based on industry patterns, needs validation with actual operators |
| Architecture | HIGH | Integration points verified by reading existing codebase, modification scope assessed |
| Pitfalls | MEDIUM | Pitfalls based on industry research and STIGForge-specific constraints, some patterns need real-world validation |

**Overall confidence:** HIGH

### Gaps to Address

- **WPF UI virtualization for 10K+ rules:** Research mentions virtualization but does not specify control-level implementation. Handle during Phase 3 performance work with profiling data.
- **PowerShell 5.1 TraceId propagation:** Pattern identified (pass via command arguments) but no code example in existing codebase. Validate approach during Phase 2 observability integration.
- **Operator error message testing:** Usability research recommended but not conducted. Plan operator feedback session during Phase 5 error UX implementation.
- **Offline telemetry buffering:** OpenTelemetry OTLP exporter buffering strategy not fully specified. Configure during Phase 2 with local file fallback.

## Sources

### Primary (HIGH confidence)
- OpenTelemetry .NET Documentation (https://opentelemetry.io/docs/instrumentation/net/) - Official docs, .NET 8 integration
- BenchmarkDotNet Documentation (https://benchmarkdotnet.org/) - Official repository, .NET 8 support verified
- Polly Documentation (https://www.pollyproj.org/) - Official site, .NET 8 compatibility confirmed
- Serilog Documentation (https://serilog.net/) - Official docs, enricher patterns
- Coverlet Documentation (https://github.com/coverlet-coverage/coverlet) - Verified active maintenance, .NET 8 support
- STIGForge Codebase Analysis - Direct codebase inspection of App.xaml.cs, service interfaces, test configuration

### Secondary (MEDIUM confidence)
- Stryker.NET Documentation (https://stryker-mutator.io/) - .NET 8 support confirmed, quality gate patterns
- Nielsen Norman Group Error Message Guidelines (https://www.nngroup.com/articles/error-message-guidelines/) - UX best practices
- xUnit Shared Context Documentation (https://xunit.net/docs/shared-context) - Async testing patterns
- Martin Fowler: Unit Testing (https://martinfowler.com/bliki/UnitTest.html) - Testing behavior vs implementation
- .NET 8 Performance Improvements (https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/) - Performance patterns

### Tertiary (LOW confidence)
- HarmonyOS App Recovery Guidelines 2026 - General UX patterns, needs adaptation for desktop WPF
- Industry coverage baseline research - General patterns, STIGForge-specific baselines need establishment

---
*Research completed: 2026-02-22*
*Ready for roadmap: yes*

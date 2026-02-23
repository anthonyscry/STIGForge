# Feature Research: v1.1 Operational Maturity

**Domain:** Enterprise Compliance Tooling - Operational Maturity
**Researched:** 2026-02-22
**Confidence:** MEDIUM

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete.

#### Testing Capabilities

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **80% line coverage on critical assemblies** | Enterprise compliance tools require auditable quality gates | MEDIUM | Coverlet already in csproj, need threshold enforcement |
| **Branch coverage reporting** | Line coverage alone misses conditionals - regulators look for path coverage | MEDIUM | Coverlet supports branch coverage with format configuration |
| **CI-integrated coverage gates** | Prevents coverage regression, standard enterprise practice | LOW | dotnet test with CollectCoverage + threshold policies |
| **Unit test isolation** | Flaky tests destroy confidence in compliance verification | MEDIUM | Address existing flaky test: BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory |
| **Test categorization** | Fast unit vs slow integration for pipeline efficiency | LOW | xUnit traits [Trait("Category", "UnitTest")] |
| **PowerShell interop testing** | C#->PowerShell boundaries are high-risk for compliance tooling | HIGH | Pester for PS scripts, xUnit for C# integration points |

#### Observability Capabilities

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Structured logging with correlation IDs** | Trace mission lifecycle across components for audit trails | MEDIUM | Serilog already used, enhance with Enrichers |
| **Mission-level tracing (Build->Apply->Verify->Prove)** | Compliance workflows span multiple steps - observability must connect them | HIGH | OpenTelemetry Activity/ActivitySource for distributed traces |
| **Performance metrics** | Baseline expectations: startup time, mission duration, memory usage | MEDIUM | System.Diagnostics.Meters, Promethus/Grafana |
| **Log levels with environmental toggles** | Production noise vs debugging clarity - essential for ops teams | LOW | Serilog minimum level configuration |
| **Debug export bundles** | Offline compliance requires portable diagnostics for support | MEDIUM | Export logs + traces + config to zip for analysis |
| **Structured error codes** | Error cataloging requires machine-readable identifiers | MEDIUM | Define error code taxonomy (ERR_BUILD_001, etc.) |

#### Performance Capabilities

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Startup time baseline (< 3s cold, < 1s warm)** | CLI tools that feel sluggish lose operator trust | MEDIUM | BenchmarkDotNet for cold/warm startup measurement |
| **Mission time baselines** | Operators need predictable execution windows for scheduling | LOW | Stopwatch instrumentation around mission boundaries |
| **Scale testing (10K+ rules)** | Large STIG bundles must process without OOM or timeout | HIGH | Load testing with Chrome-certified STIG sets |
| **Memory profiling** | Long-running fleet operations must not leak | MEDIUM | dotMemory or similar for baseline establishment |
| **I/O bottleneck identification** | SQLite + file system are critical paths for offline ops | MEDIUM | Trace file/db operations, identify hot paths |

#### Error UX Capabilities

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Human-readable error messages** | "System.NullReferenceException" means nothing to ISSOs | MEDIUM | Error message template system |
| **Recovery guidance in errors** | Don't just say what failed - say what to do next | MEDIUM | "Next steps" section in error output |
| **Self-service remediation** | Reduce support burden by enabling operator-driven fixes | HIGH | Detect common errors, offer automated fixes |
| **Error catalog documentation** | Searchable error codes with resolutions | LOW | Static error catalog with markdown docs |
| **Graceful degradation** | Partial failures shouldn't abort entire missions | MEDIUM | Apply continues with non-critical failures, report separately |
| **Rollback capability** | Apply operations must be reversible for failed missions | HIGH | SnapshotService exists, enhance with error-triggered rollback |

### Differentiators (Competitive Advantage)

Features that set the product apart. Not required, but valuable.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Deterministic replay from traces** | Reproduce any compliance mission from captured telemetry | HIGH | Export traces allow exact mission reconstruction |
| **AI-assisted error triage** | Pattern recognition across error logs identifies systemic issues | HIGH | Use LLM to analyze error clusters (future v2+) |
| **Compliance-specific metrics** | STIG coverage, rule pass rate, artifact checksum trends | MEDIUM | Domain-specific counters beyond generic ops metrics |
| **Mission diff visualization** | Show exactly what changed between compliance runs | MEDIUM | Compare bundles + verify results + evidence packages |
| **Predictive failure detection** | Warn operators before missions fail based on patterns | HIGH | ML model on historical mission outcomes (v2+) |
| **Offline observability bundles** | Portable diagnostics that work without internet - unique value prop | MEDIUM | Self-contained HTML dashboard with embedded data |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **Real-time streaming telemetry** | See operations as they happen | Offline-first conflicts, adds complexity | Batched telemetry with periodic flush |
| **Auto-fix without confirmation** | Reduce operator workload | Violates safety invariants, dangerous for compliance | Review-required workflow with manual approval |
| **Generic "catch-all" exception handlers** | Simplify error handling | Swallows root cause, makes debugging impossible | Explicit error handling at component boundaries |
| **100% test coverage mandate** | Maximum quality assurance | Diminishing returns, false sense of security | 80% coverage + critical path coverage focus |
| **Centralized cloud observability** | Modern best practice | Conflicts with offline requirement | Local-first observability with optional export |
| **Complex retry logic with exponential backoff** | Handle transient failures | Complicates timeout calculations, hard to test | Explicit retry with clear configuration, no magic defaults |

## Feature Dependencies

```
[Structured Error Codes]
    └──requires──> [Error Catalog Documentation]
                   └──enhances──> [Human-Readable Error Messages]
                                   └──requires──> [Recovery Guidance]

[Mission-Level Tracing]
    └──requires──> [Structured Logging]
                   └──enhances──> [Deterministic Replay]
                                   └──requires──> [Offline Observability Bundles]

[80% Line Coverage]
    └──requires──> [Test Categorization]
                   └──requires──> [Test Isolation (fix flaky tests)]

[Self-Service Remediation]
    └──requires──> [Recovery Guidance]
                   └──requires──> [Rollback Capability]

[Performance Baselines]
    └──requires──> [Mission-Level Tracing]
```

### Dependency Notes

- **Structured Error Codes requires Error Catalog Documentation**: Cannot document what doesn't exist. Error codes must be defined before catalog can be created.
- **Mission-Level Tracing requires Structured Logging**: Tracing builds on logging infrastructure - correlation IDs and structured events are prerequisites.
- **80% Line Coverage requires Test Isolation**: Cannot enforce coverage thresholds with flaky tests. Must fix `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` first.
- **Self-Service Remediation requires Recovery Guidance**: Cannot offer fixes if errors don't explain what went wrong and what to do.
- **Performance Baselines requires Mission-Level Tracing**: Need trace data to establish accurate performance baselines for real-world operations.

## Existing Codebase Assets

Dependencies on existing STIGForge Next codebase:

| Asset | Location | How It Helps |
|-------|----------|--------------|
| **Serilog logging** | 26 files using ILogger/ILoggerFactory | Foundation for structured logging, needs correlation ID enhancement |
| **Coverlet collector** | tests/*.csproj (v6.0.4) | Coverage tooling present, needs threshold enforcement |
| **xUnit + Moq** | Test projects | Framework in place, needs categorization traits |
| **SnapshotService** | STIGForge.Apply/Snapshot/* | Rollback capability exists, needs error-triggered enhancement |
| **Service registration** | CLI/WPF startup | Injection points for observability components |
| **Flaky test identified** | BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory | Known blocker for coverage enforcement |

## MVP Definition

### Launch With (v1.1)

Minimum viable product - what's needed to validate operational maturity.

- [ ] **80% Line Coverage on Critical Assemblies** — Essential for quality gate establishment
- [ ] **Structured Logging with Correlation IDs** — Foundation for all observability
- [ ] **Human-Readable Error Messages** — Essential UX improvement for operators
- [ ] **Mission Time Baselines** — Basic performance expectations
- [ ] **Test Isolation (Fix Flaky Test)** — Remove existing reliability blocker

### Add After Validation (v1.2)

Features to add once core maturity is working.

- [ ] **Mission-Level Tracing** — Enable end-to-end observability
- [ ] **Error Catalog Documentation** — Searchable error reference
- [ ] **Scale Testing (10K+ Rules)** — Validate large STIG handling
- [ ] **Self-Service Remediation** — Reduce support burden

### Future Consideration (v2+)

Features to defer until product-market fit is established.

- [ ] **AI-Assisted Error Triage** — Requires data collection and model training
- [ ] **Predictive Failure Detection** — ML-based, needs historical data
- [ ] **Deterministic Replay** — Advanced debugging, nice-to-have
- [ ] **100% Coverage Areas** — Security-critical paths only, not blanket mandate

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Fix flaky test (BuildHost) | HIGH | LOW | P1 |
| Structured error codes | HIGH | MEDIUM | P1 |
| Human-readable error messages | HIGH | MEDIUM | P1 |
| 80% line coverage (critical assemblies) | HIGH | MEDIUM | P1 |
| Mission time baselines | HIGH | LOW | P1 |
| Correlation IDs in logging | MEDIUM | MEDIUM | P2 |
| Mission-level tracing | MEDIUM | HIGH | P2 |
| Error catalog documentation | MEDIUM | LOW | P2 |
| Scale testing (10K+ rules) | MEDIUM | HIGH | P2 |
| Self-service remediation | MEDIUM | HIGH | P3 |
| Performance optimization (startup) | LOW | MEDIUM | P3 |
| Deterministic replay from traces | LOW | HIGH | P3 |
| AI-assisted error triage | LOW | HIGH | P3 |

**Priority key:**
- P1: Must have for v1.1 launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

## Competitor Feature Analysis

| Feature | PowerSTIG | SCCM | STIGForge Approach |
|---------|-----------|------|-------------------|
| Test coverage | Unknown (closed source) | Enterprise-grade validation | Open testing, 80% baseline |
| Observability | Basic logging | Full enterprise monitoring | Offline-first, mission-centric |
| Error UX | PowerShell errors | Enterprise support UI | Structured codes + recovery guidance |
| Performance | PowerShell overhead | Enterprise scale | Native C# performance, measured baselines |
| Rollback | Manual configuration | Configuration drift rollback | Deterministic snapshot-based rollback |

## Sources

### Test Coverage & Testing Patterns
- HIGH: [.NET Code Coverage Documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/code-coverage/)
- HIGH: [Coverlet Documentation](https://github.com/coverlet-coverage/coverlet) - Cross-platform coverage for .NET
- MEDIUM: [xUnit Trait Documentation](https://xunit.net/docs/shared-context) - Test categorization
- MEDIUM: [Pester Testing Framework](https://pester.dev/) - PowerShell testing
- LOW: [CSDN Test Coverage Standards](https://blog.csdn.net/) - Industry coverage baselines

### Observability & Telemetry
- HIGH: [.NET 8 OpenTelemetry Documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-dotnet)
- HIGH: [OpenTelemetry .NET GitHub](https://github.com/open-telemetry/opentelemetry-dotnet)
- HIGH: [System.Diagnostics.Meter API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics)
- MEDIUM: [Serilog Documentation](https://serilog.net/) - Structured logging
- MEDIUM: [2026 Telemetry Engineering Trends](https://blog.gruntwork.io/telemetry-engineering-2026)
- LOW: [Security Observability for Zero Trust](https://www.crowdstrike.com/cybersecurity-101/observability/)

### Performance & Baselines
- HIGH: [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- HIGH: [.NET Performance Best Practices](https://learn.microsoft.com/en-us/dotnet/framework/performance/performance-tips)
- MEDIUM: [Microsoft Security Compliance Manager](https://www.microsoft.com/en-us/security/business/security-compliance-manager)
- LOW: [UL Solutions Performance Testing](https://www.ul.com/solutions/performance-testing)
- LOW: [JD Cloud Security Compliance Baselines](https://jd.com/) - Baseline automation patterns

### Error UX & Recovery Patterns
- HIGH: [Nielsen Norman Group - Error Message Guidelines](https://www.nngroup.com/articles/error-message-guidelines/)
- MEDIUM: [Ant Design Error Handling](https://ant.design/docs/spec/overview)
- MEDIUM: [Power Platform Error Monitoring](https://learn.microsoft.com/en-us/power-platform/monitor/)
- MEDIUM: [Vue Storefront Error Architecture](https://www.vuestorefront.io/) - Multi-layer error handling
- LOW: [Smashing Magazine AI Error Patterns](https://www.smashingmagazine.com/) - 2026 AI error trends
- LOW: [User Experience Design Methodology](https://www.nngroup.com/articles/) - Five-layer error handling

### STIG/DoD Compliance Tooling
- HIGH: [DoD STIG Overview](https://public.cyber.mil/stigs/)
- MEDIUM: [Oracle STIG Tool Documentation](https://docs.oracle.com/en/) - Rollback & compliance patterns
- MEDIUM: [AWS Systems Manager STIG Automation](https://docs.aws.amazon.com/systems-manager/) - Automated compliance
- LOW: [Parasoft Military Defense Solutions](https://www.parasoft.com/) - Mission-critical testing

### C# & PowerShell Interop
- HIGH: [System.Management.Automation Documentation](https://learn.microsoft.com/en-us/powershell/scripting/developer/cmdlet)
- HIGH: [Pester GitHub](https://github.com/pester/Pester) - PowerShell testing
- MEDIUM: [RunspaceFactory Class](https://learn.microsoft.com/en-us/dotnet/api/system.management.automation.runspaces.runspacefactory)

---
*Feature research for: STIGForge Next v1.1 Operational Maturity*
*Researched: 2026-02-22*

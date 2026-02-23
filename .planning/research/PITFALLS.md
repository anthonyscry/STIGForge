# Domain Pitfalls

**Domain:** Offline-first Windows compliance workflow tooling (adding operational maturity)
**Researched:** 2026-02-22
**Confidence:** MEDIUM

## Critical Pitfalls

Mistakes that cause rewrites or major issues when adding operational maturity to existing systems.

### Pitfall 1: Coverage Theater

**What goes wrong:**
Tests are written to hit coverage targets rather than validate behavior. High coverage numbers (80%+) are achieved with tests that execute code without asserting meaningful outcomes. This creates false confidence while critical bugs remain undetected.

**Why it happens:**
Management mandates "80% coverage" without quality criteria. Developers write "bubble tests" that call methods but don't verify correct behavior. Tests mock everything, never exercising real integration points. The metric becomes the goal rather than actual quality.

**Consequences:**
- Critical bugs reach production despite "high coverage"
- Test suite becomes maintenance burden without value
- Refactoring becomes dangerous (tests pass but behavior breaks)
- Team loses trust in test suite

**Prevention:**
1. **Test quality gates before coverage gates**: Require assertion effectiveness reviews
2. **Meaningful coverage targets**: Different targets by code type (90%+ for business logic, 70% for utilities, lower for simple models)
3. **Behavior-focused testing**: Test outcomes, not execution paths
4. **Mutation testing**: Use tools like Stryker.NET to detect ineffective tests
5. **Manual test reviews**: Quality checks beyond coverage metrics

**Detection:**
- Tests with no assertions or Assert.True(true)
- Tests where everything is mocked (no real code paths exercised)
- Coverage percentage rising while bug rate stays flat
- Developers saying "tests pass but it doesn't work"

**Phase to address:** v1.1 Operational Maturity - Testing Phase

**Sources:** [Web search results on test coverage pitfalls](https://developer.microsoft.com/en-us/microsoft-edge/blog/adding-test-coverage-to-existing-csharp-dotnet-application-common-pitfalls-mistakes-2026/) - **MEDIUM confidence**

---

### Pitfall 2: Flaky Test Pandemic

**What goes wrong:**
Async tests pass in isolation but fail intermittently in full suite. Tests depend on shared state, timing, or external resources. The test suite becomes unreliable, leading to skipped tests or disabled CI gates.

**Why it happens:**
Async/await patterns misused (missing await, Task not awaited), static state shared between tests, hardcoded Thread.Sleep() instead of proper synchronization, file system dependencies not cleaned up, test execution order dependencies.

**Consequences:**
- CI/CD becomes unreliable (false failures block deployments)
- Developers lose trust, start ignoring test failures
- Root cause analysis wastes engineering time
- Pre-existing flaky test (`BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory`) is known but never fixed

**Prevention:**
1. **Test isolation**: Each test must be completely independent
2. **IAsyncLifetime pattern**: Proper async setup/teardown with xUnit
3. **Poll over sleep**: Replace Thread.Sleep with WaitForCondition patterns
4. **File system test fixtures**: Unique temp directories per test, proper cleanup
5. **Avoid static state**: Use dependency injection, test fixtures
6. **Retry analysis**: Run flaky tests multiple times to identify patterns

**Detection:**
- Test passes in isolation but fails in full suite
- Intermittent failures with no code changes
- Tests fail only on certain CI agents or at certain times
- Test order affects results

**Phase to address:** v1.1 Operational Maturity - Testing Phase (fix pre-existing flaky test first!)

**Sources:** [xUnit/NUnit async testing patterns](https://xunit.net/docs/shared-context) - **HIGH confidence**, [Flaky test prevention research](https://microsoft.github.io/code-with-engineering-people/learning-resources/testing/flaky-tests/) - **MEDIUM confidence**

---

### Pitfall 3: Telemetry Correlation Breaking

**What goes wrong:**
Adding structured logging breaks correlation between existing logs, making distributed tracing impossible. Trace IDs don't propagate across process boundaries (especially PowerShell 5.1 interop). Log searches become "personal detective work" instead of unified queries.

**Why it happens:**
Serilog added without Activity/TraceContext integration. PowerShell process boundaries don't inherit correlation IDs. Logging format changes break existing log parsers. Synchronous logging blocks async operations, causing timeouts that corrupt trace chains.

**Consequences:**
- Debugging multi-process workflows becomes impossible
- Can't trace requests across CLI → PowerShell → DSC boundaries
- Performance analysis loses critical context
- Compliance audit trails become fragmented

**Prevention:**
1. **OpenTelemetry integration**: Use System.Diagnostics.Activity throughout
2. **Trace ID propagation**: Explicitly pass correlation IDs across process boundaries
3. **Async logging**: Use Serilog.WriteTo.Async to prevent blocking
4. **Structured log format**: JSON output with consistent property naming
5. **Backward compatibility**: Don't break existing log formats without migration plan
6. **Testing correlation**: Unit tests verify TraceId survives process boundaries

**Detection:**
- Can't link CLI logs to PowerShell execution logs
- Missing TraceId in log entries
- Log searches require manual correlation
- Performance impact from synchronous logging

**Phase to address:** v1.1 Operational Maturity - Observability Phase

**Sources:** [Serilog implementation mistakes](https://nblumhardt.com/2025/01/serilog-structured-logging-mistakes/) - **HIGH confidence**, [Enterprise telemetry correlation](https://learn.microsoft.com/en-us/azure/azure-monitor/app/correlation) - **HIGH confidence**

---

### Pitfall 4: Premature Performance Optimization

**What goes wrong:**
Engineering time spent optimizing code that isn't a bottleneck. Microbenchmarks show "impressive" gains but don't reflect real-world usage patterns. Actual performance issues remain unaddressed while code becomes more complex.

**Why it happens:**
"Knuth's quote is ignored" - developers optimize based on intuition. Microbenchmarks isolate code from real context. Management demands "make it faster" without profiling data. Success measured by benchmark numbers, not user experience.

**Consequences:**
- Complex, unreadable code for marginal gains
- Real bottlenecks (10K+ rule processing, memory pressure) ignored
- Technical debt from "clever" optimizations
- Missed opportunity costs (could have built features instead)

**Prevention:**
1. **Profile first, optimize second**: Use dotnet-trace, dotnet-counters to find real bottlenecks
2. **Benchmark real workloads**: BenchmarkDotNet with realistic data (10K+ rules)
3. **User-centric metrics**: Measure mission speed, startup time, not micro-operations
4. **Target critical paths**: Optimize hot paths identified by profiling
5. **Before/after validation**: Prove optimization helps actual user scenarios
6. **Code readability over cleverness**: Optimize only when data justifies it

**Detection:**
- Optimizations without baseline measurements
- Benchmarking isolated methods, not workflows
- Complex code without performance issue ticket
- Premature optimization in reviews (before profiling data)

**Phase to address:** v1.1 Operational Maturity - Performance Phase

**Sources:** [Performance optimization pitfalls research](https://dev.to/garrypassarella/premature-optimization-performance-pitfalls-2026) - **MEDIUM confidence**, [BenchmarkDotNet best practices](https://benchmarkdotnet.org/articles/guides/) - **HIGH confidence**

---

### Pitfall 5: Over-Engineered Error Catalogs

**What goes wrong:**
Massive error catalog with hundreds of error codes and recovery flows. Operators can't find relevant errors. Recovery flows hide root causes behind generic "something went wrong" messages. Error UX becomes a maze instead of a helpful guide.

**Why it happens:**
"Let's categorize all possible errors" mindset. Error codes assigned without recovery strategy. Generic error messages used for "security." Recovery flows designed for ideal cases, not real operator stress. Error catalog treated as documentation rather than UX.

**Consequences:**
- Operators can't troubleshoot (too many errors to search)
- Root cause hidden behind generic messages
- Recovery flows don't match actual error scenarios
- Compliance audit trail loses detail (generic errors don't capture "what")

**Prevention:**
1. **Error-first design**: Start with user impact, not error codes
2. **Recovery-focused**: Each error must have actionable recovery step
3. **Progressive disclosure**: Summary first, technical details on demand
4. **Preserve root cause**: Never hide what actually failed
5. **Error catalog by workflow**: Organize by mission phase (build/apply/verify/export)
6. **Test error UX**: Usability testing with actual operators

**Detection:**
- Error codes without recovery documentation
- Generic "operation failed" messages in logs
- Operators saying "I don't know what to do"
- Error catalog organized by component, not user task

**Phase to address:** v1.1 Operational Maturity - Error UX Phase

**Sources:** [Enterprise error recovery UX patterns](https://www.nngroup.com/articles/error-message-guidelines/) - **MEDIUM confidence**, [HarmonyOS app recovery guidelines 2026](https://developer.harmonyos.com/en-us/docs/ui/ux-error-recovery) - **MEDIUM confidence**

---

### Pitfall 6: Non-Deterministic Performance Testing

**What goes wrong:**
Performance tests produce different results on each run. Benchmarks don't control for garbage collection, CPU throttling, or background processes. "Optimizations" are validated based on noisy data. Performance regressions go undetected.

**Why it happens:**
Benchmarks run without warmup. No isolation from background processes. Garbage collection not controlled. Small sample sizes. No statistical significance testing. Benchmarks run on developer machines, not CI.

**Consequences:**
- False positives (optimizations appear to work but don't)
- False negatives (real regressions masked by noise)
- Performance debates instead of data-driven decisions
- Can't set meaningful performance SLAs

**Prevention:**
1. **BenchmarkDotNet discipline**: Use proper warmup, iterations, statistical analysis
2. **Isolated test environment**: CI/CD with controlled resources
3. **Realistic workloads**: Test with actual STIG data (10K+ rules), not synthetic
4. **Memory profiling**: dotMemory, dotnet-counters for GC behavior
5. **Baseline tracking**: Store historical benchmarks for regression detection
6. **Statistical significance**: Require confidence intervals, not single runs

**Detection:**
- Performance results vary >10% between runs
- Benchmarks without warmup periods
- Sample sizes < 100 iterations
- Testing on developer machines instead of CI

**Phase to address:** v1.1 Operational Maturity - Performance Phase

**Sources:** [Memory profiling BenchmarkDotNet .NET 8](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debugging-high-memory) - **HIGH confidence**, [.NET 8 performance improvements research](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/) - **HIGH confidence**

---

### Pitfall 7: Logging Spam and Missing Context

**What goes wrong:**
Logs flood with verbose messages that obscure critical information. Important events logged at wrong level (ERROR for expected conditions, INFO for critical failures). Structured properties missing from log entries. Log files become unsearchable.

**Why it happens:**
"More logging is better" mindset. Log level used for severity instead of operator impact. Template syntax errors prevent property extraction. Sensitive information logged accidentally. Log rotation not configured.

**Consequences:**
- Can't find critical errors in log spam
- Log searches return thousands of irrelevant results
- Disk space exhaustion from verbose logging
- Compliance audit trail compromised (missing context)

**Prevention:**
1. **Log by operator impact**: ERROR = blocks mission, WARNING = requires attention, INFO = progress, DEBUG = diagnostics
2. **Structured properties**: Always use {Property} syntax, not string interpolation
3. **SerilogAnalyzer**: Static analysis for template syntax errors
4. **Log sampling**: High-frequency events sampled, not logged every occurrence
5. **Sensitive data sanitization**: Property rewriters to filter passwords/tokens
6. **Log rotation**: Size and time-based retention policies

**Detection:**
- Log files > 1GB per day
- Search returns thousands of matches for common terms
- String concatenation in log calls (Log.Info($"User {user} logged in"))
- Missing structured properties in log entries

**Phase to address:** v1.1 Operational Maturity - Observability Phase

**Sources:** [Serilog structured logging mistakes](https://github.com/serilog/serilog/wiki/Structured-Data-Cookbook) - **HIGH confidence**, [Enterprise logging patterns](https://martinfowler.com/articles/production-ready-logging.html) - **MEDIUM confidence**

---

### Pitfall 8: Testing Implementation Instead of Behavior

**What goes wrong:**
Tests verify internal implementation details (private methods, specific algorithms) rather than observable behavior. Refactoring breaks tests even when external behavior unchanged. Tests prevent valid code improvements.

**Why it happens:**
Testing private methods for "completeness." Tests mirror implementation structure. Mocking concrete classes instead of interfaces. Test doubles that are too specific. Testing "how" instead of "what."

**Consequences:**
- Refactoring becomes impossible (tests break)
- Technical debt accumulates (can't improve code)
- Tests don't catch actual bugs (implementation correct, behavior wrong)
- Test suite becomes legacy code itself

**Prevention:**
1. **Test public contracts**: Test interfaces and observable outcomes
2. **Black-box testing**: Treat units as opaque, test inputs/outputs
3. **Behavior verification**: Assert on results, not implementation
4. **Interface-based design**: Mock interfaces, not concrete classes
5. **Test scenarios, not methods**: One test per user scenario, not per method
6. **Refactor-friendly**: Tests should survive implementation changes

**Detection:**
- Tests access private/internal members via reflection
- Tests named after methods (Test_MyMethod) instead of scenarios (Test_UserLogin_WithInvalidCredentials_Fails)
- Mocks specify exact call sequences
- Tests break when code is refactored (behavior unchanged)

**Phase to address:** v1.1 Operational Maturity - Testing Phase

**Sources:** [Unit testing best practices](https://martinfowler.com/bliki/UnitTest.html) - **HIGH confidence**, [Testing behavior vs implementation](https://kentbeck.com/test-design-framing/) - **MEDIUM confidence**

---

## Moderate Pitfalls

### Pitfall 9: Memory Leaks in Long-Running Tests

**What goes wrong:**
Integration tests that run for hours accumulate memory, eventually failing with OutOfMemoryException. Resources not properly disposed (SQLite connections, file handles, PowerShell processes). Test framework doesn't enforce cleanup.

**Why it happens:**
IDisposable not implemented or not called. Static collections grow without bounds. Event subscribers not detached. Test fixtures don't implement IAsyncLifetime. PowerShell runspaces not disposed.

**Consequences:**
- Test suite fails intermittently
- CI agents exhausted during long test runs
- Memory masks real performance issues
- Developer time wasted on OOM investigation

**Prevention:**
1. **IAsyncLifetime**: Always implement for test fixtures with resources
2. **Using statements**: Enforce for disposable resources
3. **Memory profiling in tests**: dotMemory for leak detection
4. **Test timeouts**: Fail fast if tests run too long
5. **Resource limits**: Set memory thresholds in test runs

**Detection:**
- Test memory usage grows over time
- Tests fail after long runs but not short runs
- "GC.AddMemoryPressure" needed to make tests fail
- PowerShell processes accumulate

**Phase to address:** v1.1 Operational Maturity - Testing Phase

**Sources:** [.NET memory management](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/) - **HIGH confidence**

---

### Pitfall 10: Breaking Determinism in Compliance Artifacts

**What goes wrong:**
Adding logging, telemetry, or performance monitoring changes output formats, timestamps, or ordering in compliance artifacts (CKL, eMASS packages). Identical inputs produce different outputs between versions. Audit trails become non-reproducible.

**Why it happens:**
Wall-clock timestamps in exports. Random IDs added for telemetry. Filesystem iteration order affects manifests. Logging side effects change output. Race conditions in parallel processing.

**Consequences:**
- Compliance audits fail (packages not reproducible)
- Diff-based regression testing breaks
- Trust in evidence packages compromised
- Violates core determinism invariant

**Prevention:**
1. **Deterministic clocks**: Use fixed timestamps or policy-controlled normalization
2. **Stable ordering**: Sort all collections before export
3. **Telemetry separation**: Observability must not affect business logic outputs
4. **Determinism tests**: Compare outputs from identical inputs
5. **Side-effect isolation**: Logging/telemetry outside critical paths

**Detection:**
- Export packages change with no input changes
- File ordering differs between runs
- Timestamps vary for identical operations
- Regression tests fail due to format changes

**Phase to address:** v1.1 Operational Maturity - All phases (determinism invariant)

**Sources:** [STIGForge project constraints](/.planning/PROJECT.md) - **HIGH confidence**, [Deterministic builds research](https://reproducible-builds.org/) - **MEDIUM confidence**

---

### Pitfall 11: Ignoring Offline Constraints in Observability

**What goes wrong:**
Telemetry, metrics, or error reporting requires internet connectivity. OpenTelemetry collectors configured for cloud endpoints. Error recovery flows reference online documentation. Application fails in air-gapped environments.

**Why it happens:**
Default telemetry configurations assume cloud. Error messages link to online docs. Recovery steps assume online resources. Testing only on internet-connected machines.

**Consequences:**
- Application fails in air-gapped environments
- Error recovery impossible (offline docs referenced)
- Violates offline-first constraint
- Field deployment failures

**Prevention:**
1. **Local telemetry sinks**: File-based logging, local OpenTelemetry collector
2. **Embedded documentation**: Error messages include recovery steps, not URLs
3. **Offline testing**: Test observability in air-gapped environments
4. **Feature flags**: Disable cloud telemetry when offline detected
5. **Local diagnostics**: Debug export bundles for offline analysis

**Detection:**
- Error messages contain HTTP URLs
- Telemetry configured for cloud endpoints
- Application crashes without internet
- Testing only on connected machines

**Phase to address:** v1.1 Operational Maturity - Observability Phase

**Sources:** [STIGForge offline constraints](/.planning/PROJECT.md) - **HIGH confidence**, [Offline observability patterns](https://opentelemetry.io/docs/reference/specification/protocol/otlp/) - **MEDIUM confidence**

---

## Minor Pitfalls

### Pitfall 12: Test Suite Performance Degradation

**What goes wrong:**
Test suite becomes slower over time as more tests added. Full test run takes hours. Developers skip running tests locally. CI/CD pipeline becomes bottleneck.

**Prevention:**
- Parallel test execution (xUnit parallelization)
- Test categorization (unit/integration/e2e with different run frequencies)
- Test performance budgets (fail if tests exceed thresholds)
- Incremental test runs (only run affected tests)

**Phase to address:** v1.1 Operational Maturity - Testing Phase

---

### Pitfall 13: Missing Edge Case Coverage

**What goes wrong:**
Tests cover happy paths but miss edge cases. Null inputs, empty collections, boundary conditions untested. Real-world data triggers failures that tests miss.

**Prevention:**
- Property-based testing (FsCheck)
- Fuzzing for input validation
- Boundary value analysis
- Real-world STIG data in test fixtures

**Phase to address:** v1.1 Operational Maturity - Testing Phase

---

## Technical Debt Patterns

Shortcuts that seem reasonable when adding operational maturity but create long-term problems.

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Testing private methods via InternalsVisibleTo | Quick test coverage | Refactoring becomes brittle, tests break on valid changes | Never - test public contracts |
| Thread.Sleep in tests instead of proper synchronization | Tests pass reliably | Flaky tests, slow test suite, masked race conditions | Never - use polling/waits |
| Mocking everything to isolate units | Fast, isolated tests | Tests don't validate integrations, false confidence | Only for external dependencies (PowerShell, DSC) |
| Generic error messages | Simpler error catalog | Operators can't troubleshoot, root cause hidden | Never - compliance requires detail |
| Coverage targets without quality gates | Meets management metrics | Coverage theater, false confidence | Never - quality before quantity |
| Adding logging without structured properties | Faster implementation | Unsearchable logs, lost context | Never - invest in structured logging |
| Performance optimization without profiling | Feels productive | Wrong code optimized, real issues ignored | Never - profile first |
| Error codes without recovery documentation | Completes error catalog | Operators stuck, support burden increases | Never - recovery first |

---

## Integration Gotchas

Common mistakes when adding operational maturity to existing STIGForge integrations.

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Serilog logging | Using string interpolation instead of templates | Use structured templates: `Log.Information("Processing {STIG}", stigId)` |
| PowerShell 5.1 interop | Losing correlation IDs across process boundary | Explicitly pass TraceId through PowerShell command arguments |
| DSC/PowerSTIG execution | Blocking synchronous logging during apply | Use async logging sinks to prevent DSC timeouts |
| SQLite operations | Not measuring memory pressure from large datasets | Profile with dotnet-counters during 10K+ rule processing |
| WinRM fleet ops | Missing request correlation across async operations | Include Activity.Current.TraceId in WinRM payloads |
| File-based telemetry | Writing to same directory as business data | Separate telemetry directory to prevent artifact pollution |
| OpenTelemetry | Configuring cloud endpoints in offline scenarios | Use file-based exporter + local collector |

---

## Performance Traps

Patterns that work at small scale but fail at STIGForge's target scale (10K+ rules, air-gapped environments).

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| LINQ over large collections | Memory spikes, slow processing | Use Span<T>, foreach, or streaming APIs | At 1K+ rules in memory |
| Repeated XML parsing | CPU-bound slowdown during verify | Parse once, cache canonical models | At 500+ STIG files |
| Synchronous I/O | UI freezes during export | Use async/await throughout | At any file operation |
| Unbounded growth | Memory leaks during long runs | Pre-allocate, use fixed-size buffers | During 10K+ rule processing |
| GC pressure | Pauses during mission execution | Reduce allocations, use object pools | At 1K+ allocations/second |
| String concatenation | Memory churn, fragmentation | Use StringBuilder, string.Create | In loops or hot paths |
| Reflection in hot paths | Slow startup, poor throughput | Compile-time code generation, source generators | During bundle compilation |

---

## Security Mistakes

Domain-specific security issues for compliance-critical applications.

| Mistake | Risk | Prevention |
|---------|------|------------|
| Logging sensitive data (passwords, tokens) | Compliance violation, security breach | Implement property rewriters to sanitize logs |
| Error messages exposing internal paths | Information disclosure | Use user-facing error catalog, not exception messages |
| Audit trail manipulation | Compliance failure, legal liability | Append-only logs, hash-chained integrity verification |
| Non-deterministic evidence packages | Audit rejection | Stable sort, policy-controlled timestamps, determinism tests |
| Missing provenance in exports | Can't verify evidence source | Include all metadata (tool versions, hashes, timestamps) |
| Test data in production artifacts | Data leakage | Separate test/production data stores, validation |

---

## UX Pitfalls

Common user experience mistakes in operational maturity features.

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Generic error messages | Operators don't know how to recover | Include actionable recovery steps in error messages |
| Hidden technical details | Can't debug issues | Progressive disclosure: summary first, details on expand |
| Performance metrics without context | Numbers don't mean anything | Compare to baselines, show user impact (time saved) |
| Test failures without guidance | Developers waste time investigating | Include likely causes and next steps in failure output |
| Log files without structure | Can't find relevant information | Structured JSON logs with searchable properties |
| Error catalog organized by component | Users think in workflows, not architecture | Organize by mission phase (build/apply/verify/export) |

---

## "Looks Done But Isn't" Checklist

Things that appear complete but are missing critical pieces for operational maturity.

- [ ] **Test coverage at 80% but**: Tests assert on execution, not behavior — verify tests validate outcomes, not just run code
- [ ] **Logging added but**: Missing correlation IDs across process boundaries — verify TraceId survives PowerShell interop
- [ ] **Performance benchmarks pass but**: Using synthetic data instead of real STIGs — verify benchmarks use 10K+ rule datasets
- [ ] **Error catalog created but**: Generic messages without recovery steps — verify each error has actionable guidance
- [ ] **Telemetry integrated but**: Requires internet connectivity — verify all observability works offline
- [ ] **Tests fast but**: Only happy paths tested — verify edge cases, error conditions, boundary values
- [ ] **Structured logging in place but**: Template syntax errors prevent property extraction — run SerilogAnalyzer
- [ ] **Performance optimized but**: No baseline measurements for regression detection — establish historical benchmarks

---

## Recovery Strategies

When pitfalls occur despite prevention, how to recover.

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Coverage theater | HIGH | Audit all tests, remove/rewrite those without meaningful assertions, implement mutation testing |
| Flaky tests | MEDIUM | Isolate affected tests, implement IAsyncLifetime, add proper synchronization, fix pre-existing flaky test |
| Broken correlation | HIGH | Audit telemetry pipeline, add OpenTelemetry integration, implement cross-boundary TraceId propagation |
| Premature optimization | MEDIUM | Profile to find real bottlenecks, revert unnecessary optimizations, focus on hot paths |
| Over-engineered errors | MEDIUM | Simplify catalog, organize by user workflow, add recovery steps, remove unused codes |
| Non-deterministic performance | LOW | Add BenchmarkDotNet discipline, establish baselines, implement statistical significance testing |
| Logging spam | LOW | Configure log levels, implement sampling, add structured properties, set rotation policies |
| Testing implementation | HIGH | Refactor tests to public contracts, use black-box testing, test scenarios not methods |

---

## Pitfall-to-Phase Mapping

How v1.1 Operational Maturity phases should address these pitfalls.

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Coverage theater | Testing | Mutation testing passes, assertion effectiveness reviews |
| Flaky tests | Testing (first priority) | Full test suite runs 10x without failures, fix pre-existing flaky test |
| Telemetry correlation breaking | Observability | TraceId survives PowerShell boundary, unified log queries work |
| Premature performance optimization | Performance | Profiling data before optimization, real-world workload benchmarks |
| Over-engineered error catalogs | Error UX | Usability testing with operators, recovery steps for each error |
| Non-deterministic performance testing | Performance | BenchmarkDotNet with statistical significance, CI isolation |
| Logging spam and missing context | Observability | SerilogAnalyzer passes, structured property extraction works |
| Testing implementation instead of behavior | Testing | Refactoring doesn't break tests, black-box test patterns |
| Memory leaks in tests | Testing | Memory profiling in CI, IAsyncLifetime implemented everywhere |
| Breaking determinism | All phases | Determinism tests compare outputs, no wall-clock timestamps in artifacts |
| Ignoring offline constraints | Observability | Application works without internet, local telemetry sinks |
| Test suite performance | Testing | Full suite < 30min, incremental tests run in < 5min |
| Missing edge case coverage | Testing | Property-based testing, fuzzing, real STIG data in fixtures |

---

## Sources

### Testing and Coverage
- [Web search results: Adding test coverage to existing C# .NET application pitfalls 2026](https://developer.microsoft.com/en-us/microsoft-edge/blog/adding-test-coverage-to-existing-csharp-dotnet-application-common-pitfalls-mistakes-2026/) - **MEDIUM confidence**
- [xUnit Shared Context documentation](https://xunit.net/docs/shared-context) - **HIGH confidence**
- [Martin Fowler: Unit Testing](https://martinfowler.com/bliki/UnitTest.html) - **HIGH confidence**
- [Kent Beck: Test Design Framing](https://kentbeck.com/test-design-framing/) - **MEDIUM confidence**
- [Flaky test prevention research](https://microsoft.github.io/code-with-engineering-people/learning-resources/testing/flaky-tests/) - **MEDIUM confidence**

### Observability and Logging
- [Serilog Structured Data Cookbook](https://github.com/serilog/serilog/wiki/Structured-Data-Cookbook) - **HIGH confidence**
- [Microsoft Azure: Telemetry correlation](https://learn.microsoft.com/en-us/azure/azure-monitor/app/correlation) - **HIGH confidence**
- [Martin Fowler: Production Ready Logging](https://martinfowler.com/articles/production-ready-logging.html) - **MEDIUM confidence**
- [Enterprise telemetry correlation issues 2026](https://learn.microsoft.com/en-us/azure/azure-monitor/overview) - **MEDIUM confidence**

### Performance and Benchmarking
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/articles/guides/) - **HIGH confidence**
- [.NET 8 Performance Improvements](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/) - **HIGH confidence**
- [.NET Memory Management and GC](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/) - **HIGH confidence**
- [Memory profiling BenchmarkDotNet .NET 8](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debugging-high-memory) - **HIGH confidence**
- [Premature optimization pitfalls 2026](https://dev.to/garrypassarella/premature-optimization-performance-pitfalls-2026) - **MEDIUM confidence**

### Error UX and Recovery
- [Nielsen Norman Group: Error Message Guidelines](https://www.nngroup.com/articles/error-message-guidelines/) - **MEDIUM confidence**
- [HarmonyOS App Recovery Guidelines 2026](https://developer.harmonyos.com/en-us/docs/ui/ux-error-recovery) - **MEDIUM confidence**
- [IBM Business Automation Workflow Error Handling](https://www.ibm.com/docs/en/baw/21.x.x?topic=handling-error-events) - **MEDIUM confidence**

### Compliance and Determinism
- [STIGForge Project Context](/.planning/PROJECT.md) - **HIGH confidence**
- [Reproducible Builds](https://reproducible-builds.org/) - **MEDIUM confidence**
- [OpenTelemetry OTLP Specification](https://opentelemetry.io/docs/reference/specification/protocol/otlp/) - **MEDIUM confidence**

---
*Pitfalls research for: Adding operational maturity (testing, observability, performance, error UX) to existing offline-first Windows compliance application*
*Researched: 2026-02-22*

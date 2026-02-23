# Requirements: STIGForge Next v1.1 Operational Maturity

**Defined:** 2026-02-22
**Core Value:** Produce deterministic, defensible compliance outcomes with strict control mapping and complete evidence packaging, without requiring internet access.

## v1.1 Requirements

Requirements for Operational Maturity milestone. Each maps to roadmap phases.

### Testing

- [ ] **TEST-01**: Test suite runs reliably without flaky failures (fix BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory)
- [ ] **TEST-02**: 80% line coverage on critical assemblies (Build, Apply, Verify, Infrastructure)
- [ ] **TEST-03**: Branch coverage reporting available in CI pipeline
- [ ] **TEST-04**: Coverage gates enforced in CI (block PRs that drop below threshold)
- [ ] **TEST-05**: Tests categorized by speed (unit vs integration) for pipeline efficiency
- [ ] **TEST-06**: Mutation testing validates test quality (not just coverage numbers)

### Observability

- [x] **OBSV-01**: Structured logging with correlation IDs for trace correlation
- [ ] **OBSV-02**: Mission-level tracing spans Build → Apply → Verify → Prove lifecycle
- [ ] **OBSV-03**: Performance metrics collected (startup time, mission duration, memory usage)
- [x] **OBSV-04**: Log levels configurable per environment (debug vs production)
- [ ] **OBSV-05**: Debug export bundles create portable diagnostics for offline support
- [ ] **OBSV-06**: Trace IDs propagate across PowerShell process boundaries

### Performance

- [ ] **PERF-01**: Cold startup time baseline established (< 3s target)
- [ ] **PERF-02**: Warm startup time baseline established (< 1s target)
- [ ] **PERF-03**: Mission duration baselines documented for each mission type
- [ ] **PERF-04**: Scale testing validates 10K+ rule processing without OOM
- [ ] **PERF-05**: Memory profile baseline established with leak detection
- [ ] **PERF-06**: I/O bottlenecks identified and documented

### Error Ergonomics

- [ ] **ERRX-01**: Human-readable error messages replace raw exception output
- [ ] **ERRX-02**: Recovery guidance included in error output ("Next steps")
- [x] **ERRX-03**: Structured error codes enable machine-readable error cataloging
- [ ] **ERRX-04**: Error catalog documentation searchable by error code
- [ ] **ERRX-05**: Graceful degradation allows partial mission completion
- [ ] **ERRX-06**: Unified error UX across CLI and WPF surfaces

## v1.2 Requirements

Deferred to next milestone. Tracked but not in current roadmap.

### Advanced Observability

- **OBSV-07**: Deterministic replay from captured traces
- **OBSV-08**: Compliance-specific metrics (STIG coverage, rule pass rate)

### Advanced Error Handling

- **ERRX-07**: Self-service remediation for common error patterns
- **ERRX-08**: Automated rollback triggered by critical failures

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Real-time streaming telemetry | Offline-first architecture conflicts with streaming requirements |
| Auto-fix without confirmation | Violates safety invariants for compliance tooling |
| Generic catch-all exception handlers | Masks root cause, prevents debugging |
| 100% test coverage mandate | Diminishing returns, prefer 80% + critical path focus |
| Centralized cloud observability | Conflicts with offline-first requirement |
| AI-assisted error triage | Requires data collection and model training (v2+) |
| Predictive failure detection | ML-based, needs historical data (v2+) |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| TEST-01 | Phase 11 | Pending |
| TEST-02 | Phase 14 | Pending |
| TEST-03 | Phase 14 | Pending |
| TEST-04 | Phase 14 | Pending |
| TEST-05 | Phase 11 | Pending |
| TEST-06 | Phase 14 | Pending |
| OBSV-01 | Phase 11 | Complete |
| OBSV-02 | Phase 12 | Pending |
| OBSV-03 | Phase 13 | Pending |
| OBSV-04 | Phase 11 | Complete |
| OBSV-05 | Phase 12 | Pending |
| OBSV-06 | Phase 12 | Pending |
| PERF-01 | Phase 13 | Pending |
| PERF-02 | Phase 13 | Pending |
| PERF-03 | Phase 13 | Pending |
| PERF-04 | Phase 13 | Pending |
| PERF-05 | Phase 13 | Pending |
| PERF-06 | Phase 13 | Pending |
| ERRX-01 | Phase 15 | Pending |
| ERRX-02 | Phase 15 | Pending |
| ERRX-03 | Phase 11 | Complete |
| ERRX-04 | Phase 15 | Pending |
| ERRX-05 | Phase 15 | Pending |
| ERRX-06 | Phase 15 | Pending |

**Coverage:**
- v1.1 requirements: 24 total
- Mapped to phases: 24
- Unmapped: 0 ✓

---
*Requirements defined: 2026-02-22*
*Last updated: 2026-02-23 after 11-03 completion*

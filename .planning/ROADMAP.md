# Roadmap: STIGForge Next

## Milestones

- [x] **v1.0 STIGForge Next** - Phases 1-5, 8-9 (shipped 2026-02-22)
- [ ] **v1.1 Operational Maturity** - Phases 11-15 (planned)

## Phases

<details>
<summary>v1.0 STIGForge Next (Phases 1-5, 8-9) - SHIPPED 2026-02-22</summary>

- [x] Phase 1: Canonical Ingestion Contracts (4/4 plans) - completed 2026-02-22
- [x] Phase 2: Policy Scope and Safety Gates (5/5 plans) - completed 2026-02-22
- [x] Phase 3: Deterministic Mission Execution Core (5/5 plans) - completed 2026-02-22
- [x] Phase 4: Human Resolution and Evidence Continuity (4/4 plans) - completed 2026-02-22
- [x] Phase 5: Proof Packaging, Fleet-lite, and Integrity (4/4 plans) - completed 2026-02-22
- [x] Phase 8: Canonical Model Completion (4/4 plans) - completed 2026-02-22
- [x] Phase 9: Phase 3 Verification (4/4 plans) - completed 2026-02-22

**Total:** 30 plans across 7 phases

**Key accomplishments:**
- Canonical ingestion pipeline (STIG/SCAP/GPO/LGPO/ADMX with dedupe)
- Profile-based scope filtering and safety gates
- Deterministic bundle compiler with strict per-STIG SCAP mapping
- Multi-backend apply with convergence tracking
- Manual wizard with reusable answer files
- CKL/POA&M/eMASS export packages
- Hash-chained integrity verification

</details>

### v1.1 Operational Maturity (Planned)

**Milestone Goal:** Harden STIGForge Next with production-grade test coverage, observability, performance optimization, and error ergonomics.

- [x] **Phase 11: Foundation and Test Stability** - Fix flaky tests and establish telemetry/error infrastructure (completed 2026-02-23)
- [ ] **Phase 12: Observability Integration** - Mission tracing, correlation, and debug export capability
- [ ] **Phase 13: Performance Baselining** - Startup time, mission duration, scale testing, memory profile
- [ ] **Phase 14: Test Coverage Expansion** - 80% coverage, mutation testing, CI enforcement
- [ ] **Phase 15: Error UX Integration** - Human-readable messages, recovery guidance, unified UX

## Phase Details

### Phase 11: Foundation and Test Stability
**Goal**: Establish reliable test infrastructure and foundation services for observability and error handling
**Depends on**: Phase 9 (v1.0 complete)
**Requirements**: TEST-01, TEST-05, OBSV-01, OBSV-04, ERRX-03
**Success Criteria** (what must be TRUE):
  1. Test suite runs to completion without flaky failures (BuildHost test passes consistently)
  2. Tests are categorized by speed, enabling faster feedback on unit tests
  3. Structured logs include correlation IDs that enable trace correlation
  4. Log verbosity is configurable between debug and production environments
  5. Error codes are structured and machine-readable for cataloging
**Plans**: 4 plans

Plans:
- [x] 11-01-PLAN.md - Fix flaky test and establish test categorization (TEST-01, TEST-05)
- [x] 11-02-PLAN.md - Create correlation enricher and logging configuration (OBSV-01, OBSV-04)
- [x] 11-03-PLAN.md - Create structured error code infrastructure (ERRX-03)
- [x] 11-04-PLAN.md - Wire observability into CLI and WPF hosts (OBSV-01, OBSV-04 integration)

### Phase 12: Observability Integration
**Goal**: Enable end-to-end mission observability with tracing, correlation, and offline diagnostics
**Depends on**: Phase 11 (structured logging foundation)
**Requirements**: OBSV-02, OBSV-05, OBSV-06
**Success Criteria** (what must be TRUE):
  1. Mission lifecycle (Build -> Apply -> Verify -> Prove) emits trace spans that can be correlated
  2. Trace IDs propagate across PowerShell process boundaries during mission execution
  3. Debug export bundles can be created and contain all diagnostics needed for offline support
**Plans**: TBD

### Phase 13: Performance Baselining
**Goal**: Establish documented performance baselines and validate scale handling
**Depends on**: Phase 12 (telemetry infrastructure for measurement)
**Requirements**: OBSV-03, PERF-01, PERF-02, PERF-03, PERF-04, PERF-05, PERF-06
**Success Criteria** (what must be TRUE):
  1. Cold startup time baseline is documented and under 3 seconds
  2. Warm startup time baseline is documented and under 1 second
  3. Mission duration baselines are documented for each mission type (Build, Apply, Verify, Prove)
  4. System processes 10K+ rules without out-of-memory errors
  5. Memory profile baseline is documented with leak detection capability
  6. I/O bottlenecks are identified and documented for future optimization
**Plans**: TBD

### Phase 14: Test Coverage Expansion
**Goal**: Achieve 80% test coverage with quality gates enforced in CI
**Depends on**: Phase 11 (reliable test infrastructure)
**Requirements**: TEST-02, TEST-03, TEST-04, TEST-06
**Success Criteria** (what must be TRUE):
  1. Critical assemblies (Build, Apply, Verify, Infrastructure) achieve 80% line coverage
  2. Branch coverage reports are available in CI pipeline output
  3. CI blocks PRs that drop coverage below the threshold
  4. Mutation testing validates that tests catch real bugs, not just hit coverage targets
**Plans**: TBD

### Phase 15: Error UX Integration
**Goal**: Provide human-friendly error experiences with recovery guidance across CLI and WPF
**Depends on**: Phase 11 (error codes infrastructure)
**Requirements**: ERRX-01, ERRX-02, ERRX-04, ERRX-05, ERRX-06
**Success Criteria** (what must be TRUE):
  1. Operators see human-readable error messages instead of raw exception output
  2. Error output includes actionable "Next steps" for recovery
  3. Error catalog documentation is searchable by error code
  4. Missions can complete partially when non-critical errors occur (graceful degradation)
  5. Error experience is consistent across CLI and WPF interfaces
**Plans**: TBD

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Canonical Ingestion Contracts | v1.0 | 4/4 | Complete | 2026-02-22 |
| 2. Policy Scope and Safety Gates | v1.0 | 5/5 | Complete | 2026-02-22 |
| 3. Deterministic Mission Execution Core | v1.0 | 5/5 | Complete | 2026-02-22 |
| 4. Human Resolution and Evidence Continuity | v1.0 | 4/4 | Complete | 2026-02-22 |
| 5. Proof Packaging, Fleet-lite, and Integrity | v1.0 | 4/4 | Complete | 2026-02-22 |
| 8. Canonical Model Completion | v1.0 | 4/4 | Complete | 2026-02-22 |
| 9. Phase 3 Verification | v1.0 | 4/4 | Complete | 2026-02-22 |
| 11. Foundation and Test Stability | 4/4 | Complete    | 2026-02-23 | 2026-02-23 |
| 12. Observability Integration | v1.1 | 0/TBD | Not started | - |
| 13. Performance Baselining | v1.1 | 0/TBD | Not started | - |
| 14. Test Coverage Expansion | v1.1 | 0/TBD | Not started | - |
| 15. Error UX Integration | v1.1 | 0/TBD | Not started | - |

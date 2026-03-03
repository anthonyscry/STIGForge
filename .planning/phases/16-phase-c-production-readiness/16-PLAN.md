---
phase: 16-phase-c-production-readiness
artifact: plan
type: implementation-plan
status: in-progress
last_updated: 2026-03-03
---

# Phase C: Production Readiness Plan

## Overview

**Goal:** Complete production deployment readiness features focusing on scanner correlation (ACAS/Nessus), STIG Viewer integration, and continuous compliance monitoring. Build upon v1.1 core + Phase B ship-readiness.

**Current State:**
- ✅ Drift Detection — COMPLETE (422 lines, 8 tests)
- ✅ Rollback — COMPLETE (474 lines, 6 tests)
- ✅ GPO Conflicts — COMPLETE (3 tests)
- ✅ ACAS Correlation — COMPLETE (210 lines, fully implemented)
- ✅ Nessus Import — COMPLETE (183 lines, fully implemented)
- ✅ CKL Import/Export — COMPLETE (236 lines, CklImporter + CklExporter)
- ✅ eMASS Package Gen — COMPLETE (646 lines, fully implemented)
- ✅ Continuous Compliance Agent — COMPLETE (190 lines, BackgroundService)

**Exit Criteria:** All 6 CLI commands functional with full test coverage, DI wired, CI passing.

---

## Task Breakdown

### Wave 1: Scanner Correlation (ACAS/Nessus)
**Dependencies:** None (can start immediately)
**Parallel Tasks:** 2

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-01 | Implement AcasCorrelationService | Parse ACAS .nessus XML, correlate findings to STIG rules | SKELETON → COMPLETE | 4h |
| C-02 | Implement NessusImporter | Import Nessus scans, extract CVE-to-VulnID mapping | SKELETON → COMPLETE | 4h |
| C-03 | Add CLI commands: `acas-import`, `nessus-import` | Wire to PhaseCCommandService, add to CliReference | NOT STARTED | 2h |
| C-04 | Unit tests for ACAS/Nessus services | 80% coverage, mock XML parsing | NOT STARTED | 3h |
| C-05 | Integration tests | End-to-end import workflows | NOT STARTED | 2h |

**Deliverables:**
- ACAS scan correlation with VulnID mapping
- Nessus CVE-to-control correlation
- CLI commands documented

---

### Wave 2: STIG Viewer Integration (CKL)
**Dependencies:** Wave 1 (optional, can parallel if needed)
**Parallel Tasks:** 2

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-06 | Implement CklSyncService | Bidirectional sync with STIG Viewer .ckl files | SKELETON → COMPLETE | 6h |
| C-07 | Add CLI commands: `ckl-import`, `ckl-export` | Import findings, export results | NOT STARTED | 2h |
| C-08 | CKL merge/resolution logic | Handle conflicts between STIGForge and CKL statuses | NOT STARTED | 4h |
| C-09 | Unit tests for CKL service | Parsing, sync, conflict resolution | NOT STARTED | 3h |

**Deliverables:**
- Import STIG Viewer checklists
- Export results to CKL format
- Bidirectional status sync

---

### Wave 3: eMASS Package Generation
**Dependencies:** Wave 1-2 (uses scan results)
**Parallel Tasks:** 1

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-10 | Implement EmassPackageGenerator | Generate eMASS submission packages (.zip with artifacts) | SKELETON → COMPLETE | 6h |
| C-11 | Add CLI command: `emass-package` | Create submission-ready package | NOT STARTED | 2h |
| C-12 | Package validation | Verify all required artifacts present | NOT STARTED | 2h |
| C-13 | Unit tests | Package generation, validation | NOT STARTED | 2h |

**Deliverables:**
- Automated eMASS package generation
- Validation of submission readiness
- CLI integration

---

### Wave 4: Continuous Compliance Agent
**Dependencies:** Wave 1 (uses drift detection)
**Parallel Tasks:** 1

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-14 | Implement ContinuousComplianceAgent | Background Windows service for periodic checks | SKELETON → COMPLETE | 8h |
| C-15 | Add CLI commands: `agent-install`, `agent-uninstall`, `agent-status` | Service lifecycle management | NOT STARTED | 3h |
| C-16 | Agent configuration | JSON config for schedules, thresholds | NOT STARTED | 2h |
| C-17 | Unit tests | Agent logic, scheduling | NOT STARTED | 3h |

**Deliverables:**
- Windows service for continuous monitoring
- Configurable check schedules
- Drift alerting integration

---

### Wave 5: Integration & Wiring
**Dependencies:** All Waves 1-4
**Parallel Tasks:** 3

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-18 | DI registration | Register all Phase C services in DI container | NOT STARTED | 2h |
| C-19 | CLI command handlers | Wire all Phase C commands to handlers | NOT STARTED | 3h |
| C-20 | Update CliReference.md | Document all new commands | PARTIAL (6 exist) | 2h |
| C-21 | DbBootstrap schema updates | Ensure Drift/Rollback tables initialized | NOT STARTED | 1h |
| C-22 | CI integration | Add Phase C tests to CI workflow | NOT STARTED | 1h |

**Deliverables:**
- All services DI-wired
- All CLI commands functional
- CI passing

---

### Wave 6: Documentation & Verification
**Dependencies:** Wave 5
**Parallel Tasks:** 2

| Task | Description | Status | Est. Effort |
|------|-------------|--------|-------------|
| C-23 | User Guide updates | Document scanner correlation workflows | NOT STARTED | 4h |
| C-24 | Architecture docs | Document Phase C service interactions | NOT STARTED | 2h |
| C-25 | Integration test suite | Full Phase C E2E tests | NOT STARTED | 4h |
| C-26 | Release gate verification | All tests pass, artifacts generated | NOT STARTED | 2h |

**Deliverables:**
- Complete documentation
- Integration tests passing
- Release-ready

---

## Parallel Execution Schedule

```
Day 1-2:  Wave 1 (Scanner Correlation)
          └─→ C-01, C-02 (parallel)
          └─→ C-03, C-04, C-05

Day 2-3:  Wave 2 (CKL) + Wave 3 (eMASS) — parallel
          ├─→ C-06, C-07, C-08, C-09
          └─→ C-10, C-11, C-12, C-13

Day 4:    Wave 4 (Continuous Agent)
          └─→ C-14, C-15, C-16, C-17

Day 5:    Wave 5 (Integration)
          └─→ C-18 through C-22

Day 6:    Wave 6 (Docs & Verification)
          └─→ C-23 through C-26
```

**Total Estimated Effort:** ~70 hours (8-9 days at 8h/day)

---

## Definition of Done

- [ ] All 6 CLI commands functional (`drift-*`, `rollback-*`, `gpo-conflicts`, `acas-import`, `nessus-import`, `ckl-*`, `emass-package`, `agent-*`)
- [ ] All services have 80%+ unit test coverage
- [ ] Integration tests pass (PhaseCCommandFlowTests expanded)
- [ ] CI workflow includes Phase C tests
- [ ] CliReference.md documents all commands
- [ ] User Guide updated with scanner workflows
- [ ] Release gate passes (build + tests + artifacts)

---

## Phase C → Phase D Transition

**Phase D Triggers:**
1. All Phase C tasks complete
2. v1.2 requirements defined (pluggable adapters, bulk simulation per FUT-01/02)
3. Production deployment successful

**Phase D Scope (v1.2 preview):**
- Advanced bulk remediation simulation (FUT-01)
- Pluggable enterprise packaging (FUT-02)
- Direct eMASS API write-back (out of scope for v1.1, reconsider for v1.2)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| ACAS XML format changes | Schema validation with fallback to raw parsing |
| Nessus API rate limits | Batch import with configurable delays |
| CKL format version drift | Support CKL v2.8+ with version detection |
| Windows service permissions | Document admin requirements |
| Database schema conflicts | Add migration check to bootstrap |

---

## Skills Required

- C# / .NET 8
- XML parsing (ACAS/Nessus)
- Windows Service development
- SQLite/Dapper
- PowerShell integration
- xUnit testing

## Categories

- `backend` — Service implementation
- `integration` — CLI wiring, DI registration
- `testing` — Unit/integration tests
- `docs` — User guides, reference docs

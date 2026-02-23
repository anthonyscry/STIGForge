# Roadmap: STIGForge Next (M1-M6)

## Overview

- **Phases:** 6
- **v1 requirements mapped:** 30/30
- **Critical invariants embedded in gates:** strict per-STIG SCAP mapping, deterministic outputs, offline-first readiness

## Phase Summary

| Milestone | Name | Goal | Requirements |
|-----------|------|------|--------------|
| M1 | Foundations and Canonical Contracts | Establish canonical ingestion/model/policy contracts and baseline quality gates | ING-01..03, CORE-01..04 |
| M2 | Pipeline Core (Build/Apply/Verify/Strict Mapping) | Deliver deterministic execution pipeline and strict mapping enforcement | BLD-01..02, APL-01..03, VFY-01..02, MAP-01..02 |
| M3 | Manual and Evidence Loop | Deliver manual wizard + answer files + evidence autopilot/indexing | MAN-01..02, EVD-01..02 |
| M4 | Compliance Export and Audit | Deliver CKL/POA&M/eMASS exports and integrity-verifiable audit/reporting | EXP-01..04, AUD-01..02 |
| M5 | Quarterly Lifecycle (Diff/Rebase/Gate) | Deliver safe quarterly update loop and release-age policy behavior | DIF-01..02, CORE-05 |
| M6 | Fleet and Production Hardening | Deliver multi-host execution and production readiness hardening | FLT-01 |

## Dependency Graph

```text
M1 -> M2 -> M3 -> M4
  \           ^
   \-> M5 ----|

M6 depends on M2 + M4
```

### Sequencing Rationale

1. M1 must lock contracts before pipeline implementation to avoid model churn.
2. M2 must complete strict mapping and deterministic build contracts before downstream manual/export work.
3. M3 uses canonical IDs and verify outputs from M2.
4. M4 depends on stable evidence and status models from M2/M3.
5. M5 can iterate after M1 but is most reliable once M2 mapping contract is implemented.
6. M6 requires mature execution and export behavior from M2/M4.

## Milestone Details and Hard Acceptance Gates

## M1 - Foundations and Canonical Contracts

**Goal:** Build schema-first ingestion/policy foundation.

**Deliverables:**
- Canonical schemas and model docs
- Import classifier and metadata normalization path
- Profile/overlay policy engine baseline
- Initial unit/contract test harness

**Hard Acceptance Gate (must all pass):**
- [ ] Import fixtures parse into valid schema-conformant objects for each artifact type.
- [ ] Every imported artifact includes provenance and hash metadata.
- [ ] Profile + overlay merge is deterministic for repeated runs.
- [ ] `ING-*` and `CORE-*` tests pass in CI.

## M2 - Pipeline Core (Build/Apply/Verify/Strict Mapping)

**Goal:** Deliver core mission execution path with strict mapping contract.

**Deliverables:**
- Deterministic bundle builder + manifest
- Apply preflight and backend orchestration (DSC/GPO/script)
- Verify wrappers for SCAP and Evaluate-STIG
- Strict per-STIG mapping implementation + diagnostics

**Hard Acceptance Gate (must all pass):**
- [ ] Identical input run generates deterministic structure/index/hash behavior per policy.
- [ ] Strict per-STIG mapping tests prove no broad fallback cross-pairing.
- [ ] Ambiguous mapping is review-required, never auto-matched.
- [ ] Offline preflight test passes with no external dependency calls.

## M3 - Manual and Evidence Loop

**Goal:** Replace manual thrash with guided wizard + reusable answers + evidence capture.

**Deliverables:**
- Manual wizard UX with required status/reason paths
- Answer file import/export/reuse
- Evidence recipe runner and control-indexed evidence metadata

**Hard Acceptance Gate (must all pass):**
- [ ] Manual queue only contains unresolved manual controls after policy filtering.
- [ ] Answer file replay reproduces prior responses deterministically.
- [ ] Evidence records always include checksum, timestamp, and provenance.
- [ ] Contract tests validate evidence index linkage per control.

## M4 - Compliance Export and Audit

**Goal:** Produce submission-ready deterministic packages and auditable trails.

**Deliverables:**
- CKL/POA&M exporters
- Deterministic eMASS package builder
- Control-evidence index and hash report generation
- Audit trail writer + verifier

**Hard Acceptance Gate (must all pass):**
- [ ] Export package structure matches contract exactly.
- [ ] `control_evidence_index.csv` resolves all required paths/status fields.
- [ ] Hash verification catches tampering in negative test fixture.
- [ ] Audit verification command returns pass for clean run and fail for altered log.

## M5 - Quarterly Lifecycle (Diff/Rebase/Gate)

**Goal:** Make quarterly updates safe and explainable.

**Deliverables:**
- Pack diff reporting (added/removed/changed)
- Overlay/answer rebase with confidence and review queue
- Release-age automation gate policy implementation

**Hard Acceptance Gate (must all pass):**
- [ ] Diff report correctly classifies fixture change sets.
- [ ] Rebase auto-carry only occurs above confidence threshold.
- [ ] Ambiguous rebases are blocked pending review.
- [ ] New/changed controls respect grace-period gate policy.

## M6 - Fleet and Production Hardening

**Goal:** Add reliable multi-host operation and production-grade quality gates.

**Deliverables:**
- WinRM fleet status/apply/verify orchestration
- Concurrency and per-host result partitioning
- Operational hardening (retry strategy, timeout policy, support bundle quality)

**Hard Acceptance Gate (must all pass):**
- [ ] Fleet runs isolate host failures without corrupting global run state.
- [ ] Per-host export and summary indices remain deterministic.
- [ ] Soak test on representative host set completes within SLO bounds.
- [ ] Release gate passes with no critical regressions.

## Coverage Check

- v1 requirements total: 30
- Mapped requirements: 30
- Unmapped requirements: 0

## First Execution Target

Phase to plan next: **M1**

Command: `/gsd-plan-phase 1`

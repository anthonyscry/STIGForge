# Roadmap: STIGForge Next (Mission Console Reboot)

## Overview

This roadmap delivers an offline-first, deterministic hardening mission console where operators can import quarterly content, run build/apply/verify/manual flows with strict per-STIG SCAP mapping, and produce submission-ready proof artifacts.

## Phases

- [ ] **Phase 1: Canonical Ingestion Contracts** - Import and normalize compliance inputs into stable, versioned canonical records.
- [ ] **Phase 2: Policy Scope and Safety Gates** - Apply deterministic profile/overlay/scope decisions with explicit review and release-age protections.
- [ ] **Phase 3: Deterministic Build and Apply Core** - Compile repeatable mission bundles and execute guarded enforcement workflows.
- [ ] **Phase 4: Verification and Strict SCAP Association** - Normalize scanner outputs and enforce no-broad-fallback per-STIG mapping invariants.
- [ ] **Phase 5: Human Resolution and Update Rebase** - Close unresolved controls and carry quarterly updates forward safely.
- [ ] **Phase 6: Proof Packaging and Fleet Integrity** - Export deterministic submission artifacts with audit integrity and fleet-lite execution summaries.

## Phase Details

### Phase 1: Canonical Ingestion Contracts
**Goal**: Operators can ingest raw/compressed sources and get deterministic canonical control data with versioned schemas and provenance.
**Depends on**: Nothing (first phase)
**Requirements**: ING-01, ING-02, CORE-01, CORE-02
**Success Criteria** (what must be TRUE):
  1. Operator can import STIG/SCAP/GPO/LGPO/ADMX inputs and see deterministic classification confidence plus dedupe outcomes.
  2. Imported pack metadata shows pack identity, benchmark/version/date details, source label, applicability tags, and SHA-256 manifest entries.
  3. Operator can inspect canonical `ControlRecord` entries that include provenance and external ID mappings for each normalized control.
  4. Contract tests pass for all required schemas (`ContentPack`, `ControlRecord`, `Profile`, `Overlay`, `BundleManifest`, `VerificationResult`, `EvidenceRecord`, `ExportIndexEntry`).
**Plans**: 4 plans
Plans:
- [ ] 01-01-PLAN.md - Close initial deterministic hash and overlay merge verification blockers.
- [ ] 01-02-PLAN.md - Restore deterministic directory import hashing and persisted dedupe.
- [ ] 01-03-PLAN.md - Fix overlay editor rule selection UX and `Overlay.Overrides` persistence parity.
- [ ] 01-04-PLAN.md - Wire overlay merge artifacts and apply-time NotApplicable filtering.

### Phase 2: Policy Scope and Safety Gates
**Goal**: Operators can make deterministic policy and scope decisions that are explainable, reviewable, and safe for new content.
**Depends on**: Phase 1
**Requirements**: POL-01, POL-02, SCOPE-01, SCOPE-02, SAFE-01
**Success Criteria** (what must be TRUE):
  1. Operator can apply profile knobs (`new_rule_grace_days`, confidence thresholds, automation guardrails) and get the same decision outcome on repeated runs.
  2. Overlay conflicts always resolve with deterministic precedence and a report that explains winning vs overridden values.
  3. Scope filtering supports `classified`, `unclassified`, and `mixed` modes, and auto-NA output honors confidence thresholds deterministically.
  4. Ambiguous scope decisions are routed to a review queue and `na_scope_filter_report.csv` is emitted with traceable reasoning.
  5. Auto-apply is blocked for newly changed controls until release-age grace and trusted-mapping criteria are met.
**Plans**: TBD

### Phase 3: Deterministic Build and Apply Core
**Goal**: Operators can build deterministic mission bundles and run safe apply workflows with reboot-aware convergence.
**Depends on**: Phase 2
**Requirements**: BLD-01, APL-01, APL-02
**Success Criteria** (what must be TRUE):
  1. Build produces a deterministic bundle tree containing `Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, and `Manifest/`.
  2. Re-running build with identical inputs yields byte-stable manifest/checksum outcomes under normalized deterministic rules.
  3. Apply preflight blocks unsafe runs (elevation, compatibility, reboot state, PowerShell safety) with explicit failure reasons.
  4. Operator can complete apply using PowerSTIG/DSC primary path, optional GPO/LGPO path, or script fallback with reboot-aware convergence.
**Plans**: TBD

### Phase 4: Verification and Strict SCAP Association
**Goal**: Operators can run verification and trust that per-STIG SCAP mapping remains strict, deterministic, and auditable.
**Depends on**: Phase 3
**Requirements**: VER-01, MAP-01
**Success Criteria** (what must be TRUE):
  1. Operator can run SCAP/SCC and Evaluate-STIG verification and receive normalized canonical results with raw-source provenance.
  2. Each verification result links back to canonical controls without cross-STIG broad fallback behavior.
  3. Strict per-STIG mapping rules pass tests for benchmark overlap priority, deterministic tie-breakers, and strict fallback tags only.
**Plans**: TBD

### Phase 5: Human Resolution and Update Rebase
**Goal**: Operators can resolve manual controls and safely carry policy/answers through quarterly content changes.
**Depends on**: Phase 4
**Requirements**: MAN-01, EVD-01, REB-01, REB-02
**Success Criteria** (what must be TRUE):
  1. Manual wizard only shows unresolved controls and stores reusable answer files that can be re-applied later.
  2. Evidence autopilot captures control-level artifacts with metadata and checksums suitable for audit review.
  3. Pack diff reports deterministic add/remove/text/mapping deltas between quarterly releases.
  4. Rebase auto-carries only high-confidence matches and flags uncertain carries for explicit operator review.
**Plans**: TBD

### Phase 6: Proof Packaging and Fleet Integrity
**Goal**: Operators can produce submission-ready exports and integrity proofs across single-host and fleet-lite operations.
**Depends on**: Phase 5
**Requirements**: EXP-01, FLT-01, AUD-01
**Success Criteria** (what must be TRUE):
  1. Export generates deterministic CKL, standalone POA&M, and eMASS package artifacts with complete index/checksum/attestation data.
  2. Fleet-lite WinRM apply/verify runs produce host-separated artifacts with a unified summary for operators.
  3. Critical actions are hash-chained and verifiable, and package-level SHA-256 manifests validate end-to-end integrity.
  4. MVP acceptance gate is satisfiable end-to-end from import through export using repeatable offline execution.
**Plans**: TBD

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Canonical Ingestion Contracts | 0/1 | Planned | - |
| 2. Policy Scope and Safety Gates | 0/TBD | Not started | - |
| 3. Deterministic Build and Apply Core | 0/TBD | Not started | - |
| 4. Verification and Strict SCAP Association | 0/TBD | Not started | - |
| 5. Human Resolution and Update Rebase | 0/TBD | Not started | - |
| 6. Proof Packaging and Fleet Integrity | 0/TBD | Not started | - |

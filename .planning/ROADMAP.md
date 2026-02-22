# Roadmap: STIGForge Next (Mission Console Reboot)

## Overview

This roadmap delivers deterministic offline compliance operations end-to-end: operators ingest and normalize quarterly content, enforce policy and safety gates, execute build/apply/verify workflows with strict per-STIG mapping, resolve manual/evidence/rebase workflows, and produce defensible export artifacts.

## Phases

- [x] **Phase 1: Canonical Ingestion Contracts** - Import and normalize compliance sources into stable versioned canonical records. (completed 2026-02-22)
- [x] **Phase 2: Policy Scope and Safety Gates** - Make deterministic scope and automation decisions with explicit review-required handling. (completed 2026-02-22)
- [ ] **Phase 3: Deterministic Mission Execution Core** - Build deterministic bundles and run apply/verify with strict per-STIG mapping invariants.
- [ ] **Phase 4: Human Resolution and Evidence Continuity** - Close unresolved controls and carry answers/overlays forward through pack updates.
- [ ] **Phase 5: Proof Packaging, Fleet-lite, and Integrity** - Produce deterministic submission packages with fleet-lite summaries and cryptographic audit proof.

## Phase Details

### Phase 1: Canonical Ingestion Contracts
**Goal**: Operators can import source content and get canonical, versioned control data with provenance.
**Depends on**: Nothing (first phase)
**Requirements**: ING-01, ING-02, CORE-01, CORE-02
**Success Criteria** (what must be TRUE):
  1. Operator can import compressed or raw STIG/SCAP/GPO/LGPO/ADMX content and see deterministic classification confidence and dedupe outcomes.
  2. Imported packs expose required metadata (identity, benchmark release/version/date, source labels, applicability tags, hash manifest) for audit review.
  3. Operator can inspect canonical `ControlRecord` entries with provenance links and external ID mappings for each normalized control.
  4. Canonical schemas are versioned and published for all required contracts so downstream phases consume stable data structures.
**Plans**: 4 plans
Plans:
- [x] 01-01-PLAN.md - Establish MissionRun timeline contracts and append-only SQLite ledger wiring.
- [ ] 01-02-PLAN.md - Add deterministic import staging transitions before content commit.
- [ ] 01-03-PLAN.md - Wire orchestration/apply evidence capture with deterministic rerun lineage.
- [ ] 01-04-PLAN.md - Expose mission timeline visibility in CLI and WPF mission surfaces.

### Phase 2: Policy Scope and Safety Gates
**Goal**: Operators can apply deterministic policy and scope decisions safely before execution touches hosts.
**Depends on**: Phase 1
**Requirements**: POL-01, POL-02, SCOPE-01, SCOPE-02, SAFE-01
**Success Criteria** (what must be TRUE):
  1. Operator can configure policy knobs (`new_rule_grace_days`, confidence thresholds, automation guardrails) and repeated runs yield identical gate decisions.
  2. Overlay precedence and conflict outcomes are deterministic and reportable, including why winning values beat overridden values.
  3. Scope filtering supports `classified`, `unclassified`, and `mixed` modes, including deterministic confidence-threshold auto-NA behavior.
  4. Ambiguous scope decisions route to a review queue and `na_scope_filter_report.csv` is emitted with traceable rationale.
  5. New or changed controls are blocked from auto-apply until release-age grace and trusted mapping criteria are satisfied.
**Plans**: 5 plans
Plans:
- [x] 02-01-PLAN.md — Profile CLI CRUD commands and profile validation service.
- [x] 02-02-PLAN.md — Extend ClassificationScopeService for Unclassified and Mixed mode evaluation.
- [x] 02-03-PLAN.md — Overlay conflict detection service and overlay_conflict_report.csv in BundleBuilder.
- [x] 02-04-PLAN.md — CLI commands for overlay diff and bundle review-queue inspection.
- [x] 02-05-PLAN.md — WPF profile editor, guided run review queue step, and break-glass dialog.

### Phase 3: Deterministic Mission Execution Core
**Goal**: Operators can run the core mission loop (build, apply, verify) deterministically with strict SCAP association rules.
**Depends on**: Phase 2
**Requirements**: BLD-01, APL-01, APL-02, VER-01, MAP-01
**Success Criteria** (what must be TRUE):
  1. Build outputs deterministic `Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, and `Manifest/` bundle trees for identical inputs.
  2. Apply preflight blocks unsafe execution states (privilege, compatibility, reboot, PowerShell safety) with explicit operator-visible failure reasons.
  3. Operator can run apply through supported backends (PowerSTIG/DSC primary, optional GPO/LGPO, script fallback) with reboot-aware convergence tracking.
  4. Verification adapters normalize scanner outputs into canonical results with provenance retained back to raw tool artifacts.
  5. Per-STIG SCAP mapping invariants hold with benchmark-overlap priority and no broad cross-STIG fallback behavior.
**Plans**: TBD

### Phase 4: Human Resolution and Evidence Continuity
**Goal**: Operators can resolve manual controls, collect evidence, and safely rebase updates between content releases.
**Depends on**: Phase 3
**Requirements**: MAN-01, EVD-01, REB-01, REB-02
**Success Criteria** (what must be TRUE):
  1. Manual workflow shows only unresolved controls and saves reusable answer files that can be reapplied in later runs.
  2. Evidence autopilot captures control-level artifacts with metadata and checksums suitable for downstream audit packaging.
  3. Pack diff deterministically reports add/remove/text/mapping deltas between source versions.
  4. Rebase carries only high-confidence matches automatically and routes uncertain carries for explicit operator review.
**Plans**: TBD

### Phase 5: Proof Packaging, Fleet-lite, and Integrity
**Goal**: Operators can produce defensible export packages and integrity proofs for single-host and fleet-lite operations.
**Depends on**: Phase 4
**Requirements**: EXP-01, FLT-01, AUD-01
**Success Criteria** (what must be TRUE):
  1. Export produces CKL, standalone POA&M, and deterministic eMASS package artifacts with complete indices, checksums, and attestations.
  2. Fleet-lite WinRM runs generate host-separated apply/verify artifacts and a unified operator summary.
  3. Critical actions are hash-chained and verifiable, and package-level SHA-256 manifests validate end-to-end integrity.
**Plans**: TBD

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Canonical Ingestion Contracts | 4/4 | Complete   | 2026-02-22 |
| 2. Policy Scope and Safety Gates | 5/5 | Complete   | 2026-02-22 |
| 3. Deterministic Mission Execution Core | 0/TBD | Not started | - |
| 4. Human Resolution and Evidence Continuity | 0/TBD | Not started | - |
| 5. Proof Packaging, Fleet-lite, and Integrity | 0/TBD | Not started | - |

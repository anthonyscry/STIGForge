# Requirements: STIGForge Next (v1)

**Defined:** 2026-02-20
**Milestone:** vNext
**Status:** Active (new-project bootstrap)

## Core Value

Operators can run a deterministic offline hardening mission with reusable manual evidence and defensible submission artifacts.

## Requirement Set

### Ingestion and Canonical Contracts

- [ ] **ING-01**: Import compressed or raw STIG/SCAP/GPO/LGPO/ADMX sources with confidence-based classification and dedupe.
- [ ] **ING-02**: Persist pack metadata (`pack id/name`, benchmark IDs, release/version/date, source label, hash manifest, applicability tags).
- [ ] **CORE-01**: Normalize all controls into canonical `ControlRecord` with provenance and external ID mapping.
- [ ] **CORE-02**: Version and publish schemas for `ContentPack`, `ControlRecord`, `Profile`, `Overlay`, `BundleManifest`, `VerificationResult`, `EvidenceRecord`, `ExportIndexEntry`.

### Policy, Scope, and Safety Gates

- [ ] **POL-01**: Profile dimensions and policy knobs support deterministic gating (`new_rule_grace_days`, mapping confidence thresholds, automation guardrails).
- [ ] **POL-02**: Overlay precedence and conflict resolution are deterministic and reportable.
- [ ] **SCOPE-01**: Classification filter supports `classified`, `unclassified`, and `mixed` modes with confidence-threshold auto-NA.
- [ ] **SCOPE-02**: Ambiguous scope decisions route to review queue and emit `na_scope_filter_report.csv`.
- [ ] **SAFE-01**: Release-age gate blocks auto-apply for new/changed controls until grace period and trusted mapping criteria are satisfied.

### Build, Apply, Verify, Manual, Evidence

- [ ] **BLD-01**: Deterministic bundle compiler outputs `Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, `Manifest/` tree.
- [ ] **APL-01**: Apply preflight enforces elevation/compatibility/reboot/PowerShell safety checks.
- [ ] **APL-02**: Apply supports PowerSTIG/DSC primary backend, optional GPO/LGPO path, and script fallback with reboot-aware convergence.
- [ ] **VER-01**: Verify wrappers normalize SCAP/SCC and Evaluate-STIG outputs into canonical result model.
- [ ] **MAP-01**: Strict per-STIG SCAP mapping contract is enforced (per-STIG computation, benchmark-overlap primary, strict fallback tags, no broad fallback).
- [ ] **MAN-01**: Manual wizard shows unresolved controls only, with status capture and reusable answer files.
- [ ] **EVD-01**: Evidence autopilot captures control-level artifacts with metadata and checksums.

### Diff/Rebase, Export, Fleet, Integrity

- [ ] **REB-01**: Pack diff highlights add/remove/text/mapping deltas.
- [ ] **REB-02**: Overlay/answer rebase auto-carries high-confidence matches and flags uncertain carries.
- [ ] **EXP-01**: Export produces CKL, standalone POA&M, and deterministic eMASS package with indices/checksums/attestations.
- [ ] **FLT-01**: v1-lite fleet ops support WinRM apply/verify with host-separated artifacts and unified summary.
- [ ] **AUD-01**: Critical actions are hash-chained and verifiable; package-level SHA-256 manifest is complete.

## Acceptance Gate (MVP)

All of the following must pass before milestone close:

- [ ] Quarterly pack import and indexing succeed.
- [ ] Classified scope filtering works with traceable auto-NA reporting.
- [ ] Deterministic build contract validated across repeated runs.
- [ ] At least one apply backend completes successfully.
- [ ] At least one verify backend completes successfully.
- [ ] Manual wizard and evidence autopilot are operational.
- [ ] CKL/POA&M/eMASS exports validate with index + hash integrity.
- [ ] Strict per-STIG SCAP mapping tests enforce no broad fallback behavior.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Direct eMASS API sync/upload | Explicitly excluded for v1 |
| Enterprise GPO management replacement | Beyond v1-lite mission scope |
| Universal perfect vendor mapping | Non-deterministic and out of v1 contract |

## Traceability to Phases

| Requirement | Phase | Status |
|-------------|-------|--------|
| ING-01 | Phase 1 | Pending |
| ING-02 | Phase 1 | Pending |
| CORE-01 | Phase 1 | Pending |
| CORE-02 | Phase 1 | Pending |
| POL-01 | Phase 2 | Pending |
| POL-02 | Phase 2 | Pending |
| SCOPE-01 | Phase 2 | Pending |
| SCOPE-02 | Phase 2 | Pending |
| SAFE-01 | Phase 2 | Pending |
| BLD-01 | Phase 3 | Pending |
| APL-01 | Phase 3 | Pending |
| APL-02 | Phase 3 | Pending |
| VER-01 | Phase 4 | Pending |
| MAP-01 | Phase 4 | Pending |
| MAN-01 | Phase 5 | Pending |
| EVD-01 | Phase 5 | Pending |
| REB-01 | Phase 5 | Pending |
| REB-02 | Phase 5 | Pending |
| EXP-01 | Phase 6 | Pending |
| FLT-01 | Phase 6 | Pending |
| AUD-01 | Phase 6 | Pending |

---
*Last updated: 2026-02-20 from `PROJECT_SPEC.md` vNext bootstrap*

# Requirements: STIGForge Next

**Defined:** 2026-02-19
**Core Value:** Deterministic, defensible compliance outcomes with strict mapping and offline-first execution.

## v1 Requirements

### Content Ingestion

- [ ] **ING-01**: Operator can import STIG/SCAP/GPO/LGPO/ADMX sources and see artifact type confidence with reason codes.
- [ ] **ING-02**: System stores normalized pack metadata (benchmark IDs, release metadata, source labels, hashes, provenance) for every imported artifact.
- [ ] **ING-03**: System rejects malformed/incomplete source artifacts with actionable diagnostics and audit logging.

### Canonical Model and Policy

- [ ] **CORE-01**: System normalizes all controls into canonical `ControlRecord` entries with stable IDs and source provenance.
- [ ] **CORE-02**: Operator can define profile policy (OS, role, classification mode, environment mode, automation mode).
- [ ] **CORE-03**: Operator can apply ordered overlays with deterministic precedence and conflict visibility.
- [ ] **CORE-04**: System auto-marks out-of-scope controls only when policy confidence threshold is met and exports NA scope report rows.
- [ ] **CORE-05**: System enforces release-age gate so new/changed controls stay review-required until grace policy allows auto-apply.

### Build and Apply

- [ ] **BLD-01**: Operator can build deterministic mission bundles with fixed folder contract (`Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, `Manifest/`).
- [ ] **BLD-02**: System generates bundle manifest with SHA-256 hashes for included artifacts.
- [ ] **APL-01**: Apply preflight blocks execution on missing admin rights, OS/role mismatch, pending reboot, missing modules, or host readiness failures.
- [ ] **APL-02**: Apply orchestration supports PowerSTIG/DSC, optional GPO/LGPO, and script fallback with explicit source attribution.
- [ ] **APL-03**: Apply orchestration supports reboot-aware convergence and writes rollback/snapshot artifacts.

### Verify and Mapping

- [ ] **VFY-01**: Operator can run SCAP wrapper and receive normalized verification records with raw artifact linkage.
- [ ] **VFY-02**: Operator can run Evaluate-STIG wrapper and receive normalized verification records with raw artifact linkage.
- [ ] **MAP-01**: System enforces strict per-STIG SCAP mapping invariant (benchmark-first, deterministic tie-break, strict fallback, ambiguity -> review-required).
- [ ] **MAP-02**: Operator can inspect mapping diagnostics showing selection reason and confidence for each STIG/SCAP pair.

### Manual and Evidence

- [ ] **MAN-01**: Operator can complete manual-control wizard with plain-language prompts and required status/reason entries.
- [ ] **MAN-02**: Operator can save and reuse answer files across repeated runs.
- [ ] **EVD-01**: Operator can collect evidence recipes per control (command/export/file/snapshot) with metadata and checksum.
- [ ] **EVD-02**: System auto-indexes evidence per control with stable artifact paths.

### Diff/Rebase

- [ ] **DIF-01**: Maintainer can diff content packs and see added/removed/changed controls and mapping deltas.
- [ ] **DIF-02**: Maintainer can rebase overlays/answers with confidence scoring and explicit review queue for ambiguous carries.

### Export and Audit

- [ ] **EXP-01**: Operator can export CKL package from canonical run state.
- [ ] **EXP-02**: Operator can export standalone POA&M package from canonical run state.
- [ ] **EXP-03**: Operator can export deterministic eMASS package structure with manifest/index folders.
- [ ] **EXP-04**: Export includes `control_evidence_index.csv` and complete hash report for all package files.
- [ ] **AUD-01**: System records audit trail for import, profile, apply, verify, manual, and export actions.
- [ ] **AUD-02**: Operator can run audit integrity verification and get pass/fail diagnostics.

### Fleet (v1-lite)

- [ ] **FLT-01**: Operator can run WinRM fleet status/apply/verify against host list with concurrency controls and per-host outcomes.

## v2 Requirements

- **FLT-02**: Operator can generate batch multi-system eMASS package from fleet runs.
- **EXP-05**: System supports direct eMASS API integration for submission sync.
- **MAN-03**: System generates richer "explain this control" human guidance from canonical data.
- **DIF-03**: System supports historical trend analysis across multiple quarterly rebases.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Broad best-effort SCAP fallback on ambiguity | Violates strict mapping invariant and audit trust model |
| Internet-required dependency resolution during mission execution | Violates offline-first mission requirement |
| Full enterprise GPO authoring platform | Not required for v1 compliance pipeline |
| Direct eMASS API write operations | Explicitly deferred to v2 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| ING-01 | M1 | Pending |
| ING-02 | M1 | Pending |
| ING-03 | M1 | Pending |
| CORE-01 | M1 | Pending |
| CORE-02 | M1 | Pending |
| CORE-03 | M1 | Pending |
| CORE-04 | M1 | Pending |
| CORE-05 | M5 | Pending |
| BLD-01 | M2 | Pending |
| BLD-02 | M2 | Pending |
| APL-01 | M2 | Pending |
| APL-02 | M2 | Pending |
| APL-03 | M2 | Pending |
| VFY-01 | M2 | Pending |
| VFY-02 | M2 | Pending |
| MAP-01 | M2 | Pending |
| MAP-02 | M2 | Pending |
| MAN-01 | M3 | Pending |
| MAN-02 | M3 | Pending |
| EVD-01 | M3 | Pending |
| EVD-02 | M3 | Pending |
| DIF-01 | M5 | Pending |
| DIF-02 | M5 | Pending |
| EXP-01 | M4 | Pending |
| EXP-02 | M4 | Pending |
| EXP-03 | M4 | Pending |
| EXP-04 | M4 | Pending |
| AUD-01 | M4 | Pending |
| AUD-02 | M4 | Pending |
| FLT-01 | M6 | Pending |

**Coverage:**
- v1 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0

---
*Requirements defined: 2026-02-19*
*Last updated: 2026-02-19 after new-project reboot synthesis from PROJECT_SPEC.md*

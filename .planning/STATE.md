# STIGForge Development State

## Current Position

**Phase:** v1.1-release-candidate-go-no-go (finalization)
**Last Completed:** 10-quality-and-release-signal-hardening-02 (trend-signal promotion checks across CI/release/vm workflows)

**Started:** February 9, 2026

Progress: ████████████████████████████████████████████░░░░░░░░░░░░ (92%)

---

## Decisions Accumulated

### Content Format
- **Decision:** Use canonical ControlRecord as single source of truth
- **Rationale:** Normalizes STIG/SCAP/PowerSTIG/GPO into unified format
- **Status:** Implemented (models exist)

### Data Storage
- **Decision:** SQLite with JSON columns for complex objects
- **Rationale:** SQLite for queries, JSON for flexible content serialization
- **Status:** Implemented (SqliteJsonXRepository pattern exists)

### Classification Scope
- **Decision:** Implement classification_scope: classified_only | unclassified_only | both | unknown
- **Rationale:** Classified environments need auto-NA of unclassified-only controls
- **Status:** Implemented (ClassificationScopeService exists)

### Release Age Gate
- **Decision:** New rules default to Review Required, auto-apply after grace period
- **Rationale:** Avoid unsafe auto-remediation immediately after quarterly updates
- **Status:** Implemented (ReleaseAgeGate service exists)

### PowerShell Version
- **Decision:** Target PowerShell 5.1 (not PS 6/Core)
- **Rationale:** Windows Desktop/air-gapped environments may not have PS 6
- **Status:** Implemented (CLI targets PS 5.1, WPF app will use 5.1)

### Offline-First
- **Decision:** All operations must work without internet
- **Rationale:** Air-gapped classified environments
- **Status:** Implemented (content packs local, bundles self-contained)

### LCM Configuration
- **Decision:** Configure LCM before DSC application, reset optionally after
- **Rationale:** Ensures proper reboot behavior and consistency checks, restores original settings
- **Status:** Implemented (LcmService with ConfigureLcm, GetLcmState, ResetLcm)

### Break-Glass Guardrails
- **Decision:** High-risk bypass flags require explicit acknowledgment and specific reason before execution
- **Rationale:** Prevent silent safety bypass and ensure operator intent is explicit for destructive paths
- **Status:** Implemented (CLI apply/orchestrate/build + WPF apply/orchestrate guard semantics)

### Break-Glass Audit Trace
- **Decision:** Every accepted high-risk bypass emits dedicated `break-glass` audit entry with action, bypass type, and reason
- **Rationale:** Preserve tamper-evident accountability for emergency override behavior
- **Status:** Implemented (BuildCommands, MainViewModel.ApplyVerify, BundleOrchestrator)

### Archive Extraction Boundary Enforcement
- **Decision:** ZIP extraction now validates canonical destination paths and blocks writes outside extraction roots before any file write.
- **Rationale:** Prevent path traversal and uncontrolled unpack behavior from untrusted content bundles.
- **Status:** Implemented (ContentPackImporter, ScapBundleParser)

### Hardened XML Parsing Baseline
- **Decision:** OVAL and verify adapter XML entry points now enforce one hardened reader configuration (`DtdProcessing=Prohibit`, `XmlResolver=null`).
- **Rationale:** Ensure unsafe XML constructs fail predictably with actionable diagnostics across import/verify workflows.
- **Status:** Implemented (OvalParser, CklAdapter, EvaluateStigAdapter, ScapResultAdapter)

### Deterministic Security Intelligence Handling
- **Decision:** Security gate now classifies unavailable external intelligence as unresolved findings (review-required) instead of silent pass behavior.
- **Rationale:** Preserve deterministic, auditable outcomes in air-gapped/offline environments without masking uncertainty.
- **Status:** Implemented (Invoke-SecurityGate summary/report + unresolved intelligence model)

### Strict Security Gate Execution Mode
- **Decision:** Strict mode is exposed in release and CI workflows so unresolved findings can be treated as blocking by policy.
- **Rationale:** Allow high-assurance environments to enforce fail-closed behavior while keeping default offline deterministic mode usable.
- **Status:** Implemented (Invoke-ReleaseGate + GitHub workflows strict mode wiring)

### Fail-Closed Mission Completion Gates
- **Decision:** Apply/export completion is blocked when integrity-critical evidence fails (audit chain invalid, required export validation invalid).
- **Rationale:** Mission completion and submission readiness must never report success when integrity evidence is invalid or unavailable.
- **Status:** Implemented (ApplyRunner, EmassExporter, VerifyCommands, MainViewModel.ApplyVerify)

### Resume Context Operator Decision Gate
- **Decision:** Invalid or exhausted reboot resume context stops automated continuation and requires explicit operator decision.
- **Rationale:** Prevent unsafe automatic continuation when reboot recovery evidence is stale or inconsistent.
- **Status:** Implemented (RebootCoordinator + ApplyRunner validation)

### Support Bundle Least-Disclosure Default
- **Decision:** Support bundles redact sensitive metadata and exclude sensitive artifact paths by default, with explicit opt-in for sensitive collection.
- **Rationale:** Troubleshooting bundles must remain portable without leaking secrets/credentials unless operator explicitly accepts risk.
- **Status:** Implemented (SupportBundleBuilder + BundleCommands)

---

## Pending Todos

### High Priority
- [x] Execute `08-upgrade-rebase-operator-workflow-01-PLAN.md`
- [x] Execute `08-upgrade-rebase-operator-workflow-02-PLAN.md`
- [x] Prepare Phase 09 context for WPF parity and recovery UX
- [x] Execute `09-wpf-parity-and-recovery-ux-01-PLAN.md`
- [x] Execute `09-wpf-parity-and-recovery-ux-02-PLAN.md`
- [x] Prepare Phase 10 context for quality and release signal hardening
- [x] Execute `10-quality-and-release-signal-hardening-01-PLAN.md`
- [x] Execute `10-quality-and-release-signal-hardening-02-PLAN.md`
- [x] Run release-gate and package-build evidence generation for v1.1 RC (`phase10-rc`)
- [x] Execute `docs/release/ShipReadinessChecklist.md` for v1.1 release candidate
- [ ] Close manual checklist blockers for functional UAT and upgrade/rollback validation
- [ ] Pin RC commit and regenerate release/package evidence from clean working tree
- [ ] Run `release-package.yml` and `vm-smoke-matrix.yml` for pinned RC commit and archive outputs

### Medium Priority
- [x] Validate requirement traceability remains 100% after Phase 08 updates
- [x] Validate WP-02/WP-03 acceptance criteria for severity and recovery guidance parity
- [x] Validate QA-01/QA-02 gating evidence after Phase 10 Plan 01
- [x] Define v1.1 quality/release signal acceptance thresholds for Phase 10
- [x] Capture initial go/no-go decision record with artifact roots and commit hash
- [ ] Capture final go/no-go decision record after manual and workflow signoff

### Low Priority
- [ ] SCCM packaging integration (v2/v3)
- [ ] Direct eMASS API integration (v2)
- [ ] Full enterprise GPO management platform (v2)

---

## Blockers & Concerns

- Current go/no-go is `NO-GO (temporary)` in `docs/release/GoNoGo-v1.1-rc1.md` until manual checklist and workflow promotion checks complete.
- RC evidence was generated from a dirty working tree; final promotion requires evidence regenerated from a pinned clean commit.

---

## Environment

**Platform:** Windows 10/11, Windows Server 2019
**.NET Version:** .NET 8
**PowerShell Target:** 5.1
**Database:** SQLite
**Git Branch:** main

---

## Session Continuity

**Last session:** 2026-02-09T07:37:00Z
**Stopped at:** Produced initial RC go/no-go decision record (`docs/release/GoNoGo-v1.1-rc1.md`) with temporary NO-GO due outstanding manual validations and workflow-run evidence.
**Resume file:** None

---

## Last Updated

**Date:** February 9, 2026
**Updated By:** OpenCode Executor
**Reason:** Executed checklist audit and recorded initial RC decision with explicit blockers

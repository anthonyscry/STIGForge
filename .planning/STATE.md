# STIGForge Development State

## Current Position

**Phase:** 07-release-readiness-and-compatibility completed
**Last Completed:** 07-release-readiness-and-compatibility-04 (RC checklist + reproducibility + upgrade/rebase gate)

**Started:** February 3, 2026

Progress: ███████████████████████████████████████████████ (100%)

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
- [x] Complete Phase 1: Content Parsing
- [x] Complete Phase 2: Apply Logic
- [x] Complete Phase 3: Verification Integration
- [x] Complete Phase 4: eMASS Export
- [x] Complete Phase 5: Manual Check Wizard (high ROI)

### Medium Priority
- [ ] Complete Phase 6: Diffs & Rebasing
- [ ] Complete Phase 7: WPF App UI
- [ ] Complete Phase 8: Testing & Quality

### Low Priority
- [ ] SCCM packaging integration (v2/v3)
- [ ] Direct eMASS API integration (v2)
- [ ] Full enterprise GPO management platform (v2)

---

## Blockers & Concerns

**None identified**

---

## Environment

**Platform:** Windows 10/11, Windows Server 2019
**.NET Version:** .NET 8
**PowerShell Target:** 5.1
**Database:** SQLite
**Git Branch:** main

---

## Session Continuity

**Last session:** 2026-02-08T22:56:30Z
**Stopped at:** Completed 07-04 with RC playbook/checklist lock, upgrade/rebase release-gate evidence, and reproducibility package artifacts verified.
**Resume file:** None

---

## Last Updated

**Date:** February 8, 2026
**Updated By:** OpenCode Executor
**Reason:** Executed Phase 07 plan 04, verified release/package gates, and closed release-readiness phase

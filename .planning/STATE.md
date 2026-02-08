# STIGForge Development State

## Current Position

**Phase:** 06-security-and-operational-hardening - plan 03 completed, ready for plan 04
**Last Completed:** 06-security-and-operational-hardening-03 (deterministic offline security gate + strict mode CI wiring)

**Started:** February 3, 2026

Progress: █████████████████████████████████████████████░ (99%)

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

**Last session:** 2026-02-08T21:25:13.503Z
**Stopped at:** Completed 06-security-and-operational-hardening-03-PLAN.md
**Resume file:** .planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-04-PLAN.md

---

## Last Updated

**Date:** February 8, 2026
**Updated By:** OpenCode Executor
**Reason:** Phase 06 plan 03 completed with deterministic offline security-gate behavior and strict mode release/CI wiring

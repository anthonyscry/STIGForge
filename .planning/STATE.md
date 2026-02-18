# STIGForge Development State

## Current Position

**Phase:** Not started (defining requirements)
**Plan:** —
**Status:** Defining requirements
**Last activity:** 2026-02-18 — Milestone v1.2 started

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

### Fail-Closed Mission Completion Gates
- **Decision:** Apply/export completion is blocked when integrity-critical evidence fails (audit chain invalid, required export validation invalid).
- **Rationale:** Mission completion and submission readiness must never report success when integrity evidence is invalid or unavailable.
- **Status:** Implemented (ApplyRunner, EmassExporter, VerifyCommands, MainViewModel.ApplyVerify)

### Shared Release Evidence Contract Validator
- **Decision:** Use one deterministic validator (`Test-ReleaseEvidenceContract.ps1`) across CI, release-package, VM smoke, and package-build preflight.
- **Rationale:** Eliminate cross-flow drift and enforce identical blocker semantics (`missing-proof`, `failed-check`, `disabled-check`).
- **Status:** Implemented (Phase 13 Plan 01 workflow + package-build wiring)

---

## Pending Todos

### High Priority
- [ ] Define v1.2 requirements
- [ ] Create v1.2 roadmap

### Low Priority
- [ ] SCCM packaging integration (v2/v3)
- [ ] Direct eMASS API integration (v2)
- [ ] Full enterprise GPO management platform (v2)

---

## Blockers & Concerns

- SCC/verify workflow returning 0 results needs root-cause investigation before export work makes sense.

---

## Environment

**Platform:** Windows 10/11, Windows Server 2019
**.NET Version:** .NET 8
**PowerShell Target:** 5.1
**Database:** SQLite
**Git Branch:** main (v1.2 branches TBD)

---

## Session Continuity

**Last session:** 2026-02-18
**Stopped at:** Starting v1.2 milestone — gathering requirements
**Resume file:** None

---

## Last Updated

**Date:** February 18, 2026
**Updated By:** GSD new-milestone
**Reason:** Started milestone v1.2

# STIGForge Development State

## Current Position

**Phase:** 02-apply-logic - Plan 01 complete, ready for next plan
**Last Completed:** 02-apply-logic-01 (PowerSTIG data file generation)

**Started:** February 3, 2026

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

---

## Pending Todos

### High Priority
- [x] Complete Phase 1: Content Parsing
- [x] Complete Phase 2: Apply Logic
- [ ] Complete Phase 3: Verification Integration
- [ ] Complete Phase 4: eMASS Export
- [ ] Complete Phase 5: Manual Check Wizard (high ROI)

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

## Last Updated

**Date:** February 3, 2026
**Updated By:** GSD Planner
**Reason:** Phase 1 content parsing plans created (3 plans in 2 waves)

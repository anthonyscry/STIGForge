---
phase: 15-phase-b-ship-readiness
artifact: summary
status: complete
last_updated: 2026-03-02
---

# Phase B: Ship-Readiness Features Summary

**Status:** Completed (shipped with v1.1.0 tag, 2026-03-02)  
**Scope:** Post-v1.1 operational maturity features for production deployment readiness

## Overview

Phase B delivered ship-readiness capabilities that bridge the gap between v1.1's core compliance platform and production deployment scenarios. These features focus on UI automation for testing, GAP (Gap Analysis Program) compliance workflows, auto-staging of tools, and enhanced apply logic for enterprise environments.

## Deliverables

### 1. UI Automation Framework (FlaUI-based)

**Status:** ✅ Complete  
**Commit:** e9a2966

- **STIGForge.UiDriver** project providing Playwright-like automation for WPF
  - `UiAppDriver`: Application lifecycle management (launch, attach, close)
  - `UiLocator`: Element discovery by automation ID, name, type
  - Screenshot capture for visual regression testing
- **STIGForge.App.UiTests** project with 6 smoke tests
  - Header button validation
  - All tab navigation coverage
  - Integrated into `vm-smoke-matrix.yml` workflow

**Test Count:** 6 UI smoke tests  
**Artifacts:** Screenshots captured per test

---

### 2. GAP (Gap Analysis Program) Features

**Status:** ✅ Complete  
**Commit:** e9a2966

GAP-1 through GAP-8 compliance workflow enhancements:

| GAP ID | Feature | Implementation |
|--------|---------|----------------|
| GAP-1 | Per-rule Remediation Engine | `RemediationRunner` + handlers for Registry, Audit Policy, Services |
| GAP-2 | Dry-Run Preview | `DryRunCollector` + `DscWhatIfParser` for preview-before-apply |
| GAP-3 | Granular Control Filtering | `ControlFilterService` — filter by Rule ID, severity, category |
| GAP-4 | Compliance Scoring | `ComplianceTrendService` + SQLite repository for trend tracking |
| GAP-6 | Exception/Waiver Lifecycle | `ExceptionWorkflowService` + `ControlException` model |
| GAP-7 | Advanced Security | WDAC, BitLocker, Firewall rule management |
| GAP-8 | STIG Release Monitoring | `StigReleaseMonitorService` for tracking DISA releases |

**New CLI Commands (12):**
- `compliance-score` — Calculate current compliance percentage
- `compliance-trend` — View historical compliance trends
- `exception-create|list|update|revoke` — Exception lifecycle management
- `check-release` — Check for new STIG releases
- `release-notes` — View release notes for STIG updates
- `remediate` / `remediate-list` — Execute per-rule remediation
- `security-wdac|bitlocker|firewall` — Advanced security features

**Test Count:** 15 new unit test files, 5 new integration test files  
**Total Tests:** 552 passing

---

### 3. Auto-Staging and Tool Management

**Status:** ✅ Complete  
**Commit:** e9a2966

**LocalSetupValidator** enhancements:
- Auto-extracts Evaluate-STIG from import ZIPs when tool root is missing
- Bridges the `./import` folder gap for air-gapped deployments
- Validates tool presence before workflow execution

---

### 4. DC Auto-Detection and LGPO/GPO Support

**Status:** ✅ Complete  
**Commit:** 88236cd

**Domain Controller Detection:**
- Detects DC role via NTDS registry key
- Configures PowerSTIG with `OsRole='DC'` instead of 'MS'

**LGPO Staging:**
- Stages LGPO.exe from import ZIPs into `tools/` directory
- Resolves via `AppContext.BaseDirectory`

**GPO Import:**
- `apply_gpo_import` step for DCs
- Runs `Import-GPO` for each domain GPO backup subfolder

---

### 5. WPF UI Theming Overhaul

**Status:** ✅ Complete  
**Commit:** 1088ff2

- Compliance workflow reporting refinements
- Visual consistency improvements
- Workflow status visualization enhancements

---

### 6. PowerSTIG Dependency Bundling (Air-Gapped)

**Status:** ✅ Complete  
**Commits:** 9acf34a, 4653137, d99fb0e..e9a2966

- Auto-installs missing PowerSTIG dependencies at harden time
- Self-healing module staging for air-gapped operation
- Unblocks DSC module files to prevent execution policy rejection
- Treats partial DSC failures as success when LCM applied configuration

---

## Version

**Shipped as:** v1.3.0 (per commit e9a2966)  
**Tagged as:** v1.1.0 (milestone completion tag includes Phase B)

## Documentation Updates

- `docs/CliReference.md`: 50 commands (+12 new), new options on apply-run/orchestrate
- `docs/UserGuide.md`: New workflow sections for GAP features

## Files Changed

62 files, +5,331 lines  
New projects: `STIGForge.UiDriver`, `STIGForge.App.UiTests`

## Verification

- All 552 tests passing
- UI smoke tests integrated into VM matrix
- CLI commands documented and functional
- Air-gapped scenarios tested with dependency bundling

---

*Phase B represents the transition from v1.1 core platform to production-ready deployment with enterprise features, automated testing, and comprehensive compliance workflows.*

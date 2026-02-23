---
phase: 13-mandatory-release-gate-enforcement-and-verification
plan: 01
subsystem: release
tags: [powershell, github-actions, release-gate, fail-closed, qa]

requires:
  - phase: 12-wpf-parity-evidence-promotion-and-verification
    provides: explicit WPF contract signals in upgrade/rebase evidence summaries
provides:
  - shared mandatory release evidence contract validator with typed blocker taxonomy
  - fail-closed workflow enforcement across ci, release-package, and vm smoke promotion surfaces
  - package-build preflight that blocks before archive creation when required evidence is missing, failed, or disabled
affects: [release-workflows, promotion-policy, package-reproducibility]

tech-stack:
  added: []
  patterns: [shared release evidence validator, checklist-first blocker reporting, stop-before-package preflight]

key-files:
  created: [tools/release/Test-ReleaseEvidenceContract.ps1]
  modified: [.github/workflows/ci.yml, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml, tools/release/Invoke-PackageBuild.ps1, docs/release/ReleaseCandidatePlaybook.md]

key-decisions:
  - "Treat run_release_gate=false as a disabled-check blocker for promotion packaging."
  - "Use one deterministic evidence contract validator across CI, release-package, VM, and package-build paths."
  - "Persist blocked reproducibility metadata when package preflight fails before archive creation."

patterns-established:
  - "Promotion workflows must call Test-ReleaseEvidenceContract.ps1 against deterministic evidence roots."
  - "Blocker output is checklist-first with what blocked, why blocked, and next command in console and report artifacts."

requirements-completed: [QA-01, QA-02, QA-03]

duration: 5 min
completed: 2026-02-17
---

# Phase 13 Plan 01: Mandatory Release Gate Enforcement and Verification Summary

**Mandatory release-gate evidence enforcement now blocks CI/release/VM/package promotion paths with shared missing-proof/failed-check/disabled-check semantics before packaging artifacts are produced.**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-17T00:44:46Z
- **Completed:** 2026-02-17T00:50:43Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments
- Added `tools/release/Test-ReleaseEvidenceContract.ps1` as the shared fail-closed validator for deterministic release evidence contracts and explicit blocker taxonomy.
- Updated CI, release-package, and VM workflows to enforce the shared validator and fail required proof artifact uploads with `if-no-files-found: error`.
- Hardened `tools/release/Invoke-PackageBuild.ps1` to execute mandatory contract preflight before `Compress-Archive`, persist blocked metadata on failure, and terminate with copy-paste recovery guidance.
- Updated `docs/release/ReleaseCandidatePlaybook.md` with mandatory enforcement semantics, blocker categories, and recovery commands.

## Task Commits

Each task was committed atomically:

1. **Task 1: Create shared mandatory evidence-contract validator with checklist-first blocker reporting** - `5900ac0` (feat)
2. **Task 2: Enforce mandatory contract and disabled-check blocking in CI, release-package, and VM workflows** - `f8fdd91` (feat)
3. **Task 3: Make package build fail closed on mandatory release-gate proof and align operator guidance** - `67fa5eb` (fix)
4. **Task 3 follow-up: Surface blocker taxonomy in package-build terminating error guidance** - `d7e2393` (fix)

## Files Created/Modified
- `tools/release/Test-ReleaseEvidenceContract.ps1` - shared deterministic contract validator and persisted checklist-first blocker report writer.
- `.github/workflows/ci.yml` - contract validator enforcement and required artifact upload failure policy.
- `.github/workflows/release-package.yml` - explicit disabled-check blocker for `run_release_gate=false`, shared contract preflight, required proof upload failure policy.
- `.github/workflows/vm-smoke-matrix.yml` - shared contract preflight for VM evidence roots and required artifact upload failure policy.
- `tools/release/Invoke-PackageBuild.ps1` - mandatory pre-archive contract validation, blocked-state metadata persistence, terminating recovery guidance.
- `docs/release/ReleaseCandidatePlaybook.md` - operator documentation for blocker categories and copy-paste recovery commands.

## Decisions Made
- Enforced `run_release_gate=false` as `[disabled-check]` blocker to remove release-package fail-open bypass.
- Centralized mandatory evidence checks in one validator script to prevent CI/release/VM drift.
- Required package preflight to fail before archive generation and to persist blocked reproducibility metadata for auditability.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added explicit blocker taxonomy to package preflight termination output**
- **Found during:** Task 3 verification
- **Issue:** Package preflight error path did not explicitly surface all blocker taxonomy names in terminating guidance.
- **Fix:** Updated terminating error output in `Invoke-PackageBuild.ps1` to include `missing-proof`, `failed-check`, and `disabled-check` with copy-paste recovery command.
- **Files modified:** tools/release/Invoke-PackageBuild.ps1
- **Verification:** `rg --line-number "Test-ReleaseEvidenceContract\.ps1|missing-proof|failed-check|disabled-check" tools/release/Invoke-PackageBuild.ps1 .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml`
- **Committed in:** d7e2393

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Auto-fix tightened operator-facing blocker diagnostics without scope creep; enforcement scope remained aligned to plan intent.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Ready for `13-mandatory-release-gate-enforcement-and-verification-02-PLAN.md` verification backfill and three-source QA closure reconciliation.
- Promotion surfaces now enforce mandatory fail-closed evidence contract semantics consistently.

## Self-Check: PASSED

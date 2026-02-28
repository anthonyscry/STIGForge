---
phase: 12-wpf-parity-evidence-promotion-and-verification
plan: 02
subsystem: infra
tags: [wpf, release-gate, github-actions, powershell, evidence]

# Dependency graph
requires:
  - phase: 09-wpf-parity-and-recovery-ux
    provides: WPF parity and recovery behavior contracts that promotion evidence must represent
  - phase: 10-quality-and-release-signal-hardening
    provides: upgrade/rebase promotion gating and fail-closed workflow validation patterns
provides:
  - Explicit upgrade/rebase WPF contract signals for workflow, severity parity, and recovery guidance
  - Fail-closed workflow enforcement for explicit WPF contract step presence and success
  - Release package reproducibility linkage keyed to explicit WPF contract evidence
affects: [release, promotion, packaging, wp-requirements]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Fail-closed promotion checks verify explicit summary step names, not only aggregate status
    - Package evidence catalog keys can require both artifact presence and summary-step contract success

key-files:
  created: []
  modified:
    - tools/release/Invoke-ReleaseGate.ps1
    - .github/workflows/ci.yml
    - .github/workflows/release-package.yml
    - .github/workflows/vm-smoke-matrix.yml
    - tools/release/Invoke-PackageBuild.ps1
    - docs/release/UpgradeAndRebaseValidation.md

key-decisions:
  - Promotion workflows must require explicit WPF workflow/severity/recovery step names and success.
  - Package release-gate evidence catalog now includes explicit WPF contract keys linked to summary step validation.

patterns-established:
  - "Explicit contract promotion: represent WP requirement areas as named upgrade/rebase summary steps."
  - "Fail-closed evidence linking: required package evidence keys remain incomplete when required summary-step contracts are missing or failed."

requirements-completed: [WP-01, WP-02, WP-03]

# Metrics
duration: 3 min
completed: 2026-02-16
---

# Phase 12 Plan 02: WPF Parity Evidence Promotion and Verification Summary

**Release-gate, promotion workflows, and package evidence now require explicit WP-01/WP-02/WP-03 contract signals via dedicated upgrade/rebase WPF workflow, severity, and recovery contract identifiers.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-16T23:36:05Z
- **Completed:** 2026-02-16T23:39:14Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Added explicit release-gate contract step emissions for `upgrade-rebase-wpf-workflow-contract`, `upgrade-rebase-wpf-severity-contract`, and `upgrade-rebase-wpf-recovery-contract` and required them in upgrade/rebase summary/report evidence.
- Updated CI, release-package, and VM smoke workflows to fail closed unless each explicit WPF contract step is present and succeeded.
- Extended package reproducibility evidence linkage and release documentation to require and describe explicit WPF contract identifiers without naming drift.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add explicit WPF parity contract signals to release-gate summary/report outputs** - `8d44970` (feat)
2. **Task 2: Enforce explicit WPF contract evidence in CI, release-package, and VM promotion flows** - `2a8be92` (feat)
3. **Task 3: Wire package evidence catalog and release docs to explicit WPF contract signals** - `c18ba24` (feat)

**Plan metadata:** pending

## Files Created/Modified

- `tools/release/Invoke-ReleaseGate.ps1` - emits explicit WPF contract steps and requires them in summary/report evidence arrays.
- `.github/workflows/ci.yml` - validates all explicit WPF upgrade/rebase contract steps fail closed.
- `.github/workflows/release-package.yml` - enforces explicit WPF contract steps before package promotion.
- `.github/workflows/vm-smoke-matrix.yml` - enforces explicit WPF contract steps per VM runner.
- `tools/release/Invoke-PackageBuild.ps1` - adds explicit WPF evidence catalog keys with summary-step validation.
- `docs/release/UpgradeAndRebaseValidation.md` - documents explicit WPF contract identifiers and enforcement expectations.

## Decisions Made

- Required explicit WPF contract step identities (`workflow`, `severity`, `recovery`) as first-class promotion evidence to map directly to `WP-01`..`WP-03`.
- Kept deterministic existing test entry points and introduced explicit WPF contract step names as aliases to preserve PS 5.1 compatibility and avoid dependency expansion.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 02 evidence promotion work is complete and committed. Ready for `12-wpf-parity-evidence-promotion-and-verification-03-PLAN.md`.

---

*Phase: 12-wpf-parity-evidence-promotion-and-verification*
*Completed: 2026-02-16*

## Self-Check: PASSED

- FOUND: `.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-wpf-parity-evidence-promotion-and-verification-02-SUMMARY.md`
- FOUND commit: `8d44970`
- FOUND commit: `2a8be92`
- FOUND commit: `c18ba24`

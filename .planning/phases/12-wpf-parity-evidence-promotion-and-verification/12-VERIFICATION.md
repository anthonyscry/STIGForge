# Phase 12 Verification - WPF Parity Evidence Promotion and Verification

## Scope

This artifact is the canonical closure evidence for Phase 12 (`WP-01` through `WP-03`).
It verifies that source WPF behavior evidence is promoted into release/package enforcement artifacts and that requirement closure remains fail-closed.

This verification enforces three-source closure alignment:

1. `.planning/REQUIREMENTS.md` traceability rows
2. Source-phase behavior evidence (`.planning/phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md` and Phase 09 summaries)
3. Promotion wiring evidence (`tools/release/Invoke-ReleaseGate.ps1`, `.github/workflows/*.yml`, and `tools/release/Invoke-PackageBuild.ps1`)

Requirements remain unresolved if any source is missing, mismatched, or non-passing.

## Verification Commands

Run these commands to validate deterministic structure and promotion wiring:

```bash
rg --line-number "^# Phase 12 Verification|^## Requirement Evidence Mapping|^## Promotion Wiring Checks|^## Three-Source Cross-Check|WP-0[1-3]|upgrade-rebase-wpf-(workflow|severity|recovery)-contract" .planning/phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md
rg --line-number "upgrade-rebase-wpf-(workflow|severity|recovery)-contract" tools/release/Invoke-ReleaseGate.ps1 .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml
rg --line-number "upgradeRebaseWpf(Workflow|Severity|Recovery)Contract|summaryStep" tools/release/Invoke-PackageBuild.ps1
```

## Requirement Evidence Mapping

| Requirement | Requirement statement | Source behavior evidence (Phase 09) | Promotion wiring evidence (Phase 12) | Evidence status |
|---|---|---|---|---|
| WP-01 | WPF app exposes diff/rebase workflow end-to-end without CLI fallback for standard operator paths | `09-VERIFICATION.md` maps WP-01 to Phase 09 plan evidence; `09-...-01-SUMMARY.md` documents WPF `DiffViewer` and `RebaseWizard` end-to-end parity and deterministic exports | `Invoke-ReleaseGate.ps1` emits `upgrade-rebase-wpf-workflow-contract`; CI/release/vm workflows require step presence and success; package evidence catalog requires `upgradeRebaseWpfWorkflowContract` with `summaryStep=upgrade-rebase-wpf-workflow-contract` | present |
| WP-02 | WPF status and mission summaries match CLI semantics for blocking failures, warnings, and optional skips | `09-VERIFICATION.md` maps WP-02 to CLI/WPF severity parity evidence; `09-...-02-SUMMARY.md` documents mission severity parity propagation across dashboard/apply/verify/report surfaces | `Invoke-ReleaseGate.ps1` emits `upgrade-rebase-wpf-severity-contract`; CI/release/vm workflows require step presence and success; package evidence catalog requires `upgradeRebaseWpfSeverityContract` with `summaryStep=upgrade-rebase-wpf-severity-contract` | present |
| WP-03 | WPF surfaces actionable recovery guidance for failed apply/rebase paths | `09-VERIFICATION.md` maps WP-03 to WPF recovery guidance evidence; `09-...-02-SUMMARY.md` documents required artifacts, next-action hints, and rollback guidance surfacing | `Invoke-ReleaseGate.ps1` emits `upgrade-rebase-wpf-recovery-contract`; CI/release/vm workflows require step presence and success; package evidence catalog requires `upgradeRebaseWpfRecoveryContract` with `summaryStep=upgrade-rebase-wpf-recovery-contract` | present |

## Promotion Wiring Checks

| Check area | Source | Required contract(s) | Fail-closed condition |
|---|---|---|---|
| Release-gate step emission | `tools/release/Invoke-ReleaseGate.ps1` | `upgrade-rebase-wpf-workflow-contract`, `upgrade-rebase-wpf-severity-contract`, `upgrade-rebase-wpf-recovery-contract` | Missing step definition or failed step marks upgrade/rebase validation as failed |
| Workflow enforcement | `.github/workflows/ci.yml`, `.github/workflows/release-package.yml`, `.github/workflows/vm-smoke-matrix.yml` | same three WPF contracts | Any missing/failed step throws and blocks CI/release/vm promotion path |
| Package evidence linkage | `tools/release/Invoke-PackageBuild.ps1` | `upgradeRebaseWpfWorkflowContract`, `upgradeRebaseWpfSeverityContract`, `upgradeRebaseWpfRecoveryContract` with matching `summaryStep` values | Missing catalog entry, missing artifact, missing summary step, or failed summary step leaves release-gate evidence incomplete |

Fail-closed policy: WP closure cannot be marked complete if promotion contract presence/success is not machine-verifiable in all required wiring points.

## Command Results

- `rg` checks confirm this artifact contains required sections and explicit `upgrade-rebase-wpf-(workflow|severity|recovery)-contract` references.
- `rg` checks in `Invoke-ReleaseGate.ps1` confirm the three WPF contract steps are emitted and included in required evidence arrays.
- `rg` checks in CI/release/vm workflows confirm each path requires explicit WPF contract step presence/success.
- `rg` checks in `Invoke-PackageBuild.ps1` confirm package release-gate evidence catalog includes WPF contract keys linked to expected summary step names.

## Three-Source Cross-Check

| Requirement | REQUIREMENTS.md traceability row | Source evidence mapping (09-VERIFICATION + summaries) | Promotion wiring checks | Verdict |
|---|---|---|---|---|
| WP-01 | present (`Pending`) | present | present | ready-for-closure |
| WP-02 | present (`Pending`) | present | present | ready-for-closure |
| WP-03 | present (`Pending`) | present | present | ready-for-closure |

Fail-closed rule: change verdict to `closed` only after REQUIREMENTS traceability rows are reconciled to `Completed` and still match source evidence plus promotion wiring evidence.

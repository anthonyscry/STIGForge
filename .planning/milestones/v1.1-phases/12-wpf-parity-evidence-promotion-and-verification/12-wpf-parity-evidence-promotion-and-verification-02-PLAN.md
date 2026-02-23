---
phase: 12-wpf-parity-evidence-promotion-and-verification
plan: 02
type: execute
wave: 1
depends_on: []
files_modified:
  - tools/release/Invoke-ReleaseGate.ps1
  - tools/release/Invoke-PackageBuild.ps1
  - .github/workflows/ci.yml
  - .github/workflows/release-package.yml
  - .github/workflows/vm-smoke-matrix.yml
  - docs/release/UpgradeAndRebaseValidation.md
autonomous: true
requirements:
  - WP-01
  - WP-02
  - WP-03
must_haves:
  truths:
    - "Promotion evidence includes explicit WPF workflow, severity parity, and recovery guidance contract signals, not only generic upgrade/rebase pass status."
    - "CI, release-package, and VM smoke workflows fail closed when explicit WPF contract evidence steps are missing or failed."
    - "Release package reproducibility evidence requires explicit WPF parity evidence keys from release-gate artifacts."
  artifacts:
    - path: "tools/release/Invoke-ReleaseGate.ps1"
      provides: "Explicit WPF parity contract step emission and requiredEvidence entries in upgrade/rebase summary"
      contains: "upgrade-rebase-wpf"
    - path: "tools/release/Invoke-PackageBuild.ps1"
      provides: "Release-gate evidence catalog entries for WPF parity contract artifacts"
      contains: "releaseGateCatalog"
    - path: ".github/workflows/release-package.yml"
      provides: "Fail-closed workflow validation of explicit WPF evidence steps"
      contains: "Validate upgrade/rebase evidence"
  key_links:
    - from: "tools/release/Invoke-ReleaseGate.ps1"
      to: ".github/workflows/ci.yml"
      via: "workflow parses upgrade-rebase summary steps and enforces explicit WPF contract presence"
      pattern: "upgrade-rebase-wpf.*contract"
    - from: "tools/release/Invoke-ReleaseGate.ps1"
      to: ".github/workflows/release-package.yml"
      via: "release-package summary validation requires each explicit WPF contract step"
      pattern: "summary\.steps"
    - from: "tools/release/Invoke-ReleaseGate.ps1"
      to: "tools/release/Invoke-PackageBuild.ps1"
      via: "required evidence catalog includes new WPF parity keys"
      pattern: "upgradeRebase.*Wpf"
---

<objective>
Promote explicit WPF parity contract evidence into release-gate outputs and workflow/package enforcement points.

Purpose: Ensure WP requirements are represented as first-class promotion contracts and cannot silently regress behind generic pass status.
Output: Release gate and workflow validators require explicit WPF contract signals for workflow coverage, mission semantics parity, and recovery guidance.
</objective>

<execution_context>
@/home/anthonyscry/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/anthonyscry/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/12-wpf-parity-evidence-promotion-and-verification/12-RESEARCH.md
@tools/release/Invoke-ReleaseGate.ps1
@tools/release/Invoke-PackageBuild.ps1
@.github/workflows/ci.yml
@.github/workflows/release-package.yml
@.github/workflows/vm-smoke-matrix.yml
@docs/release/UpgradeAndRebaseValidation.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add explicit WPF parity contract signals to release-gate summary/report outputs</name>
  <files>tools/release/Invoke-ReleaseGate.ps1</files>
  <action>Extend `Invoke-ReleaseGate.ps1` so upgrade/rebase validation emits explicit WPF contract steps mapped to phase requirements: one for WPF diff/rebase workflow contract (`WP-01`), one for WPF mission severity semantics parity (`WP-02`), and one for WPF recovery guidance contract (`WP-03`). Reuse existing deterministic test/script entry points where possible per research guidance (do not introduce new external dependencies). Add these contract names to both upgrade/rebase summary `requiredEvidence` arrays and the operator report section. Preserve PowerShell 5.1 compatibility and fail-closed behavior (missing or failed contract step keeps status failed).</action>
  <verify>Run `rg --line-number "upgrade-rebase-wpf-(workflow|severity|recovery)-contract|requiredEvidence" tools/release/Invoke-ReleaseGate.ps1` and confirm all three explicit WPF contract signals are emitted and required.</verify>
  <done>Release-gate outputs include explicit, machine-readable WPF contract signals aligned to `WP-01`..`WP-03`, and gate status fails when any required signal fails.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce explicit WPF contract evidence in CI, release-package, and VM promotion flows</name>
  <files>.github/workflows/ci.yml, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Update parity evidence validation steps in all three workflows to assert presence and success of the explicit WPF contract step names from Task 1 (not only `upgrade-rebase-parity-contract`). Keep fail-closed checks: missing summary, missing contract steps, or failed steps must throw and stop promotion. Maintain existing offline-first behavior and do not introduce optional bypass for these WPF checks.</action>
  <verify>Run `rg --line-number "upgrade-rebase-wpf-(workflow|severity|recovery)-contract|Validate upgrade/rebase evidence" .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml` and confirm each workflow validates all explicit WPF signals.</verify>
  <done>All promotion workflows deterministically enforce explicit WPF parity evidence and fail closed on missing or failed contract steps.</done>
</task>

<task type="auto">
  <name>Task 3: Wire package evidence catalog and release docs to explicit WPF contract signals</name>
  <files>tools/release/Invoke-PackageBuild.ps1, docs/release/UpgradeAndRebaseValidation.md</files>
  <action>Extend `Invoke-PackageBuild.ps1` release-gate catalog to include required keys representing the new explicit WPF parity evidence areas so package manifest/reproducibility evidence cannot report complete when those artifacts are absent. Update `docs/release/UpgradeAndRebaseValidation.md` contract and artifact sections to list the new explicit WPF contract signals and their verification expectations. Keep terminology consistent with release-gate step names.</action>
  <verify>Run `rg --line-number "upgradeRebase.*Wpf|upgrade-rebase-wpf-(workflow|severity|recovery)-contract" tools/release/Invoke-PackageBuild.ps1 docs/release/UpgradeAndRebaseValidation.md` and confirm catalog and docs reference the same contract identifiers.</verify>
  <done>Package evidence linkage and release documentation explicitly require and describe WPF parity contract signals with no naming drift.</done>
</task>

</tasks>

<verification>
- `rg --line-number "upgrade-rebase-wpf-(workflow|severity|recovery)-contract" tools/release/Invoke-ReleaseGate.ps1 .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml docs/release/UpgradeAndRebaseValidation.md`
- `rg --line-number "upgradeRebase.*Wpf|releaseGateCatalog" tools/release/Invoke-PackageBuild.ps1`
</verification>

<success_criteria>
- Release gate emits explicit WPF workflow/severity/recovery contract evidence in summary and report artifacts.
- CI, release-package, and VM smoke workflows enforce those explicit WPF contract signals fail-closed.
- Release package evidence catalog and release docs reflect the same explicit WPF contract identifiers.
</success_criteria>

<output>
After completion, create `.planning/phases/12-wpf-parity-evidence-promotion-and-verification/12-wpf-parity-evidence-promotion-and-verification-02-SUMMARY.md`
</output>

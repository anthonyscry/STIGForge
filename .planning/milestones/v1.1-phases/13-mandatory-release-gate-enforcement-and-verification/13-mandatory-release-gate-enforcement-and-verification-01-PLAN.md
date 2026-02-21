---
phase: 13-mandatory-release-gate-enforcement-and-verification
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tools/release/Test-ReleaseEvidenceContract.ps1
  - tools/release/Invoke-PackageBuild.ps1
  - .github/workflows/ci.yml
  - .github/workflows/release-package.yml
  - .github/workflows/vm-smoke-matrix.yml
  - docs/release/ReleaseCandidatePlaybook.md
autonomous: true
requirements:
  - QA-01
  - QA-02
  - QA-03
must_haves:
  truths:
    - "Promotion packaging is blocked before any zip artifact is produced when required evidence is missing, failed, or disabled."
    - "CI, release-package, and VM workflows enforce one mandatory core evidence contract with deterministic artifact names and blocker semantics."
    - "Blocker output is checklist-first and includes explicit blocker category and copy-paste recovery commands in console and persisted report artifacts."
  artifacts:
    - path: "tools/release/Test-ReleaseEvidenceContract.ps1"
      provides: "Shared fail-closed evidence-contract validator with blocker categories (missing-proof, failed-check, disabled-check)"
      contains: "missing-proof"
    - path: ".github/workflows/release-package.yml"
      provides: "Stop-before-package enforcement including disabled-check blocker when required gate execution is turned off"
      contains: "disabled-check"
    - path: "tools/release/Invoke-PackageBuild.ps1"
      provides: "Mandatory release-gate preflight that hard-fails instead of recording partial evidence"
      contains: "Test-ReleaseEvidenceContract.ps1"
  key_links:
    - from: "tools/release/Test-ReleaseEvidenceContract.ps1"
      to: ".github/workflows/ci.yml"
      via: "workflow invokes shared validator against deterministic evidence roots"
      pattern: "Test-ReleaseEvidenceContract\.ps1"
    - from: "tools/release/Test-ReleaseEvidenceContract.ps1"
      to: ".github/workflows/vm-smoke-matrix.yml"
      via: "VM promotion flow reuses shared validator with same core requirements"
      pattern: "missing-proof|failed-check|disabled-check"
    - from: ".github/workflows/release-package.yml"
      to: "tools/release/Invoke-PackageBuild.ps1"
      via: "package build runs only after release-gate required checks are enabled and passed"
      pattern: "Build release packages"
---

<objective>
Harden promotion flows with mandatory fail-closed release-evidence contract enforcement shared across CI, release-package, VM, and package-build paths.

Purpose: Remove fail-open promotion paths so missing, failed, or disabled required checks always block before package artifacts are produced.
Output: Shared evidence validator, consistent blocker taxonomy and recovery output, and stop-before-package enforcement in workflow and package build logic.
</objective>

<execution_context>
@/home/anthonyscry/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/anthonyscry/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/13-mandatory-release-gate-enforcement-and-verification/13-CONTEXT.md
@.planning/phases/13-mandatory-release-gate-enforcement-and-verification/13-RESEARCH.md
@tools/release/Invoke-ReleaseGate.ps1
@tools/release/Invoke-PackageBuild.ps1
@.github/workflows/ci.yml
@.github/workflows/release-package.yml
@.github/workflows/vm-smoke-matrix.yml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create shared mandatory evidence-contract validator with checklist-first blocker reporting</name>
  <files>tools/release/Test-ReleaseEvidenceContract.ps1</files>
  <action>Create `Test-ReleaseEvidenceContract.ps1` as the single validator used by all promotion surfaces. Implement deterministic required artifact and summary-step checks with one mandatory core evidence set (release summary/report/checksums, upgrade/rebase summary/report and explicit WPF contracts, and quarterly/stability trend artifacts) plus optional flow-specific extras. Emit typed fail-closed blocker categories exactly as `missing-proof`, `failed-check`, and `disabled-check`. Output must use checklist-first format (`what blocked`, `why blocked`, `next command`) and include copy-paste recovery commands. Persist the same blocker checklist to a report artifact file (for example under `report/release-evidence-contract-report.md`) in addition to console output. Keep PowerShell 5.1-compatible strict-mode behavior and do not introduce warning-only bypass logic.</action>
  <verify>Run `rg --line-number "missing-proof|failed-check|disabled-check|what blocked|why blocked|next command|release-evidence-contract-report" tools/release/Test-ReleaseEvidenceContract.ps1` and confirm category names plus checklist/recovery output are implemented.</verify>
  <done>One reusable validator exists with deterministic required-contract checks, explicit blocker categories, and persisted checklist-style recovery guidance.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce mandatory contract and disabled-check blocking in CI, release-package, and VM workflows</name>
  <files>.github/workflows/ci.yml, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Wire all three workflows to call `Test-ReleaseEvidenceContract.ps1` against deterministic evidence roots so they share identical fail-closed semantics and contract signal names. In `release-package.yml`, add an explicit preflight that fails with `[disabled-check]` when `run_release_gate=false` (per locked decision that disabling required checks is a blocker). Keep any required check failures as immediate blockers before packaging steps. Update required artifact upload behavior so mandatory proof bundle uploads fail when missing (`if-no-files-found: error`) instead of warn for required bundles.</action>
  <verify>Run `rg --line-number "Test-ReleaseEvidenceContract\.ps1|\[disabled-check\]|if-no-files-found: error|missing-proof|failed-check" .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml` and confirm shared validator wiring plus disabled-check blocker enforcement.</verify>
  <done>CI, release-package, and VM flows all enforce the same mandatory contract checks and block immediately on missing-proof, failed-check, or disabled-check conditions.</done>
</task>

<task type="auto">
  <name>Task 3: Make package build fail closed on mandatory release-gate proof and align operator guidance</name>
  <files>tools/release/Invoke-PackageBuild.ps1, docs/release/ReleaseCandidatePlaybook.md</files>
  <action>Update `Invoke-PackageBuild.ps1` so missing/failed required release-gate evidence throws a terminating error before `Compress-Archive` package creation (do not leave status as `partial` for required proof). Reuse the shared validator from Task 1 for preflight contract enforcement so manual/local package builds cannot bypass policy. Ensure failure messages include blocker category and copy-paste recovery command references, and that persisted reproducibility/report metadata reflects blocked state. Update release playbook guidance to document mandatory enforcement semantics, blocker categories, and operator recovery commands.</action>
  <verify>Run `rg --line-number "partial|Compress-Archive|Test-ReleaseEvidenceContract|missing-proof|failed-check|disabled-check" tools/release/Invoke-PackageBuild.ps1 docs/release/ReleaseCandidatePlaybook.md` and confirm required evidence now blocks before package zip generation.</verify>
  <done>Package build path is fail-closed and cannot produce promotion artifacts when mandatory release evidence is missing, failed, or disabled.</done>
</task>

</tasks>

<verification>
- `rg --line-number "Test-ReleaseEvidenceContract\.ps1|missing-proof|failed-check|disabled-check" tools/release/Invoke-PackageBuild.ps1 .github/workflows/ci.yml .github/workflows/release-package.yml .github/workflows/vm-smoke-matrix.yml`
- `rg --line-number "release-evidence-contract-report|what blocked|why blocked|next command" tools/release/Test-ReleaseEvidenceContract.ps1`
- `rg --line-number "run_release_gate|\[disabled-check\]|if-no-files-found: error" .github/workflows/release-package.yml`
</verification>

<success_criteria>
- Promotion packaging is blocked before artifact creation when required evidence is missing, failed, or disabled.
- CI, release-package, and VM flows use one deterministic mandatory contract with standardized blocker categories.
- Checklist-first blocker guidance with copy-paste recovery commands appears in workflow output and persisted report artifacts.
</success_criteria>

<output>
After completion, create `.planning/phases/13-mandatory-release-gate-enforcement-and-verification/13-mandatory-release-gate-enforcement-and-verification-01-SUMMARY.md`
</output>

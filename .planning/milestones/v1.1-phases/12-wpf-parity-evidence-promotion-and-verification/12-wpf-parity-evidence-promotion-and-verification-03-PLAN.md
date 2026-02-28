---
phase: 12-wpf-parity-evidence-promotion-and-verification
plan: 03
type: execute
wave: 2
depends_on:
  - 12-wpf-parity-evidence-promotion-and-verification-01
  - 12-wpf-parity-evidence-promotion-and-verification-02
files_modified:
  - .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md
  - .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md
  - .planning/REQUIREMENTS.md
autonomous: true
requirements:
  - WP-01
  - WP-02
  - WP-03
must_haves:
  truths:
    - "Phase 12 has a verification artifact proving WPF parity evidence is wired from source behavior evidence into promotion enforcement artifacts."
    - "WP-01 through WP-03 are marked completed only when three-source closure evidence aligns (traceability, summary metadata, verification mapping)."
    - "Requirement closure remains fail-closed if any required source is missing, mismatched, or non-passing."
  artifacts:
    - path: ".planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md"
      provides: "Canonical phase closure proof for WPF evidence promotion wiring and requirement closure checks"
      contains: "Requirement Evidence Mapping"
    - path: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      provides: "Source-phase WP evidence mapping referenced by phase closure cross-check"
      contains: "WP-01"
    - path: ".planning/REQUIREMENTS.md"
      provides: "WP requirement checkboxes and traceability rows updated to Completed only after evidence alignment"
      contains: "| WP-01 | Phase 12 | Completed |"
  key_links:
    - from: ".planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md"
      to: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      via: "closure mapping references source-phase WP behavior evidence"
      pattern: "WP-0[1-3]"
    - from: ".planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md"
      to: "tools/release/Invoke-ReleaseGate.ps1"
      via: "promotion wiring checks reference explicit WPF contract step names"
      pattern: "upgrade-rebase-wpf-(workflow|severity|recovery)-contract"
    - from: ".planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md"
      to: ".planning/REQUIREMENTS.md"
      via: "three-source cross-check verdict alignment"
      pattern: "(closed|unresolved|ready-for-closure)"
---

<objective>
Finalize Phase 12 closure by creating canonical verification evidence for promotion wiring and reconciling WP requirement traceability status.

Purpose: Complete machine-verifiable, fail-closed WP closure after source evidence and promotion wiring are in place.
Output: `12-VERIFICATION.md` plus aligned WP closure status across `09-VERIFICATION.md` and `.planning/REQUIREMENTS.md`.
</objective>

<execution_context>
@/home/anthonyscry/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/anthonyscry/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/REQUIREMENTS.md
@.planning/v1.1-MILESTONE-AUDIT.md
@.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-RESEARCH.md
@.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md
@tools/release/Invoke-ReleaseGate.ps1
@.github/workflows/ci.yml
@.github/workflows/release-package.yml
@.github/workflows/vm-smoke-matrix.yml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create Phase 12 verification artifact for WPF evidence promotion wiring</name>
  <files>.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md</files>
  <action>Create `.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md` with deterministic sections: scope, verification commands, requirement evidence mapping, promotion wiring checks, command results, and three-source cross-check. For each `WP-01`..`WP-03`, map (a) source behavior evidence from `09-VERIFICATION.md` and summaries, and (b) explicit promotion evidence contracts from release-gate/workflow validators/package evidence linkage. Include fail-closed verdict language so missing wiring evidence keeps requirement unresolved.</action>
  <verify>Run `rg --line-number "^# Phase 12 Verification|^## Requirement Evidence Mapping|^## Promotion Wiring Checks|^## Three-Source Cross-Check|WP-0[1-3]|upgrade-rebase-wpf-(workflow|severity|recovery)-contract" .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md` and confirm complete structure plus explicit contract references.</verify>
  <done>`12-VERIFICATION.md` exists and clearly proves diff/rebase -> WPF parity -> promotion evidence wiring for all WP requirements.</done>
</task>

<task type="auto">
  <name>Task 2: Reconcile WP requirement closure status across REQUIREMENTS and verification artifacts</name>
  <files>.planning/REQUIREMENTS.md, .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md, .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md</files>
  <action>Update `.planning/REQUIREMENTS.md` WP checkboxes and traceability rows to `Completed` only after Task 1 confirms all three closure sources align. Update three-source cross-check verdict rows in both verification artifacts so they agree with final WP closure state. Keep counters/coverage summary in `.planning/REQUIREMENTS.md` consistent with the new completion totals. Preserve fail-closed semantics: if any source remains missing or mismatched, keep affected WP rows pending/unresolved and document why.</action>
  <verify>Run `rg --line-number "\*\*WP-0[1-3]\*\*:|\| WP-0[1-3] \| Phase 12 \| (Pending|Completed) \|" .planning/REQUIREMENTS.md && rg --line-number "WP-0[1-3].*(closed|unresolved|ready-for-closure)" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md` and confirm statuses align across all sources.</verify>
  <done>WP requirement closure is machine-consistent and fail-closed across requirements traceability plus both verification artifacts.</done>
</task>

</tasks>

<verification>
- `rg --line-number "WP-0[1-3]" .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md`
- `rg --line-number "\| WP-0[1-3] \| Phase 12 \| Completed \|" .planning/REQUIREMENTS.md`
- `rg --line-number "WP-0[1-3].*(closed|unresolved|ready-for-closure)" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md .planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-VERIFICATION.md`
</verification>

<success_criteria>
- `12-VERIFICATION.md` exists with explicit WP mapping and promotion wiring checks.
- WP-01..WP-03 closure status is aligned across REQUIREMENTS and verification artifacts.
- Closure path enforces fail-closed behavior for missing/mismatched evidence.
</success_criteria>

<output>
After completion, create `.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-wpf-parity-evidence-promotion-and-verification-03-SUMMARY.md`
</output>

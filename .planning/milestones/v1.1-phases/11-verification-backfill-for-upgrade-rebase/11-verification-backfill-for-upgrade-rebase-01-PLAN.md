---
phase: 11-verification-backfill-for-upgrade-rebase
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md
  - .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md
  - .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md
  - .planning/REQUIREMENTS.md
autonomous: true
gap_closure: true
requirements:
  - UR-01
  - UR-02
  - UR-03
  - UR-04
must_haves:
  truths:
    - "Phase 08 has a verification artifact that explicitly maps UR-01 through UR-04 to concrete evidence."
    - "UR-01 through UR-04 can be machine-verified as closed across requirements traceability, verification artifact, and summary metadata."
    - "No UR requirement is marked completed when any closure source is missing or inconsistent."
  artifacts:
    - path: ".planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md"
      provides: "Canonical Phase 08 verification evidence and three-source cross-check table"
      contains: "Three-Source Cross-Check"
    - path: ".planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md"
      provides: "Machine-readable requirement closure metadata for Plan 01 summary"
      contains: "requirements-completed"
    - path: ".planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md"
      provides: "Machine-readable requirement closure metadata for Plan 02 summary"
      contains: "requirements-completed"
    - path: ".planning/REQUIREMENTS.md"
      provides: "Requirement checkbox and traceability status for UR closure"
      contains: "| UR-01 | Phase 11 | Completed |"
  key_links:
    - from: ".planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md"
      to: ".planning/REQUIREMENTS.md"
      via: "Three-source table status alignment"
      pattern: "UR-0[1-4].*(closed|ready-for-closure)"
    - from: ".planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md"
      to: ".planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md"
      via: "requirements-completed IDs must match verification IDs"
      pattern: "requirements-completed"
    - from: ".planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md"
      to: ".planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md"
      via: "requirements-completed IDs must match verification IDs"
      pattern: "requirements-completed"
---

<objective>
Restore machine-verifiable closure evidence for upgrade/rebase requirements by backfilling Phase 08 verification artifacts and metadata.

Purpose: Remove UR orphaning so requirement traceability can be audited and promoted without manual interpretation.
Output: One new Phase 08 verification artifact plus aligned UR closure metadata in both Phase 08 summaries and REQUIREMENTS traceability.
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
@.planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md
@.planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create Phase 08 verification artifact with UR evidence and fail-closed status model</name>
  <files>.planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md</files>
  <action>Create `.planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md` as the canonical verification artifact for `UR-01` to `UR-04`. Include sections for scope, verification commands, requirement evidence mapping, command results, and a three-source cross-check table. Map each UR requirement to concrete evidence from Phase 08 summaries and relevant implementation/docs artifacts. If any required command result or artifact evidence is missing, keep that requirement verdict unresolved (do not mark closed) to preserve fail-closed requirement closure semantics.</action>
  <verify>Run `rg --line-number "^# Phase 08 Verification - Upgrade/Rebase Operator Workflow|^## Requirement Evidence Mapping|^## Three-Source Cross-Check|UR-0[1-4]" .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md` and confirm all required sections plus all four UR IDs are present.</verify>
  <done>`08-VERIFICATION.md` exists, has deterministic structure, and includes explicit evidence rows for `UR-01` through `UR-04` with verdicts reflecting available proof.</done>
</task>

<task type="auto">
  <name>Task 2: Add and align requirements-completed metadata in both Phase 08 summaries</name>
  <files>.planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md, .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md</files>
  <action>Add `requirements-completed` frontmatter to both Phase 08 summary files using the exact same ordered list (`UR-01`, `UR-02`, `UR-03`, `UR-04`). Keep existing summary content unchanged except for this metadata addition. Do not infer extra requirements beyond the phase scope. Ensure ordering and spelling match `REQUIREMENTS.md` IDs exactly so cross-check tooling remains deterministic.</action>
  <verify>Run `rg --line-number "requirements-completed|UR-0[1-4]" .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-0[12]-SUMMARY.md` and confirm both files contain identical metadata blocks.</verify>
  <done>Both Phase 08 summary frontmatters include matching `requirements-completed` arrays containing only `UR-01` to `UR-04`.</done>
</task>

<task type="auto">
  <name>Task 3: Reconcile UR traceability status and close three-source verification loop</name>
  <files>.planning/REQUIREMENTS.md, .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md</files>
  <action>Update `.planning/REQUIREMENTS.md` so UR checklist rows and traceability entries for `UR-01` through `UR-04` move to completed status only after Task 1 and Task 2 evidence exists. Then update the three-source cross-check in `08-VERIFICATION.md` to final closure verdicts for each UR requirement. Keep coverage counters and last-updated note in `.planning/REQUIREMENTS.md` consistent with the new UR completion counts. Fail closed: if any source is still missing, leave corresponding UR status pending/unresolved and document the mismatch.</action>
  <verify>Run `rg --line-number "\*\*UR-0[1-4]\*\*:|\| UR-0[1-4] \| Phase 11 \| (Pending|Completed) \|" .planning/REQUIREMENTS.md && rg --line-number "UR-0[1-4].*(closed|unresolved|ready-for-closure)" .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md` and confirm all UR statuses are consistent across both files.</verify>
  <done>UR traceability is machine-consistent across requirements file, verification artifact, and summary metadata with no orphaned UR requirement evidence.</done>
</task>

</tasks>

<verification>
- `rg --line-number "UR-0[1-4]" .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md`
- `rg --line-number "requirements-completed|UR-0[1-4]" .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-0[12]-SUMMARY.md`
- `rg --line-number "\| UR-0[1-4] \| Phase 11 \| Completed \|" .planning/REQUIREMENTS.md`
</verification>

<success_criteria>
- `08-VERIFICATION.md` exists with explicit UR requirement evidence and three-source cross-check table.
- Both Phase 08 summaries expose `requirements-completed` frontmatter for `UR-01`..`UR-04`.
- `.planning/REQUIREMENTS.md` and `08-VERIFICATION.md` agree on UR closure status without orphaned evidence.
</success_criteria>

<output>
After completion, create `.planning/phases/11-verification-backfill-for-upgrade-rebase/11-verification-backfill-for-upgrade-rebase-01-SUMMARY.md`
</output>

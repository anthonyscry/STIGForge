---
phase: 12-wpf-parity-evidence-promotion-and-verification
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md
  - .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md
  - .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md
autonomous: true
requirements:
  - WP-01
  - WP-02
  - WP-03
must_haves:
  truths:
    - "Phase 09 has a canonical verification artifact that maps WP-01 through WP-03 to concrete WPF parity evidence."
    - "Phase 09 summaries expose machine-readable requirements-completed metadata for WP-01 through WP-03."
    - "WP verification remains fail-closed when traceability, summary metadata, and verification evidence are not aligned."
  artifacts:
    - path: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      provides: "Canonical WP requirement evidence mapping and three-source cross-check for Phase 09"
      contains: "Three-Source Cross-Check"
    - path: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md"
      provides: "Machine-readable closure metadata for WPF workflow parity implementation"
      contains: "requirements-completed"
    - path: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md"
      provides: "Machine-readable closure metadata for WPF severity and recovery implementation"
      contains: "requirements-completed"
  key_links:
    - from: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      to: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md"
      via: "WP-01 evidence rows reference summary-delivered workflow artifacts"
      pattern: "WP-01"
    - from: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      to: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md"
      via: "WP-02 and WP-03 evidence rows reference severity and recovery artifacts"
      pattern: "WP-0[23]"
    - from: ".planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md"
      to: ".planning/REQUIREMENTS.md"
      via: "three-source cross-check includes traceability status column"
      pattern: "(closed|unresolved|ready-for-closure)"
---

<objective>
Backfill the missing Phase 09 verification artifact and metadata so WPF parity requirements have canonical, machine-verifiable evidence.

Purpose: Remove WP orphaning at the source behavior phase before promotion wiring and closure reconciliation.
Output: `09-VERIFICATION.md` plus aligned `requirements-completed` metadata in both Phase 09 summaries.
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
@.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md
@.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md
@.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create Phase 09 verification artifact with WP evidence mapping</name>
  <files>.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md</files>
  <action>Create `.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md` as the canonical artifact for `WP-01`, `WP-02`, and `WP-03`. Follow the proven structure from `08-VERIFICATION.md`: scope, verification commands, requirement evidence mapping, command results, and three-source cross-check. Map each WP requirement to concrete evidence from Phase 09 implementation summaries (WPF workflow path, severity semantics, and actionable recovery guidance). Keep fail-closed semantics: if evidence is missing or ambiguous, mark verdict `unresolved` instead of closed.</action>
  <verify>Run `rg --line-number "^# Phase 09 Verification|^## Requirement Evidence Mapping|^## Three-Source Cross-Check|WP-0[1-3]" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md` and confirm all required sections plus all WP IDs are present.</verify>
  <done>`09-VERIFICATION.md` exists and contains explicit evidence rows for `WP-01` through `WP-03` with fail-closed verdict language.</done>
</task>

<task type="auto">
  <name>Task 2: Add deterministic requirements-completed metadata to both Phase 09 summaries</name>
  <files>.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md, .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md, .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md</files>
  <action>Add `requirements-completed` frontmatter arrays to both Phase 09 summaries using the exact ordered IDs `WP-01`, `WP-02`, `WP-03`. Keep existing summary body content unchanged. Then update the Phase 09 three-source cross-check table to reference the presence of summary metadata consistently for each WP row. Do not include non-WP IDs. If metadata and evidence disagree, keep row verdict unresolved and document the mismatch.</action>
  <verify>Run `rg --line-number "requirements-completed|WP-0[1-3]" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-0[12]-SUMMARY.md && rg --line-number "WP-0[1-3].*(closed|unresolved|ready-for-closure)" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md` and confirm alignment.</verify>
  <done>Both Phase 09 summaries contain matching `requirements-completed` arrays and `09-VERIFICATION.md` cross-check rows reflect the same WP ID set.</done>
</task>

</tasks>

<verification>
- `rg --line-number "WP-0[1-3]" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-VERIFICATION.md`
- `rg --line-number "requirements-completed|WP-0[1-3]" .planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-0[12]-SUMMARY.md`
</verification>

<success_criteria>
- Phase 09 now has a canonical verification artifact with explicit WP requirement evidence mapping.
- Both Phase 09 summaries include deterministic `requirements-completed` metadata for `WP-01`..`WP-03`.
- Three-source closure inputs for WP requirements exist and remain fail-closed on missing evidence.
</success_criteria>

<output>
After completion, create `.planning/milestones/v1.1-phases/12-wpf-parity-evidence-promotion-and-verification/12-wpf-parity-evidence-promotion-and-verification-01-SUMMARY.md`
</output>

# Verification-First Stabilization Design

Date: 2026-02-14
Scope: Post-SUPERPROMPT verification and stabilization for recent STIGForge changes.

## Objective

Validate that recent cross-cutting changes (applicability logic, import dedupe preference, machine scan presentation, remote scan workflow, and theme consistency updates) are stable, regressions are caught early, and any failures are fixed before additional feature work.

## Success Criteria

- Full unit test suite passes in current branch.
- Windows-targeted app build succeeds.
- Key modified UI flows function at runtime without blocking errors:
  - Import view scroll and applicable pack presentation.
  - Machine scan STIG/SCAP pairing and diagnostics text visibility.
  - Remote scan discovery, selection, and WinRM-gated scanning behavior.
- Selection logic behaves as intended for Android exclusion and SCAP/ADMX auto-select fallbacks.

## Verification Checklist

- [x] Full unit tests pass
- [x] Windows-targeted build passes
- [ ] Import/Machine/Remote smoke checks pass (manual operator review pending)

## Approach Options Considered

1. Full verification first (selected): establish baseline health with full tests/build before manual runtime checks.
2. Fast smoke first: quicker iteration but can hide broader regressions until late.
3. Logic-first deep dive: useful when failures are known, but weaker as an initial confidence pass.

Selected option: Full verification first, then targeted runtime smoke validation, then fix-forward for any issues.

## Design

### 1) Verification Pipeline

- Run full unit tests for repository test projects.
- Run Windows-targeted build for app project(s).
- Capture failing tests/build diagnostics and group failures by subsystem.

### 2) Fix Strategy

- Apply minimal, localized fixes aligned to existing architecture.
- Preserve unrelated user changes in working tree.
- Re-run only impacted tests immediately after each fix, then re-run full verification at the end.

### 3) Runtime Smoke Validation

- Validate key UX flows changed in this branch:
  - Import tab behavior and scrollability.
  - Applicable imported packs matching display.
  - Machine scan structured results and pair-state clarity.
  - Remote discovery list usability and scan command gating.
- Verify themed dialogs (Compare, Diff, About) are legible and consistent in light/dark contexts.

### 4) Diagnostics and Logging Review

- Confirm new diagnostics are present and actionable:
  - SCAP conflict resolution preference (NIWC enhanced from consolidated bundle).
  - Selection reasons for STIG/SCAP/ADMX applicability decisions.
- Ensure log lines are concise and do not overwhelm normal workflow.

### 5) Exit Conditions

- Full tests pass and build passes after fixes.
- No critical runtime-blocking issues in smoke path.
- Remaining non-critical polish items are listed explicitly for follow-up.

## Risks and Mitigations

- Risk: full suite duration slows iteration.
  - Mitigation: targeted re-runs during fix loop; full rerun only at checkpoints.
- Risk: manual smoke misses edge cases.
  - Mitigation: prioritize recently changed paths and diagnostic-heavy branches.
- Risk: mixed existing workspace changes create noise.
  - Mitigation: touch only relevant files and avoid reverting unrelated modifications.

## Test Plan

- Primary: full `dotnet test` for unit test projects.
- Build: `dotnet build` with Windows target settings used by app.
- Secondary: targeted tests for `PackApplicabilityRules` and `ImportDedupService` after relevant edits.

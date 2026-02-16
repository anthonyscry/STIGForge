# Phase 08 Verification - Upgrade/Rebase Operator Workflow

## Scope

This artifact is the canonical verification evidence for Phase 08 closure of `UR-01` through `UR-04`.
It captures requirement-to-evidence mapping, command-based checks, and a fail-closed
three-source cross-check spanning:

1. `.planning/REQUIREMENTS.md` traceability status
2. Phase 08 summary frontmatter (`requirements-completed`)
3. Requirement evidence documented in this verification artifact

Requirements stay `unresolved` unless all three sources align.

## Verification Commands

Run these commands to validate evidence structure and traceability links:

```bash
rg --line-number "^# Plan 0[12] Summary|^## What Was Built|UR-0[1-4]|diff|rebase|blocking|review-required" .planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-0[12]-SUMMARY.md
rg --line-number "\| UR-0[1-4] \| Phase 11 \|" .planning/REQUIREMENTS.md
rg --line-number "^# Phase 08 Verification - Upgrade/Rebase Operator Workflow|^## Requirement Evidence Mapping|^## Three-Source Cross-Check|UR-0[1-4]" .planning/phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md
```

## Requirement Evidence Mapping

| Requirement | Requirement statement | Evidence from Phase 08 deliverables | Evidence status |
|---|---|---|---|
| UR-01 | Deterministic diff report classifies added/changed/removed/review-required controls | Plan 01 summary documents deterministic ordering in `BaselineDiffService`, first-class `ReviewRequiredControls`, and CLI/WPF review-required reporting; release guidance updated in `docs/release/UpgradeAndRebaseValidation.md` | present |
| UR-02 | Overlay rebase provides deterministic conflict classification and explicit recommended actions | Plan 01/02 summaries document deterministic rebase action ordering, blocking conflict metadata, and operator recommended actions surfaced in CLI/WPF; integration and unit coverage expanded | present |
| UR-03 | Rebase preserves non-conflicting intent and blocks completion while blocking conflicts are unresolved | Plan 01/02 summaries document fail-closed apply gating in `OverlayRebaseService`, CLI `--apply`, and WPF apply flow; unresolved blocking conflicts prevent completion | present |
| UR-04 | Diff/rebase artifacts include machine-readable summary and operator-readable report detail for release review | Plan 01/02 summaries document machine-readable conflict/review metadata and operator-readable diagnostics in CLI/WPF plus release validation guidance updates | present |

## Command Results

- `rg` against Phase 08 summaries: evidence references for diff/rebase semantics and `UR-01`..`UR-04` requirement IDs are present in this artifact mapping.
- `rg` against `.planning/REQUIREMENTS.md`: traceability rows for `UR-01`..`UR-04` exist, map to Phase 11, and are marked `Completed`.
- `rg` against this file: required canonical headings and all four UR IDs are present.

## Three-Source Cross-Check

| Requirement | REQUIREMENTS.md traceability row | Summary metadata (`requirements-completed`) | Verification evidence mapping | Verdict |
|---|---|---|---|---|
| UR-01 | present (`Completed`) | present (`UR-01`) | present | closed |
| UR-02 | present (`Completed`) | present (`UR-02`) | present | closed |
| UR-03 | present (`Completed`) | present (`UR-03`) | present | closed |
| UR-04 | present (`Completed`) | present (`UR-04`) | present | closed |

Fail-closed rule: move a requirement to `closed` only when all three sources are present and consistent.

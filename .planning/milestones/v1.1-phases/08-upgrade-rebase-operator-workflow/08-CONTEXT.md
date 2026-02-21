# Phase 08 Context - Upgrade/Rebase Operator Workflow

## Requirement Mapping

- `UR-01`, `UR-02`, `UR-03`, `UR-04` from `.planning/REQUIREMENTS.md`

## Current Baseline Observations

- CLI exposes `diff-packs` and `rebase-overlay` in `src/STIGForge.Cli/Commands/DiffRebaseCommands.cs`.
- Diff output supports JSON and Markdown, but operator summaries are not yet normalized around explicit `review-required` categories.
- Rebase report includes action classifications (`Keep`, `KeepWithWarning`, `ReviewRequired`, `Remove`, `Remap`) and confidence scoring.
- WPF has diff/rebase surfaces (`DiffViewerViewModel`, `RebaseWizardViewModel`), but completion semantics are not yet tied to unresolved blocking actions.
- Existing test coverage exists in `BaselineDiffServiceTests`, `OverlayRebaseServiceTests`, and CLI integration tests.

## Risks to Control

- Silent operator success when unresolved rebase conflicts still exist.
- Divergence between machine-readable reports and operator-readable release evidence.
- Inconsistent action semantics between CLI and WPF workflows.

## Phase 08 Success Signals

- Rebase completion blocks when unresolved blocking conflicts remain.
- Diff/rebase outputs provide deterministic JSON + Markdown artifact pair with consistent semantics.
- CLI and WPF rebase flows report equivalent conflict/action categories for the same input.

# Debug Session: Overlay artifacts missing in bundle Reports

Date: 2026-02-20
Scope: UAT Test 2 from `.planning/phases/2026-02-19-stigforge-next/01-UAT.md`

## Investigation checklist and evidence

1. Confirm artifact writes in active runnable project path

- Active runnable path is `/mnt/c/projects/STIGForge` on branch `gsd/phase-19-wpf-workflow-ux-polish-and-export-format-picker`.
- In active path, `src/STIGForge.Build/BundleBuilder.cs` writes `na_scope_filter_report.csv`, `review_required.csv`, and `automation_gate.json`, but does not write `overlay_conflicts.csv` or `overlay_decisions.json` and does not call `OverlayMergeService`.
  - Evidence: `src/STIGForge.Build/BundleBuilder.cs:56`
  - Evidence: `src/STIGForge.Build/BundleBuilder.cs:57`
  - Evidence: `src/STIGForge.Build/BundleBuilder.cs:66`
- Grep over active `src/` returns no `OverlayMergeService`, no `overlay_conflicts.csv`, and no `overlay_decisions.json` references.

2. Verify code location in `.worktrees/gsd-phase-01-foundations-gap-closure`

- `OverlayMergeService` implementation exists in worktree only:
  - `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Build/OverlayMergeService.cs:6`
- Worktree `BundleBuilder` includes overlay merge and report writes:
  - `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Build/BundleBuilder.cs:52`
  - `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Build/BundleBuilder.cs:61`
  - `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Build/BundleBuilder.cs:62`
- Therefore, required UAT behavior exists in another worktree branch, not in the active runnable project path used during UAT.

3. Inspect overlay save path/UI persistence vs storage used by build

- Overlay editor UI writes only `PowerStigOverrides` when saving; it never populates `Overlay.Overrides` (control decision overrides used by merge path).
  - Evidence: `src/STIGForge.App/OverlayEditorViewModel.cs:82`
  - Evidence: `src/STIGForge.App/OverlayEditorViewModel.cs:85`
- Build path loads overlays from profile overlay IDs and passes them to `BundleBuilder`, but active builder has no merge/report emission path.
  - Evidence: `src/STIGForge.App/MainViewModel.Import.cs:595`
  - Evidence: `src/STIGForge.App/MainViewModel.Import.cs:600`
  - Evidence: `src/STIGForge.App/MainViewModel.Import.cs:620`
- CLI overlay editing command modifies `Overlay.Overrides` (the field expected by overlay merge logic), confirming split data paths between UI overlay editor and merge-oriented overlay decisions.
  - Evidence: `src/STIGForge.Cli/Commands/BundleCommands.cs:259`
  - Evidence: `src/STIGForge.Cli/Commands/BundleCommands.cs:265`
  - Evidence: `src/STIGForge.Cli/Commands/BundleCommands.cs:274`

## Root cause statement

UAT executed against an active branch where overlay merge/report generation was never integrated (feature exists only in `.worktrees/gsd-phase-01-foundations-gap-closure`), while the current overlay editor persists only `PowerStigOverrides` and not decision `Overrides`, so the expected overlay merge artifacts are not produced and user-added overlay rule decisions do not land in the merge-consumed path.

---
phase: 08-upgrade-rebase-operator-workflow
plan: 01
status: completed
completed_at: 2026-02-09
commits: []
requirements-completed:
  - UR-01
  - UR-02
  - UR-03
  - UR-04
---

# Plan 01 Summary

Implemented deterministic diff/rebase classification contracts and fail-closed blocking-conflict semantics across core, CLI, and WPF surfaces.

## What Was Built

- Updated `src/STIGForge.Core/Services/BaselineDiffService.cs` to:
  - Use deterministic ordering for added/removed/modified lists.
  - Classify review-required diff controls via `RequiresReview`/`ReviewReason` and `TotalReviewRequired`.
  - Expose `ReviewRequiredControls` as first-class report evidence.
- Updated `src/STIGForge.Core/Services/OverlayRebaseService.cs` to:
  - Add deterministic rebase action ordering.
  - Introduce explicit blocking-conflict semantics (`IsBlockingConflict`, `BlockingConflicts`, `HasBlockingConflicts`, `RecommendedAction`).
  - Block `ApplyRebaseAsync` when unresolved blocking conflicts remain.
- Updated CLI reporting in `src/STIGForge.Cli/Commands/DiffRebaseCommands.cs`:
  - Diff output now reports review-required counts and control-level review reasons.
  - Rebase output now reports blocking conflicts and recommended operator actions.
  - `--apply` now fails closed with actionable diagnostics when blocking conflicts exist.
- Updated WPF surfaces:
  - `src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs` now uses shared core rebase service for apply logic and blocks apply when blocking conflicts remain.
  - `src/STIGForge.App/Views/RebaseWizard.xaml` now surfaces blocking conflict count/summary and disables apply while blocked.
  - `src/STIGForge.App/ViewModels/DiffViewerViewModel.cs` and `src/STIGForge.App/Views/DiffViewer.xaml` now surface review-required classification in summary/export views.
- Updated release guidance in `docs/release/UpgradeAndRebaseValidation.md` with explicit review-required classification and fail-closed apply expectations.

## Verification

- Passed:
  - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests"`
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"`
  - `dotnet build STIGForge.sln --configuration Release -p:EnableWindowsTargeting=true`

## Notes

- Full solution build succeeds; existing nullable warnings remain in unrelated files.
- C# LSP diagnostics are unavailable in this environment (`csharp-ls` not installed); validation used build + targeted tests.

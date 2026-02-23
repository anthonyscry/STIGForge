---
phase: 04-human-resolution-and-evidence-continuity
plan: 04
status: complete
duration: ~10 min
---

# Plan 04-04 Summary: WPF Wizard Surfaces

## What was built
Export/Import buttons on ManualCheckWizard, answer impact columns in DiffViewer, and full AnswerRebaseWizard with 3-step flow mirroring RebaseWizard.

## Key files

### Created
- `src/STIGForge.App/ViewModels/AnswerRebaseWizardViewModel.cs` — 3-step wizard ViewModel with confidence scoring
- `src/STIGForge.App/Views/AnswerRebaseWizard.xaml` — Wizard XAML layout (Welcome, Analysis, Completion)
- `src/STIGForge.App/Views/AnswerRebaseWizard.xaml.cs` — Code-behind with theme inheritance and close event

### Modified
- `src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs` — Added ExportAnswers and ImportAnswers RelayCommands
- `src/STIGForge.App/Views/ManualCheckWizard.xaml` — Added Export/Import toolbar buttons on Welcome screen
- `src/STIGForge.App/ViewModels/DiffViewerViewModel.cs` — Added BundleRoot property, LoadAnswerImpact method, answer validity brushes
- `src/STIGForge.App/Views/DiffViewer.xaml` — Added Answer Status and Answer Impact columns to Changed Controls tab

## Decisions
- Export/Import buttons placed on Welcome screen alongside Start Review for easy access
- Answer impact columns added directly to existing Changed Controls grid (not a separate tab)
- AnswerRebaseWizard mirrors RebaseWizard structure exactly for consistency
- Completion screen includes cross-reference note to Overlay Rebase Wizard
- AnswerRebaseActionDisplay includes ExistingStatus from original answer for operator context

## Self-Check: PASSED
- [x] ManualCheckWizard Export/Import buttons with file dialogs
- [x] DiffViewer Answer Status column with color coding (Valid=green, Uncertain=orange, Invalid=red)
- [x] DiffViewer Answer Impact reason column
- [x] AnswerRebaseWizard Welcome screen with pack selection and bundle path
- [x] AnswerRebaseWizard Analysis screen with summary stats and action tabs
- [x] AnswerRebaseWizard Completion screen with rebased file path
- [x] All XAML views compile and bind correctly
- [x] dotnet build STIGForge.App succeeds

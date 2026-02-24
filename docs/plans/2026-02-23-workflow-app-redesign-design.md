# STIGForge Workflow App Redesign

**Date:** 2026-02-23  
**Status:** Approved  
**Approach:** Clean Slate Rebuild (Option A)

## Goal

Replace the existing multi-tab STIGForge App with a single-purpose wizard-style workflow application. Remove the CLI entirely - this is GUI-only.

## Workflow Steps

```
Step 1: Setup   → Configure paths, machine target, auto-detect defaults
Step 2: Import  → Import STIGs, show progress + found content
Step 3: Scan    → Run Evaluate-STIG, baseline findings
Step 4: Harden  → Apply PowerSTIG/DSC, GPO, ADMX templates
Step 5: Verify  → Re-run Evaluate-STIG + SCC, compare results
Step 6: Done    → Summary (pass/fail delta, unmapped), mission.json path
```

## Architecture

### UI Structure

```
MainWindow
└── WorkflowWizardView (single view, no tabs)
    ├── Step 1: SetupStepView
    ├── Step 2: ImportStepView
    ├── Step 3: ScanStepView
    ├── Step 4: HardenStepView
    ├── Step 5: VerifyStepView
    └── Step 6: DoneStepView
```

### ViewModel

Single `WorkflowViewModel` with:
- `CurrentStep` enum: `Setup | Import | Scan | Harden | Verify | Done`
- Navigation: `NextCommand`, `BackCommand`, `CanGoNext`, `CanGoBack`
- Step-specific properties: paths, progress %, status text, results
- Service dependencies: `ILocalWorkflowService`, `IApplyRunner`, `IVerificationWorkflowService`

### Step Details

**Step 1: Setup**
- Inputs: Import folder path, Evaluate-STIG tool path, SCC tool path, Output folder path
- Optional: Machine target (local or remote hostname)
- Auto-detect defaults where possible
- Validation: Required paths must exist

**Step 2: Import**
- Action: Scan import folder, parse STIG content
- Display: Progress bar, list of found STIGs/benchmarks
- Output: Canonical checklist ready for scan

**Step 3: Scan**
- Action: Run Evaluate-STIG against target machine
- Display: Progress bar, live status, findings count
- Output: Baseline scan results

**Step 4: Harden**
- Action: Apply PowerSTIG/DSC configurations, local GPO, import ADMX templates
- Display: Progress bar, applied fixes list
- Output: Hardening complete status

**Step 5: Verify**
- Action: Re-run Evaluate-STIG + SCC
- Display: Progress bar, before/after comparison
- Output: Delta results (fixed, still open, new findings)

**Step 6: Done**
- Display: Summary (pass/fail counts, delta from baseline, unmapped warnings)
- Output: Path to mission.json
- Action: Open folder button, restart workflow button

### Persistence

Settings saved to `%AppData%\STIGForge\workflow-settings.json`:
- Import folder path
- Evaluate-STIG tool path
- SCC tool path
- Output folder path
- Last used machine target

### Services Used

- `ILocalWorkflowService` - orchestrates Setup/Import/Scan stages
- `IApplyRunner` - runs PowerSTIG/DSC hardening
- `IVerificationWorkflowService` - runs Evaluate-STIG and SCC verification
- `IPathBuilder` - path resolution and defaults
- `IHashingService` - artifact integrity

## Deletions

### Remove CLI
- Delete `src/STIGForge.Cli/` project entirely
- Remove from solution file

### Remove Old Views
- Delete all files in `src/STIGForge.App/Views/` except keep `AboutDialog.xaml`
- Delete `MainViewModel.*.cs` partial files
- Delete `OverlayEditorViewModel.cs` and related

### Keep
- `App.xaml` / `App.xaml.cs` (DI, resources, theming)
- `MainWindow.xaml` (will be gutted but kept)
- Core infrastructure in `STIGForge.Core`, `STIGForge.Infrastructure`, etc.

## Success Criteria

1. App launches to Step 1 (Setup) with auto-detected defaults
2. User can navigate forward through all 6 steps
3. Each step executes its action and shows progress
4. Settings persist between sessions
5. Done step shows summary and mission.json path
6. No CLI project in solution

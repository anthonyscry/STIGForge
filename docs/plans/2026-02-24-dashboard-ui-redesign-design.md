# Dashboard UI Redesign Design

**Date**: 2026-02-24  
**Status**: Approved  
**Supersedes**: 2026-02-23-workflow-app-redesign-design.md (wizard style)

## Overview

Redesign the STIGForge WPF application from a wizard-style 6-step flow to a dashboard-style UI with 4 step panels, top-right corner buttons, and a modal settings window.

## Goals

1. Simplify the workflow to 4 clear steps: Import, Scan, Harden, Verify
2. Provide quick access to Help, About, and Settings via corner buttons
3. Enable "Auto Workflow" to run all steps sequentially
4. Show results summary with mission.json path and output folder access

## Layout Structure

```
+-------------------------------------------------------------------+
|  STIGForge                                        [?] [i] [gear]  |
+-------------------------------------------------------------------+
|                                                                   |
|  +--------------+ +--------------+ +--------------+ +------------+|
|  |   IMPORT     | |    SCAN      | |   HARDEN     | |   VERIFY   ||
|  |     [1]      |>|     [2]      |>|     [3]      |>|    [4]     ||
|  |              | |              | |              | |            ||
|  |   [Run >]    | |   [Locked]   | |   [Locked]   | |  [Locked]  ||
|  +--------------+ +--------------+ +--------------+ +------------+|
|                                                                   |
|                      [ > Auto Workflow ]                          |
|                                                                   |
+-------------------------------------------------------------------+
|  Results:  mission.json: C:\...\mission.json                      |
|            Output folder: [Open Folder]                           |
+-------------------------------------------------------------------+
```

## Components

### Top Bar
- **Title**: "STIGForge" on the left
- **Buttons** (right-aligned):
  - Help [?]: Opens help documentation or dialog
  - About [i]: Shows version and credits
  - Settings [gear]: Opens modal settings window

### Step Panels

Four horizontal panels representing the workflow stages:

| Step | Name | Description |
|------|------|-------------|
| 1 | Import | Scans inbox folder for STIG/SCAP files |
| 2 | Scan | Runs verification against current system |
| 3 | Harden | Applies remediation (placeholder - TODO) |
| 4 | Verify | Re-runs verification to confirm changes |

**Panel States**:
- **Ready** (blue border): Available to run, shows "Run" button
- **Running** (animated border): Currently executing, shows progress indicator
- **Complete** (green border + checkmark): Finished successfully
- **Locked** (gray, disabled): Waiting for previous step to complete
- **Error** (red border): Failed, shows error message with retry option

**Sequential Unlock Logic**:
- Only Import is unlocked initially
- Each step unlocks only after the previous step completes successfully
- Error in any step blocks subsequent steps until resolved

### Auto Workflow Button

- Centered below the step panels
- Runs all 4 steps in sequence automatically
- Disabled if any step is currently running
- Stops on first error and highlights the failed step

### Results Section

- **mission.json path**: Shows full path, copyable
- **Output folder**: Button to open the output folder in Explorer
- Updates after each step completes

## Settings Modal Window

Opened via the gear button in the top-right corner.

### Path Configuration
- **InboxPath**: Where STIG/SCAP files are imported from
- **OutboxPath**: Where results are exported to
- **CacheBasePath**: Local cache for downloaded content
- **MissionFilePath**: Path to mission.json

Each path has a text field and browse button.

### Export Formats
Checkboxes for output formats:
- [ ] CKL (STIG Viewer Checklist)
- [ ] CSV (Spreadsheet)
- [ ] XCCDF (SCAP Results)

### Buttons
- **Save**: Persists settings to `%AppData%\STIGForge\workflow-settings.json`
- **Cancel**: Discards changes and closes

## Existing Infrastructure

### Services (already wired)
- `ImportInboxScanner`: Handles Import step
- `IVerificationWorkflowService`: Handles Scan and Verify steps
- `ApplyRunner`: Placeholder for Harden step (TODO implementation)

### Settings Persistence
- `WorkflowSettings.cs`: Saves to `%AppData%\STIGForge\workflow-settings.json`
- Needs extension for export format checkboxes (CKL, CSV, XCCDF)

### Test Coverage
- 599 unit tests passing
- `WorkflowViewModelTests.cs`: ViewModel behavior tests
- `WorkflowSettingsTests.cs`: Settings persistence tests

## Files to Modify

| File | Changes |
|------|---------|
| `MainWindow.xaml` | Replace wizard with dashboard layout |
| `MainWindow.xaml.cs` | Update window code-behind |
| `WorkflowViewModel.cs` | Add Auto Workflow command, step state management |
| `WorkflowSettings.cs` | Add ExportCkl, ExportCsv, ExportXccdf properties |

## Files to Create

| File | Purpose |
|------|---------|
| `Views/SettingsWindow.xaml` | Modal settings dialog |
| `Views/SettingsWindow.xaml.cs` | Settings window code-behind |
| `Views/DashboardView.xaml` | Main dashboard with 4 step panels |
| `Converters/StepStateToColorConverter.cs` | Convert step state to visual styling |

## Technical Decisions

1. **Modal vs inline settings**: Modal window chosen for cleaner separation and familiar UX pattern
2. **4 steps vs 6 steps**: Consolidated to Import, Scan, Harden, Verify (removed redundant steps)
3. **Sequential unlock**: Prevents running steps out of order, ensures data dependencies are met
4. **State persistence**: Export format preferences saved alongside path settings

## Success Criteria

1. Dashboard displays 4 step panels with correct state visualization
2. Steps unlock sequentially after completion
3. Auto Workflow runs all steps in order, stopping on error
4. Settings modal saves and loads all configuration correctly
5. Results section shows mission.json path and allows folder access
6. All existing tests continue to pass
7. New functionality has test coverage

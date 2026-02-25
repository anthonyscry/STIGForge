# WPF Keyboard Tab Sequence (Workflow UI)

This document captures the intended keyboard tab order for the workflow-focused UI surfaces.
Use it as a regression checklist whenever dashboard or shell controls change.

## Scope

- `src/STIGForge.App/MainWindow.xaml`
- `src/STIGForge.App/Views/DashboardView.xaml`
- `src/STIGForge.App/Views/Controls/WorkflowStepCard.xaml`
- `src/STIGForge.App/App.xaml` (`DoneStepTemplate` action row)

## Expected Tab Sequence

### Default dashboard state (no step error shown)

1. `Run Auto Workflow (Ctrl+R)` (`TabIndex=0`)
2. `Run Import` (`TabIndex=10`)
3. `Run Scan` (`TabIndex=20`)
4. `Run Harden` (`TabIndex=30`)
5. `Run Verify` (`TabIndex=40`)
6. `Open Output Folder (Ctrl+O)` (`TabIndex=90`)
7. `Restart Workflow (Ctrl+N)` (`TabIndex=91`)
8. Header `Help` (`TabIndex=900`)
9. Header `About` (`TabIndex=901`)
10. Header `Settings` (`TabIndex=902`)

### Error state behavior

Retry buttons become visible only when their step is in `Error` state and should appear
immediately after the corresponding Run button:

- Import retry: `TabIndex=11`
- Scan retry: `TabIndex=21`
- Harden retry: `TabIndex=31`
- Verify retry: `TabIndex=41`

This preserves local run/retry adjacency in keyboard traversal.

## Non-tabbable controls by design

- Dashboard result path boxes are read-only and removed from tab order:
  - `MissionJsonPath` (`IsTabStop=False`)
  - `OutputFolderPath` (`IsTabStop=False`)
- Done-step mission path box in `App.xaml` is also `IsTabStop=False`.

## Shortcut alignment

- `Ctrl+R` -> Run auto workflow
- `Alt+I/S/H/V` -> Run Import/Scan/Harden/Verify
- `Ctrl+O` -> Open output folder
- `Ctrl+N` -> Restart workflow
- `F1` -> Help dialog

Shortcuts and tab order are intentionally complementary: users can stay on keyboard for both
sequential traversal and direct command activation.

## Manual Regression Check

1. Launch app and place focus in main window.
2. Press `Tab` repeatedly and confirm default sequence matches list above.
3. Force one step into error state; confirm retry button appears directly after that step's Run button.
4. Confirm path text boxes are skipped.
5. Confirm `F1` opens Help and does not disrupt tab sequence ordering.

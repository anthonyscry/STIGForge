# Phase 19: WPF Workflow UX Polish and Export Format Picker — Research

**Researched:** 2026-02-19
**Confidence:** HIGH on all domains

## Domain 1: WPF Verify Progress Feedback (UX-01)

### Current State
- `VerifyView.xaml` has a single `TextBlock` bound to `VerifyStatus` (line 41) and `VerifySummary` (line 43)
- `MainViewModel.ApplyVerify.cs` runs Evaluate-STIG then SCAP sequentially in `VerifyRunAsync()` (lines 70-159)
- Current flow: sets `VerifyStatus = "Verify complete."` at end — no per-tool feedback during execution
- The `Orchestrate()` method (lines 268-538) already has per-tool status logging via `OrchLog` StringBuilder — shows pattern to follow

### Implementation Approach
- Create `VerifyToolStatus` class in STIGForge.App with: `ToolName` (string), `State` (enum: Pending/Running/Complete/Failed), `ElapsedTime` (TimeSpan), `FindingCount` (int)
- Use `ObservableCollection<VerifyToolStatus>` bound to an `ItemsControl` in VerifyView.xaml
- VerifyToolStatus implements `INotifyPropertyChanged` (or extends `ObservableObject` from CommunityToolkit.Mvvm)
- DispatcherTimer (1s tick) updates ElapsedTime while State is Running — lives in viewmodel since it updates observable properties
- Theme brushes already exist: `TextMutedBrush` (Pending), `AccentBrush` (Running), `SuccessBrush` (Complete), `DangerBrush` (Failed)

### Binding Pattern
- Use DataTemplate within ItemsControl for each VerifyToolStatus row
- State-to-Brush conversion via a simple IValueConverter or DataTriggers in the DataTemplate
- FindingCount shown as "N findings" when State is Complete, blank otherwise

### Integration Points
- `VerifyRunAsync()` needs to populate status items before starting scans, update State per-tool as each scan starts/completes/fails
- After Evaluate-STIG workflow returns, extract result count from `evalWorkflow.ConsolidatedResultCount`
- Same for SCAP: `scapWorkflow.ConsolidatedResultCount`

## Domain 2: Error Recovery Guidance (UX-02)

### Current State
- `VerifyRunAsync()` catch block: `VerifyStatus = "Verify failed: " + ex.Message` — bare error message
- `ExportEmassAsync()` catch block: `ExportStatus = "Export failed: " + ex.Message` — same pattern
- `ApplyRunAsync()` already has `BuildApplyRecoveryGuidance()` — existing pattern to follow
- The `Orchestrate()` method builds structured failure lists with `blockingFailures` + `BuildApplyRecoveryGuidance()`

### Implementation Approach
- Create error panel model: `ErrorPanelInfo` with `ErrorMessage` (string), `RecoverySteps` (List<string>), `CanRetry` (bool)
- Derive recovery steps from exception type:
  - `IOException` -> "Check disk space and file permissions", "Verify the output directory exists"
  - `TimeoutException` -> "Increase scan timeout in Settings tab", "Check that the scanner tool is not hung"
  - `FileNotFoundException` -> "Verify scanner tool path in Settings tab", "Ensure the tool is installed"
  - General -> "Review the error details below", "Retry the operation", "Check tool configuration"
- Error panel in XAML: `Border` with `DangerBrush` left border (4px), contains error message TextBlock + numbered recovery steps ItemsControl + Retry button
- Panel visibility bound to `HasError` bool property; hidden when no error
- Retry button bound to same command (VerifyRunCommand or export command)

### Existing Patterns
- `BuildApplyRecoveryGuidance(bundleRoot)` returns formatted guidance string — can adapt this pattern
- `GuidedNextAction` property already surfaces operator guidance — integrate error recovery with this

## Domain 3: Export Format Picker (UX-03)

### Current State
- `ExportView.xaml` has TabControl with: Dashboard, eMASS, POA&M/CKL, Audit Log tabs
- `ExportAdapterRegistry` has `GetAll()` returning `IReadOnlyList<IExportAdapter>` and `TryResolve(formatName)`
- Five adapters exist: eMASS (`EmassExporter`), CKL (`CklExportAdapter`), XCCDF (`XccdfExportAdapter`), CSV (`CsvExportAdapter`), Excel (`ExcelExportAdapter`)
- `ExportOrchestrator` dispatches to adapter by format name
- `MainViewModel.Export.cs` has `IsBusy` pattern and `ExportPoam`/`ExportCkl` commands

### Implementation Approach
- Add "Quick Export" tab after Dashboard, before eMASS in TabControl
- ComboBox `ItemsSource` bound to `ExportFormatNames` (List<string> from registry.GetAll().Select(a => a.FormatName))
- `SelectedExportFormat` string property (ObservableProperty)
- `QuickExportSystemName` TextBox for system-name option
- `QuickExportFileName` TextBox for optional file-name stem
- Single "Export" button bound to `QuickExportCommand` (RelayCommand), disabled when `IsBusy`
- Command handler:
  1. Loads results from consolidated-results.json via `VerifyReportReader.LoadFromJson`
  2. Builds `ExportAdapterRequest` with results, BundleRoot, output directory, options
  3. Resolves adapter from registry by selected format name
  4. Calls adapter.ExportAsync()
  5. Shows success status with output path

### Registry Setup
- Register all 5 adapters at app startup (constructor or initialization)
- EmassExporter requires DI dependencies (IPathBuilder, IHashingService) — needs special handling
- CklExportAdapter, XccdfExportAdapter, CsvExportAdapter, ExcelExportAdapter are dependency-free constructors
- For Quick Export, only the simple adapters (XCCDF, CSV, Excel) may be practical since eMASS/CKL have special requirements

### Keep Existing Tabs
- eMASS and POA&M/CKL tabs stay intact — they have format-specific options the generic picker cannot provide
- Quick Export is additive, not a replacement

## Domain 4: WPF MVVM Patterns

### CommunityToolkit.Mvvm
- `[ObservableProperty]` generates INotifyPropertyChanged boilerplate
- `[RelayCommand]` generates ICommand implementations with CanExecute support
- `ObservableObject` base class provides SetProperty + OnPropertyChanged
- `ObservableCollection<T>` for list binding with automatic UI refresh

### Multi-Target Considerations
- STIGForge.App targets WPF (.NET 8 + net48) — DispatcherTimer is in `System.Windows.Threading`
- CommunityToolkit.Mvvm works on both targets
- No new NuGet dependencies needed for Phase 19

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| DispatcherTimer threading | UI thread blocking | Timer only updates TimeSpan property, no heavy work |
| EmassExporter DI for registry | Registration complexity | Register only simple adapters in Quick Export; eMASS stays in its dedicated tab |
| VerifyReportReader availability | Quick Export needs results | Guard with "Run verify first" message if no results file exists |
| ObservableCollection thread safety | Cross-thread updates | All status updates happen on UI thread via async/await continuation |

---
*Phase: 19-wpf-workflow-ux-polish-and-export-format-picker*
*Researched: 2026-02-19*

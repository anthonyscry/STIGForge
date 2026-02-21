# Phase 19: WPF Workflow UX Polish and Export Format Picker - Context

**Gathered:** 2026-02-19
**Status:** Ready for planning

<domain>
## Phase Boundary

The WPF app surfaces meaningful verify progress feedback, actionable error recovery guidance, and a single adapter-driven export format picker. This phase modifies VerifyView.xaml and ExportView.xaml (plus their viewmodels) to improve operator UX across the verify and export workflows. This phase does NOT add new export formats or change CLI commands.

</domain>

<decisions>
## Implementation Decisions

### Verify progress feedback (UX-01)
- Add a VerifyStatus progress model with fields: ToolName (string), State (enum: Pending/Running/Complete/Failed), ElapsedTime (TimeSpan), FindingCount (int)
- Bind a small ItemsControl or StackPanel below the "Run Verify" button showing one row per scanner tool (Evaluate-STIG, SCAP) with their current state
- State display uses existing theme brushes: Pending = TextMutedBrush, Running = AccentBrush, Complete = SuccessBrush, Failed = DangerBrush
- Elapsed time updates on a DispatcherTimer (1-second tick) while state is Running
- Finding count shown as "N findings" next to Complete state; blank while Pending/Running
- VerifyStatus model lives in STIGForge.App (view-only model, not in STIGForge.Verify)
- Update VerifyRunCommand handler to set status per-tool as the scan progresses

### Error recovery guidance (UX-02)
- When verify or export fails, replace the current bare error message with a structured error panel
- Error panel shows: error message (what went wrong), recovery steps (numbered list of what to try), and a "Retry" button
- Recovery guidance is derived from error type: IOException -> "Check disk space and file permissions", TimeoutException -> "Increase scan timeout in Settings tab", FileNotFoundException -> "Verify scanner tool path in Settings tab", general -> "Review error details and retry"
- Error panel uses a Border with DangerBrush left border (4px) for visual distinction
- Error messages include the exception message for diagnostics, not just a generic label
- The error panel replaces inline TextBlock status messages; it's shown/hidden via Visibility binding

### Export format picker (UX-03)
- Replace the separate per-format export sections (eMASS tab, POA&M/CKL tab) with a unified "Export" tab
- The unified tab contains: a ComboBox populated by ExportAdapterRegistry.GetAll() displaying FormatName values, a system-name TextBox, a file-name TextBox (optional), and a single "Export" button
- ComboBox ItemsSource bound to a list of registered adapter format names; SelectedItem bound to SelectedExportFormat string property
- Export button calls ExportOrchestrator (or directly resolves adapter from registry) with the selected format
- Export button is disabled while _isBusy is true (prevents double-submission)
- Keep existing eMASS and POA&M/CKL tabs intact alongside the new unified tab — they have format-specific options (host name, format variant) that the generic picker cannot provide
- The unified Export tab goes after Dashboard and before eMASS, labeled "Quick Export"
- Register all 5 adapters in the registry at app startup: eMASS (via EmassExporter), CKL (via CklExportAdapter), XCCDF, CSV, Excel

### Claude's Discretion
- Exact layout spacing and sizing of the verify progress panel
- Whether to use DataTemplate or inline XAML for the verify status rows
- Error panel exact styling (font size, padding, margin)
- Whether the DispatcherTimer for elapsed time lives in the viewmodel or code-behind
- How to structure the adapter registration (static method, DI, or inline in MainWindow)

</decisions>

<specifics>
## Specific Ideas

- The existing ExportView already has a TabControl with Dashboard, eMASS, POA&M/CKL, and Audit Log tabs — the new Quick Export tab slots into this TabControl
- ExportAdapterRegistry.GetAll() returns IReadOnlyList<IExportAdapter> — perfect for populating the ComboBox
- The existing IsBusy/ActionsEnabled pattern in MainViewModel already handles button disabling — reuse it for the export button
- The verify view currently shows a single TextBlock for VerifyStatus and VerifySummary — these need to be replaced with the structured progress panel

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 19-wpf-workflow-ux-polish-and-export-format-picker*
*Context gathered: 2026-02-19*

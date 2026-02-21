# Phase 14 Plan 02 Summary: Orchestrator Wiring, Model Bridge, CklParser Hardening, UI Restructure

**Completed:** 2026-02-18
**Requirements:** VER-02, VER-03, VER-04, VER-05

## Changes

### VerificationWorkflowService.cs
- Switched from sync `Run()` to async `RunAsync()` with timeout propagation from `TimeoutSeconds` options.
- Replaced `VerifyReportWriter.BuildFromCkls(outputRoot, toolLabel)` with orchestrator-based discovery: discovers both `*.ckl` and `*.xml` files recursively via `SearchOption.AllDirectories`.
- Wired `VerifyOrchestrator.ParseAndMergeResults(allFiles)` for unified adapter chain parsing.
- Added `ToControlResult()` bridge: maps `NormalizedVerifyResult` to `ControlResult` with correct status strings (`NotAFinding`, `Open`, `Not_Applicable`, `Not_Reviewed`, `Informational`, `Error`).
- Added `SanitizeScapArgs()` method that strips `-f` when no following filename argument exists, with exact diagnostic message: `"SCAP argument '-f' was missing a filename; removed invalid switch."`.
- Appends orchestrator diagnostic messages to workflow diagnostics.

### CklParser.cs
- Replaced `XDocument.Load(path)` with `LoadSecureXml(path)`.
- Added `LoadSecureXml()` with `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 1024`, `MaxCharactersInDocument = 20_000_000`.
- XXE-bearing XML now throws `InvalidDataException` with code `[VERIFY-CKL-XML-001]`.

### MainViewModel.cs
- Changed `scapArgs` default from `"-u -s -r -f"` to `"-u"`.
- Changed `scapIncludeF` from `= true` to no initializer (defaults to `false`).
- Added `VerifyScannerMode` enum (`Scap`, `EvaluateStig`, `Both`) and `[ObservableProperty] private VerifyScannerMode verifyScannerMode;`.

### MainViewModel.Dashboard.cs
- Updated `UpdateScapArgsFromOptions()` to guard `-f` inclusion: `var includeF = ScapIncludeF && hasValueInExtraArgs;`. Resets `ScapIncludeF = includeF;` when extra args is empty.

### VerifyView.xaml
- Restructured from flat `ScrollViewer > StackPanel` to `TabControl` with "Verify" and "Settings" tabs.
- Verify tab: bundle row, scanner mode `ComboBox` bound to `{Binding VerifyScannerMode}`, run button, status, overlap analysis.
- Settings tab: `EvaluateStigRoot`, `EvaluateStigArgs`, `ScapCommandPath`, SCAP options, extra args, SCAP args preview, SCAP label, `PowerStigModulePath`.

## Test Results

- VerificationWorkflowServiceTests: 4/4 passed.
- VerifyReportWriterTests: 11/11 passed.
- VerifyViewLayoutContractTests: 3/3 passed (tab structure, scanner mode, settings placement).
- ScapArgsOptionsContractTests: 2/2 passed (safe defaults, `-f` guard).

## Artifacts

| File | What Changed |
|------|-------------|
| `src/STIGForge.Verify/VerificationWorkflowService.cs` | Orchestrator wiring, model bridge, `-f` guard, async runner calls |
| `src/STIGForge.Verify/CklParser.cs` | `LoadSecureXml()` with DTD prohibition |
| `src/STIGForge.App/MainViewModel.cs` | Safe defaults, `VerifyScannerMode` enum/property |
| `src/STIGForge.App/MainViewModel.Dashboard.cs` | `-f` conditional guard in `UpdateScapArgsFromOptions()` |
| `src/STIGForge.App/Views/VerifyView.xaml` | Tabbed layout with Verify and Settings tabs |

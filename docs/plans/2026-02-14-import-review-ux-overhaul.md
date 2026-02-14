# Import & Review UX Overhaul Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver deterministic import-folder scanning with STIG-driven dependency selection and fix Manual Review workflow defects (filters, counters, evidence, sorting, and logging).

**Architecture:** Keep import classification and dedupe in `STIGForge.Content.Import` as deterministic services, then project a user-facing summary through `MainViewModel.Import`. Keep selection orchestration in app-layer logic but extract mapping and derivation into focused helpers so STIG remains source-of-truth and dependent content stays auto-only. For Manual Review, centralize status normalization/filter predicates/counter math in reusable service methods and keep WPF views thin.

**Tech Stack:** .NET 8 + net48 multi-target libraries, WPF/MVVM Toolkit, xUnit + FluentAssertions, JSON file persistence, existing STIGForge repositories/services.

---

### Task 1: Import Folder Convention + Scan Entry UX (Stories 1, 19)

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Modify: `src/STIGForge.App/MainViewModel.cs`

**Step 1: Write failing test**
- Add a VM-focused unit test that expects missing `<repo>/import` to be created and reported as friendly status instead of immediate failure.

**Step 2: Run test to verify it fails**
- Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter ImportInboxScannerTests`
- Expected: missing behavior assertion fails.

**Step 3: Write minimal implementation**
- Resolve scan path to `<repo>/import` first.
- Ensure `Directory.CreateDirectory(importFolder)` is always called before scan.
- Add `ImportFolderPath` bindable field and optional `Open Import Folder` command.
- Improve empty-folder status: `No importable files found`.

**Step 4: Run test to verify it passes**
- Run same filtered test command.

**Step 5: Commit**
- `git add src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/Views/ImportView.xaml src/STIGForge.App/MainViewModel.cs`
- `git commit -m "feat(app): standardize import folder scan entry workflow"`


### Task 2: Multi-Artifact Detection + Routing (Stories 2, 3 core)

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportInboxModels.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxScanner.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs`

**Step 1: Write failing tests**
- Add a zip fixture with XCCDF + OVAL + Local Policies + ADMX + tool marker.
- Expect all artifact candidates to be emitted deterministically (case-insensitive path handling).

**Step 2: Verify RED**
- Run scanner test class only.
- Expected: currently returns one candidate per zip and fails expected count assertions.

**Step 3: Minimal implementation**
- Change scanner model to emit a list of artifact candidates per archive.
- Implement deterministic route ordering: STIG/SCAP/GPO/ADMX/Tool.
- Keep tool extraction path under `.stigforge/tools` and map type-specific destination.

**Step 4: Verify GREEN**
- Re-run scanner tests.

**Step 5: Commit**
- Commit scanner/model/app routing changes + tests.


### Task 3: Deterministic Dedupe + Version Preference + Decisions (Story 4)

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportDedupService.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxModels.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs`

**Step 1: Write failing tests**
- Cover: higher `V#R#` wins, unparsable version fallback (date/hash), same version with different hash warning policy.

**Step 2: Verify RED**
- Run dedupe test class and confirm expected failures.

**Step 3: Minimal implementation**
- Extend dedupe outcomes with decision reason fields.
- Preserve stable ordering and deterministic tie-breaks.
- Emit keep/skip/replace outcomes.

**Step 4: Verify GREEN**
- Re-run dedupe tests.

**Step 5: Commit**
- Commit dedupe changes and tests.


### Task 4: Scan Summary + Persisted Logging/Export (Story 19)

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Create: `src/STIGForge.Content/Import/ImportScanSummary.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs`

**Step 1: Write failing tests**
- Assert summary object counts by type and dedupe outcomes.

**Step 2: Verify RED**
- Run targeted tests.

**Step 3: Minimal implementation**
- Build summary DTO with: imported by type, skipped/replaced, warnings/errors, conflict decisions.
- Save summary JSON/text under logs root (`.stigforge/logs/import-scan-*`).
- Surface concise status + detailed text in UI.

**Step 4: Verify GREEN**
- Re-run tests and ensure summary files are created in integration-style local run.

**Step 5: Commit**
- Commit summary model + app wiring.


### Task 5: Selection Orchestration + GPO/ADMX Separation + STIG-Only Counts (Stories 5, 6, 8, 9)

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/Views/ContentPickerDialog.xaml`
- Modify: `src/STIGForge.App/Views/ContentPickerDialog.xaml.cs`
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Modify: `src/STIGForge.App/MainViewModel.Dashboard.cs`

**Step 1: Write failing tests**
- Add app-service tests for STIG-driven derivation and non-selectable dependency behavior.
- Add count tests proving STIG-only control totals.

**Step 2: Verify RED**
- Run relevant tests.

**Step 3: Minimal implementation**
- Keep STIG as selectable source.
- Keep SCAP/GPO/ADMX as locked auto selections with explicit UI messaging.
- Ensure summary labels separate GPO and ADMX.
- Keep controls/rule count computation STIG-only.
- Preserve machine-scan abstraction hooks for remote growth.

**Step 4: Verify GREEN**
- Re-run tests.

**Step 5: Commit**
- Commit orchestration/count/UI split changes.


### Task 6: Manual Review Reliability Pack (Stories 10-18)

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Manual.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs`
- Modify: `src/STIGForge.App/Views/ManualView.xaml`
- Modify: `src/STIGForge.Evidence/EvidenceAutopilot.cs`
- Modify: `src/STIGForge.Core/Services/ManualAnswerService.cs`
- Test: `tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs`
- Test: `tests/STIGForge.UnitTests/Evidence/EvidenceAutopilotTests.cs`

**Step 1: Write failing tests**
- Add tests for: `Not reviewed only`, status filter combinations (AND), wizard counter bounds, reusable answer file compatibility, evidence zero-file reason diagnostics.

**Step 2: Verify RED**
- Run manual/evidence test classes.

**Step 3: Minimal implementation**
- Add filter flag and predicate composition.
- Fix wizard progression to stable denominator and bounded index.
- Map Evaluate-STIG imported findings into disposition comments with normalized header.
- Remove editable RuleId input and derive from selected control context.
- Adjust file path + Browse layout.
- Expand evidence collector fallbacks and explicit reason messages when 0 files collected.
- Add answer-file save/load by profile with compatibility checks.

**Step 4: Verify GREEN**
- Re-run tests and perform manual UI verification checklist.

**Step 5: Commit**
- Commit manual review package changes.


### Task 7: Sorting Everywhere (Story 7)

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml.cs`
- Modify: `src/STIGForge.App/Views/ManualView.xaml.cs`
- Modify: `src/STIGForge.App/Views/VerifyView.xaml`
- Modify: `src/STIGForge.App/Views/ExportView.xaml`
- Modify: `src/STIGForge.App/Views/ManualView.xaml`
- Create: `src/STIGForge.App/Views/Sorting/GridSortBehavior.cs`

**Step 1: Write failing tests / verification checklist**
- Add deterministic sort behavior checks (or documented UI verification if framework limitations).

**Step 2: Verify RED**
- Reproduce unsorted/non-toggle state in current grids.

**Step 3: Minimal implementation**
- Add reusable sort helper for GridView/DataGrid.
- Apply across import/manual/verify/export/fleet relevant tables.

**Step 4: Verify GREEN**
- Run available automated checks + manual click-sort checklist.

**Step 5: Commit**
- Commit sort behavior and view hooks.


### Task 8: Full Verification + Integration Wrap

**Files:**
- Modify docs as needed: `docs/UserGuide.md`, `docs/WpfGuide.md`

**Step 1: Run focused tests**
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter ImportInboxScannerTests|ImportDedupServiceTests|ManualAnswerServiceTests|EvidenceAutopilotTests`

**Step 2: Run full unit tests**
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

**Step 3: Run solution build**
- `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`

**Step 4: Validate acceptance checklist**
- Confirm import summary/logs each run.
- Confirm dependency auto-selection behavior.
- Confirm manual review filter/wizard/evidence fixes.

**Step 5: Commit docs + final fixes**
- Commit any documentation or follow-up integration tweaks.

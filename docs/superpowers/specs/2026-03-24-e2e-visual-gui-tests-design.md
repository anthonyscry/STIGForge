# E2E Visual GUI Test Suite — Design Spec

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this spec task-by-task.

**Goal:** Build comprehensive FlaUI-based E2E tests that mimic a real user operating the STIGForge WPF app across both Dashboard and Wizard modes, finding visual discrepancies and functional issues.

**Tech Stack:** .NET 8, C#, xUnit, FlaUI.Core + FlaUI.UIA3, FluentAssertions

**Build note:** No .NET SDK on Linux dev machine. Build and test on Hyper-V host `triton-ajt` via `ssh majordev@triton-ajt`. Full pipeline integration tests run on SRV01.

---

## Architecture

### Test Backend: FlaUI (local UIA3 automation)
- Drives the real Windows UI Automation framework
- ~3s/test, runs in interactive desktop session
- Existing infrastructure: `UiAppDriver`, `UiLocator` in `STIGForge.UiDriver`

### Two-Tier Assertion Model
- **Hard assertions** (xUnit Assert / FluentAssertions) — element exists, is clickable, state transitions correctly. Test FAILS.
- **Soft warnings** (VisualCheck helper) — text truncation, icon rendering, spacing, colors. Captures screenshot + logs issue. Test PASSES with warnings. Produces `visual-report.json` at end.

### Test Organization

```
tests/STIGForge.App.UiTests/
├── AppSmokeTests.cs           (existing, 6 tests)
├── DashboardNavigationTests.cs (new, 4 tests)
├── ImportFlowTests.cs          (new, 2 tests)
├── WorkflowCardTests.cs        (new, 4 tests)
├── ResultsTabTests.cs          (new, 2 tests)
├── ComplianceTabTests.cs       (new, 2 tests)
├── WizardModeTests.cs          (new, 7 tests)
├── DialogTests.cs              (new, 2 tests)
└── PipelineIntegrationTests.cs (new, 2 tests)

tests/STIGForge.UiDriver/
├── UiAppDriver.cs              (existing)
├── UiLocator.cs                (existing)
├── UIFactAttribute.cs          (existing)
├── UiTestHelpers.cs            (new — shared LocateRepositoryRoot + LocateAppExecutable)
├── VisualCheck.cs              (new — soft-assertion helper)
└── VisualDiscrepancy.cs        (new — record type)
```

---

## New Infrastructure Files

### Task 1: VisualCheck helper (`STIGForge.UiDriver/VisualCheck.cs`)

```csharp
namespace STIGForge.UiDriver;

/// <summary>
/// Soft-assertion helper for visual checks. Captures screenshots and logs
/// discrepancies without failing the test. Produces a JSON report at the end.
/// </summary>
public sealed class VisualCheck : IDisposable
{
    private readonly List<VisualDiscrepancy> _issues = new();
    private readonly string _screenshotDir;
    private readonly UiAppDriver _app;

    public VisualCheck(UiAppDriver app, string screenshotDir)
    {
        _app = app;
        _screenshotDir = screenshotDir;
        Directory.CreateDirectory(screenshotDir);
    }

    public int WarningCount => _issues.Count;
    public IReadOnlyList<VisualDiscrepancy> Issues => _issues;

    /// <summary>
    /// Check a visual condition. If false, captures a screenshot and logs the issue
    /// but does NOT fail the test.
    /// </summary>
    public void Check(string name, bool condition, string description)
    {
        if (condition) return;

        var screenshotPath = _app.CaptureScreenshot(_screenshotDir, $"warn-{name}.png");
        _issues.Add(new VisualDiscrepancy(name, description, screenshotPath, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Check that a UI element's text is not truncated (content matches expected).
    /// </summary>
    public void CheckTextNotTruncated(string name, string? actualText, string expectedSubstring)
    {
        if (actualText != null && actualText.Contains(expectedSubstring))
            return;

        var screenshotPath = _app.CaptureScreenshot(_screenshotDir, $"warn-{name}-truncated.png");
        _issues.Add(new VisualDiscrepancy(name,
            $"Text may be truncated. Expected to contain '{expectedSubstring}', got '{actualText ?? "(null)"}' ",
            screenshotPath, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Write the visual report to disk as JSON.
    /// </summary>
    public void WriteReport(string outputPath)
    {
        var report = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            TotalChecks = _issues.Count == 0 ? "all passed" : $"{_issues.Count} warnings",
            Issues = _issues
        };
        var json = System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
    }

    public void Dispose()
    {
        if (_issues.Count > 0)
        {
            var reportPath = Path.Combine(_screenshotDir, "visual-report.json");
            WriteReport(reportPath);
        }
    }
}
```

### Task 2: VisualDiscrepancy record (`STIGForge.UiDriver/VisualDiscrepancy.cs`)

```csharp
namespace STIGForge.UiDriver;

public sealed record VisualDiscrepancy(
    string Name,
    string Description,
    string ScreenshotPath,
    DateTimeOffset DetectedAt);
```

### Task 3: UiTestHelpers (`STIGForge.UiDriver/UiTestHelpers.cs`)

Move `LocateRepositoryRoot()` and `LocateAppExecutable()` from `AppSmokeTests.cs` and `AppWinAppDriverTests.cs` into a shared static class. Update both test classes to call the shared helpers.

```csharp
namespace STIGForge.UiDriver;

public static class UiTestHelpers
{
    public static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Unable to locate repository root.");
    }

    public static string LocateAppExecutable(string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Debug", "net8.0-windows", "STIGForge.App.exe"),
            Path.Combine(repoRoot, "src", "STIGForge.App", "bin", "Release", "net8.0-windows", "STIGForge.App.exe"),
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        var binRoot = Path.Combine(repoRoot, "src", "STIGForge.App", "bin");
        if (Directory.Exists(binRoot))
        {
            var discovered = Directory.EnumerateFiles(binRoot, "STIGForge.App.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(discovered))
                return discovered;
        }

        throw new FileNotFoundException("Could not locate STIGForge.App.exe.");
    }

    public static string GetScreenshotDir(string repoRoot, string testCategory)
    {
        return Path.Combine(repoRoot, ".artifacts", "e2e", testCategory);
    }
}
```

---

## Test Specifications

### Task 4: DashboardNavigationTests (4 tests)

**File:** `tests/STIGForge.App.UiTests/DashboardNavigationTests.cs`

All tests use `[UIFact]` + `[Trait("Category", "UI")]`.

1. **Dashboard_AllTabsAccessible**
   - Launch app
   - Click each tab: Import Library, Workflow, Results, Compliance Summary
   - Hard-assert: each tab's content area has expected title/heading visible
   - Screenshot after each tab switch

2. **Dashboard_TabSwitching_PreservesState**
   - Open Settings, set Import Folder path to `C:\STIGForge\import`
   - Switch to Workflow tab, then back to Import tab
   - Hard-assert: import folder path is still set (settings persisted)

3. **Dashboard_HeaderButtons_RenderWithIcons**
   - Hard-assert: Help, About, Settings buttons are visible and enabled
   - Soft-check (VisualCheck): each button has non-empty content (icon glyph rendered, not empty square)
   - Soft-check: button size is at least 30x30 pixels (not clipped)

4. **Dashboard_KeyboardShortcuts_Work**
   - Send F1 keypress
   - Hard-assert: a help dialog or window appears
   - Close it

### Task 5: ImportFlowTests (2 tests)

**File:** `tests/STIGForge.App.UiTests/ImportFlowTests.cs`

5. **Import_RunImport_PopulatesLibrary**
   - Configure import folder (via Settings or direct path entry)
   - Click "Run Import step"
   - Wait for import to complete (status text changes, ImportState = Complete)
   - Hard-assert: import tab shows checkmark in header
   - Hard-assert: ImportedItemsCount > 0 (tree view has children)
   - Soft-check: no import warnings visible (or if present, count matches expected)

6. **Import_NoFolder_ShowsError**
   - Ensure import folder path is empty or invalid
   - Click "Run Import step"
   - Hard-assert: red error message appears ("Import scanner not configured or no import folder")
   - Hard-assert: "Try Again" button appears
   - Screenshot of error state

### Task 6: WorkflowCardTests (4 tests)

**File:** `tests/STIGForge.App.UiTests/WorkflowCardTests.cs`

7. **Workflow_InitialState_ScanReadyOthersLocked**
   - Complete import first
   - Navigate to Workflow tab
   - Hard-assert: Scan card's "1 Scan" button is visible and enabled
   - Hard-assert: Harden card's "2 Harden" button is visible but disabled/locked
   - Hard-assert: Verify card's "3 Verify" button is visible but disabled/locked
   - Soft-check: Locked cards have reduced opacity

8. **Workflow_SkipScan_UnlocksHarden**
   - Complete import, navigate to Workflow tab
   - Click "Skip Scan step"
   - Hard-assert: Harden card becomes enabled (button clickable)
   - Soft-check: Scan card shows "skipped" state

9. **Workflow_RunAutoButton_Visible**
   - Complete import
   - Navigate to Workflow tab
   - Hard-assert: "Run Auto" button is visible and enabled
   - Soft-check: button text is not truncated ("Run Auto" fully visible)

10. **Workflow_ErrorState_ShowsRecoveryCard**
    - Set Evaluate-STIG path to a non-existent directory
    - Click "1 Scan"
    - Wait for error state
    - Hard-assert: failure card appears with recovery guidance
    - Hard-assert: "Open Settings" button visible in recovery card
    - Screenshot of error state with recovery card

### Task 7: ResultsTabTests (2 tests)

**File:** `tests/STIGForge.App.UiTests/ResultsTabTests.cs`

11. **Results_BeforeWorkflow_ShowsEmptyState**
    - Navigate to Results tab
    - Hard-assert: tab content is accessible
    - Soft-check: output path fields are empty or show default text

12. **Results_AfterImport_ShowsOutputPath**
    - Run import
    - Navigate to Results tab
    - Soft-check: output folder path is populated (non-empty)
    - Hard-assert: "Open Output Folder" button is visible

### Task 8: ComplianceTabTests (2 tests)

**File:** `tests/STIGForge.App.UiTests/ComplianceTabTests.cs`

13. **Compliance_BeforeWorkflow_ShowsZeroState**
    - Navigate to Compliance Summary tab
    - Hard-assert: tab content is accessible
    - Soft-check: no donut chart visible (TotalRuleCount = 0)

14. **Compliance_ChartAccessibility**
    - Navigate to Compliance Summary tab
    - Hard-assert: tab renders without crash
    - Soft-check: if chart is present, it has accessible text (AutomationName set)

### Task 9: WizardModeTests (7 tests)

**File:** `tests/STIGForge.App.UiTests/WizardModeTests.cs`

15. **Wizard_Toggle_SwitchesView**
    - Click wizard-mode-toggle
    - Hard-assert: step indicator appears with numbered circles
    - Hard-assert: "Next" and "Back" buttons appear
    - Soft-check: step circles have correct visual indicators (1,2,3,4,5,check)

16. **Wizard_StepIndicator_ShowsAllSteps**
    - Toggle to wizard mode
    - Hard-assert: all step indicators visible (Setup through Done)
    - Soft-check: current step (Setup) is highlighted, others are dim

17. **Wizard_NextBack_Navigation**
    - Toggle to wizard, configure paths on Setup step
    - Click Next
    - Hard-assert: advances to Import step (step 2 highlighted)
    - Click Back
    - Hard-assert: returns to Setup step (step 1 highlighted, paths still set)

18. **Wizard_AutoExecution_OnAdvance**
    - Toggle to wizard, configure import folder on Setup
    - Click Next to advance to Import
    - Hard-assert: import auto-starts (status text shows "Scanning import folder..." or similar)
    - Wait for completion

19. **Wizard_JumpToStep_Works**
    - Toggle to wizard, advance to step 3 (Scan)
    - Click step 1 circle in the indicator
    - Hard-assert: returns to Setup step

20. **Wizard_DoneStep_ShowsCompletion**
    - Skip through all steps to reach Done
    - Hard-assert: Done step renders with completion summary
    - Hard-assert: "Restart Workflow" button or equivalent is visible

21. **Wizard_RestartFromDone_ResetsToSetup**
    - On Done step, trigger restart
    - Hard-assert: returns to Setup step
    - Hard-assert: all step states reset (Scan/Harden/Verify not Complete)

### Task 10: DialogTests (2 tests)

**File:** `tests/STIGForge.App.UiTests/DialogTests.cs`

22. **Settings_OpenAndClose**
    - Click Settings button (gear icon)
    - Hard-assert: Settings window opens (detectable as a child window)
    - Hard-assert: "Import Folder" label/field is visible
    - Hard-assert: "Evaluate-STIG Tool Path" label/field is visible
    - Close the window
    - Hard-assert: main window regains focus

23. **About_OpenAndClose**
    - Click About button
    - Hard-assert: About dialog opens
    - Soft-check: version text is visible and non-empty
    - Close the dialog

### Task 11: PipelineIntegrationTests (2 tests)

**File:** `tests/STIGForge.App.UiTests/PipelineIntegrationTests.cs`

These tests require real tools on the target machine and run in the interactive desktop session.

24. **Pipeline_Dashboard_ImportScanHardenVerify**
    - Configure: import folder = `C:\STIGForge\import`, Evaluate-STIG = `C:\Tools\Evaluate-STIG\Evaluate-STIG`
    - Click "Run Import step" → wait for Complete
    - Hard-assert: ImportState = Complete (checkmark on tab)
    - Navigate to Workflow tab
    - Click "Skip Scan" (scan requires real SCAP content, skip for speed)
    - Click "2 Harden" → wait for Complete or Error
    - Hard-assert: HardenState = Complete or Error (not stuck in Running)
    - Screenshot at each step transition
    - Soft-check: no unexpected error messages in status text

25. **Pipeline_CklExport_AfterWorkflow**
    - After pipeline runs (test 24 as prerequisite or self-contained)
    - Verify output folder has files
    - Soft-check: at least one `.ckl` file exists in output
    - Screenshot of Results tab showing populated paths

---

## Test Execution

### Running on triton-ajt (non-UI tests excluded):

```bash
dotnet test tests/STIGForge.App.UiTests/ --configuration Release --no-build --logger "console;verbosity=normal"
```

Requires `UI_TESTS_ENABLED=true` environment variable and interactive desktop session (scheduled task pattern from earlier in this session).

### Running via phased test runner:

Tests integrate with the existing `run-all-tests.ps1` phased runner — Phase 2 (FlaUI UI tests).

### Screenshot output:

All screenshots saved to `.artifacts/e2e/<test-category>/` with descriptive filenames. Visual report saved as `visual-report.json` alongside screenshots.

---

## Migration: Deduplicate Existing Code

### Task 12: Consolidate shared helpers

- Move `LocateRepositoryRoot()` and `LocateAppExecutable()` from `AppSmokeTests.cs` and `AppWinAppDriverTests.cs` to `UiTestHelpers.cs`
- Update both test classes to call `UiTestHelpers.LocateRepositoryRoot()` and `UiTestHelpers.LocateAppExecutable(repoRoot)`
- Remove the duplicate private methods

---

## Expected Outcomes

- **25 total E2E tests** (6 existing smoke + 19 new functional + visual)
- **Two-tier results**: PASS/FAIL for functional, WARN for visual with screenshot evidence
- **Visual report**: `visual-report.json` with all discrepancies cataloged
- **Screenshot catalog**: `.artifacts/e2e/` with screenshots at each test step
- **Deduplication**: shared test helpers consolidated in `UiTestHelpers`

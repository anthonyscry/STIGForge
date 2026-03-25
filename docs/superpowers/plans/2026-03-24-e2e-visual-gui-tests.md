# E2E Visual GUI Test Suite — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build 25 FlaUI-based E2E tests covering Dashboard + Wizard modes with two-tier assertions (hard fail for functional, soft warn with screenshots for visual).

**Architecture:** Extend existing `STIGForge.App.UiTests` project using `STIGForge.UiDriver` shared infrastructure. Add `VisualCheck` soft-assertion helper for screenshot-based visual regression detection. Tests organized by user journey: navigation, import, workflow cards, results, compliance, wizard, dialogs, full pipeline.

**Tech Stack:** .NET 8, C#, xUnit 2.9.3, FlaUI.Core 5.0.0, FlaUI.UIA3 5.0.0, FluentAssertions 8.8.0

**Build note:** No .NET SDK on Linux dev machine. Build on Hyper-V host `triton-ajt` via SSH. UI tests require `UI_TESTS_ENABLED=true` and interactive desktop session (scheduled task pattern).

**Design doc:** `docs/superpowers/specs/2026-03-24-e2e-visual-gui-tests-design.md`

---

## File Map

**New files:**
1. `tests/STIGForge.UiDriver/VisualDiscrepancy.cs` — record type for visual warnings
2. `tests/STIGForge.UiDriver/VisualCheck.cs` — soft-assertion helper with screenshot capture
3. `tests/STIGForge.UiDriver/UiTestHelpers.cs` — shared LocateRepositoryRoot + LocateAppExecutable
4. `tests/STIGForge.App.UiTests/DashboardNavigationTests.cs` — 4 tests
5. `tests/STIGForge.App.UiTests/ImportFlowTests.cs` — 2 tests
6. `tests/STIGForge.App.UiTests/WorkflowCardTests.cs` — 4 tests
7. `tests/STIGForge.App.UiTests/ResultsTabTests.cs` — 2 tests
8. `tests/STIGForge.App.UiTests/ComplianceTabTests.cs` — 2 tests
9. `tests/STIGForge.App.UiTests/WizardModeTests.cs` — 7 tests
10. `tests/STIGForge.App.UiTests/DialogTests.cs` — 2 tests
11. `tests/STIGForge.App.UiTests/PipelineIntegrationTests.cs` — 2 tests

**Modified files:**
12. `tests/STIGForge.App.UiTests/AppSmokeTests.cs` — remove duplicate helpers, use UiTestHelpers
13. `tests/STIGForge.App.WinAppDriverTests/AppWinAppDriverTests.cs` — remove duplicate helpers, use UiTestHelpers

---

### Task 1: VisualDiscrepancy Record

**Files:**
- Create: `tests/STIGForge.UiDriver/VisualDiscrepancy.cs`

- [ ] **Step 1: Create the record**

```csharp
namespace STIGForge.UiDriver;

/// <summary>
/// A visual discrepancy detected during E2E testing. Logged as a warning,
/// not a test failure. Includes screenshot evidence.
/// </summary>
public sealed record VisualDiscrepancy(
    string Name,
    string Description,
    string ScreenshotPath,
    DateTimeOffset DetectedAt);
```

- [ ] **Step 2: Verify it compiles**

```bash
ssh majordev@triton-ajt 'cd C:\STIGForge && dotnet build tests/STIGForge.UiDriver/STIGForge.UiDriver.csproj -c Release /p:NuGetAudit=false 2>&1'
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add tests/STIGForge.UiDriver/VisualDiscrepancy.cs
git commit -m "feat(e2e): add VisualDiscrepancy record for soft visual assertions"
```

---

### Task 2: VisualCheck Helper

**Files:**
- Create: `tests/STIGForge.UiDriver/VisualCheck.cs`

- [ ] **Step 1: Create the VisualCheck class**

```csharp
using System.Text.Json;

namespace STIGForge.UiDriver;

/// <summary>
/// Soft-assertion helper for visual checks. Captures screenshots and logs
/// discrepancies without failing the test. Produces a JSON report on dispose.
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

    public void Check(string name, bool condition, string description)
    {
        if (condition) return;
        var path = _app.CaptureScreenshot(_screenshotDir, $"warn-{name}.png");
        _issues.Add(new VisualDiscrepancy(name, description, path, DateTimeOffset.UtcNow));
    }

    public void CheckTextNotTruncated(string name, string? actualText, string expectedSubstring)
    {
        if (actualText != null && actualText.Contains(expectedSubstring))
            return;
        var path = _app.CaptureScreenshot(_screenshotDir, $"warn-{name}-truncated.png");
        _issues.Add(new VisualDiscrepancy(name,
            $"Expected to contain '{expectedSubstring}', got '{actualText ?? "(null)"}' ",
            path, DateTimeOffset.UtcNow));
    }

    public void WriteReport(string outputPath)
    {
        var report = new { GeneratedAt = DateTimeOffset.UtcNow, WarningCount, Issues = _issues };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        if (_issues.Count > 0)
            WriteReport(Path.Combine(_screenshotDir, "visual-report.json"));
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
ssh majordev@triton-ajt 'cd C:\STIGForge && dotnet build tests/STIGForge.UiDriver/STIGForge.UiDriver.csproj -c Release /p:NuGetAudit=false 2>&1'
```

- [ ] **Step 3: Commit**

```bash
git add tests/STIGForge.UiDriver/VisualCheck.cs
git commit -m "feat(e2e): add VisualCheck soft-assertion helper with screenshot evidence"
```

---

### Task 3: UiTestHelpers (Deduplicate Shared Code)

**Files:**
- Create: `tests/STIGForge.UiDriver/UiTestHelpers.cs`
- Modify: `tests/STIGForge.App.UiTests/AppSmokeTests.cs`
- Modify: `tests/STIGForge.App.WinAppDriverTests/AppWinAppDriverTests.cs`

- [ ] **Step 1: Create UiTestHelpers with shared methods**

```csharp
namespace STIGForge.UiDriver;

/// <summary>
/// Shared test infrastructure helpers for locating the app and repo root.
/// </summary>
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
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var binRoot = Path.Combine(repoRoot, "src", "STIGForge.App", "bin");
        if (Directory.Exists(binRoot))
        {
            var found = Directory.EnumerateFiles(binRoot, "STIGForge.App.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }
        throw new FileNotFoundException("Could not locate STIGForge.App.exe.");
    }

    public static string GetScreenshotDir(string repoRoot, string testCategory)
        => Path.Combine(repoRoot, ".artifacts", "e2e", testCategory);
}
```

- [ ] **Step 2: Update AppSmokeTests to use UiTestHelpers**

Remove the private `LocateRepositoryRoot()` and `LocateAppExecutable()` methods. Replace calls with `UiTestHelpers.LocateRepositoryRoot()` and `UiTestHelpers.LocateAppExecutable(repoRoot)`.

- [ ] **Step 3: Update AppWinAppDriverTests to use UiTestHelpers**

Same as step 2. The `GetTestContext()` method (added in `/simplify`) should call `UiTestHelpers` instead of the now-removed private methods.

- [ ] **Step 4: Build and verify**

```bash
ssh majordev@triton-ajt 'cd C:\STIGForge && dotnet build STIGForge.sln -c Release /p:NuGetAudit=false 2>&1'
```

- [ ] **Step 5: Commit**

```bash
git add tests/STIGForge.UiDriver/UiTestHelpers.cs tests/STIGForge.App.UiTests/AppSmokeTests.cs tests/STIGForge.App.WinAppDriverTests/AppWinAppDriverTests.cs
git commit -m "refactor(e2e): consolidate LocateRepositoryRoot + LocateAppExecutable into UiTestHelpers"
```

---

### Task 4: DashboardNavigationTests (4 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/DashboardNavigationTests.cs`

- [ ] **Step 1: Write all 4 tests**

Each test follows the pattern: launch app → navigate → hard-assert elements → soft-check visuals → screenshot.

Tests:
1. `Dashboard_AllTabsAccessible` — click each tab, verify content heading
2. `Dashboard_TabSwitching_PreservesState` — set path, switch tabs, verify retained
3. `Dashboard_HeaderButtons_RenderWithIcons` — verify icons not empty squares
4. `Dashboard_KeyboardShortcuts_F1OpensHelp` — send F1, verify help appears

Key automation IDs used: `import-tab`, `workflow-tab`, `results-tab`, `compliance-summary-tab`, `help-button`, `about-button`, `settings-button`.

- [ ] **Step 2: Build**

```bash
ssh majordev@triton-ajt 'cd C:\STIGForge && dotnet build tests/STIGForge.App.UiTests/ -c Release /p:NuGetAudit=false 2>&1'
```

- [ ] **Step 3: Commit**

```bash
git add tests/STIGForge.App.UiTests/DashboardNavigationTests.cs
git commit -m "test(e2e): add DashboardNavigationTests — tabs, icons, keyboard shortcuts"
```

---

### Task 5: ImportFlowTests (2 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/ImportFlowTests.cs`

- [ ] **Step 1: Write both tests**

1. `Import_RunImport_PopulatesLibrary` — configure import path, click Run Import, wait for Complete, verify tree populates
2. `Import_NoFolder_ShowsError` — clear path, click Run Import, verify error message + Try Again button

These tests interact with the Import tab's `WorkflowStepCard`. Use `GetByTestId("Run Import step")` to find the button. After clicking, poll for state change by watching for checkmark text (✓) or error text (✗) in the tab header.

- [ ] **Step 2: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/ImportFlowTests.cs
git commit -m "test(e2e): add ImportFlowTests — successful import + error path"
```

---

### Task 6: WorkflowCardTests (4 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/WorkflowCardTests.cs`

- [ ] **Step 1: Write all 4 tests**

1. `Workflow_InitialState_ScanReadyOthersLocked` — after import, check button enabled/disabled states
2. `Workflow_SkipScan_UnlocksHarden` — click Skip Scan, verify Harden unlocks
3. `Workflow_RunAutoButton_Visible` — verify Run Auto is visible and enabled
4. `Workflow_ErrorState_ShowsRecoveryCard` — bad tool path → scan error → recovery card

Key: Use FlaUI's `IsEnabled` property to check locked vs ready state. Recovery card is identified by "Open Settings" button appearing.

- [ ] **Step 2: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/WorkflowCardTests.cs
git commit -m "test(e2e): add WorkflowCardTests — state machine, skip, auto, error recovery"
```

---

### Task 7: ResultsTabTests + ComplianceTabTests (4 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/ResultsTabTests.cs`
- Create: `tests/STIGForge.App.UiTests/ComplianceTabTests.cs`

- [ ] **Step 1: Write ResultsTabTests (2 tests)**

1. `Results_BeforeWorkflow_ShowsEmptyState` — navigate, verify accessible
2. `Results_AfterImport_ShowsOutputPath` — run import, check paths populated

- [ ] **Step 2: Write ComplianceTabTests (2 tests)**

1. `Compliance_BeforeWorkflow_ShowsZeroState` — navigate, verify no chart
2. `Compliance_ChartAccessibility` — verify tab renders, check AutomationName

- [ ] **Step 3: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/ResultsTabTests.cs tests/STIGForge.App.UiTests/ComplianceTabTests.cs
git commit -m "test(e2e): add ResultsTab + ComplianceTab tests — empty state, accessibility"
```

---

### Task 8: WizardModeTests (7 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/WizardModeTests.cs`

- [ ] **Step 1: Write all 7 tests**

1. `Wizard_Toggle_SwitchesView` — click toggle, verify step indicator + nav buttons
2. `Wizard_StepIndicator_ShowsAllSteps` — verify all 6 step circles visible
3. `Wizard_NextBack_Navigation` — configure paths, Next → Import, Back → Setup
4. `Wizard_AutoExecution_OnAdvance` — advance from Setup, verify import auto-starts
5. `Wizard_JumpToStep_Works` — advance to step 3, click step 1, verify returns
6. `Wizard_DoneStep_ShowsCompletion` — skip to Done, verify summary + restart button
7. `Wizard_RestartFromDone_ResetsToSetup` — restart from Done, verify reset

Key automation: Toggle via `wizard-mode-toggle`. Navigation via `GetByTestId("Go to next step")` / `GetByTestId("Go to previous step")`. Step circles found by searching for numbered TextBlocks within the step indicator.

- [ ] **Step 2: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/WizardModeTests.cs
git commit -m "test(e2e): add WizardModeTests — toggle, navigation, auto-execution, restart"
```

---

### Task 9: DialogTests (2 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/DialogTests.cs`

- [ ] **Step 1: Write both tests**

1. `Settings_OpenAndClose` — click gear, verify Settings window with fields, close, verify main focus
2. `About_OpenAndClose` — click about, verify dialog with version text, close

Key: Use `app.MainWindow.ModalWindows` to detect dialog windows. Find labels/fields inside the dialog. Close via window close button or Escape key.

- [ ] **Step 2: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/DialogTests.cs
git commit -m "test(e2e): add DialogTests — settings window + about dialog open/close"
```

---

### Task 10: PipelineIntegrationTests (2 tests)

**Files:**
- Create: `tests/STIGForge.App.UiTests/PipelineIntegrationTests.cs`

- [ ] **Step 1: Write both tests**

1. `Pipeline_Dashboard_ImportScanHardenVerify` — full workflow through the GUI with real tools. Configure paths, run Import → Skip Scan → Harden. Hard-assert each step transitions. Screenshot at each step.
2. `Pipeline_CklExport_AfterWorkflow` — verify output folder has files after pipeline.

These tests have longer timeouts (120s per step) and are designed to run on SRV01 with tools installed. They use `[Trait("Category", "Integration")]` in addition to `[UIFact]` so they can be filtered separately.

- [ ] **Step 2: Build and commit**

```bash
git add tests/STIGForge.App.UiTests/PipelineIntegrationTests.cs
git commit -m "test(e2e): add PipelineIntegrationTests — full GUI workflow + CKL export"
```

---

### Task 11: Build and Run Full Suite

- [ ] **Step 1: Full build**

```bash
ssh majordev@triton-ajt 'cd C:\STIGForge && dotnet build STIGForge.sln -c Release /p:NuGetAudit=false 2>&1'
```
Expected: 0 errors.

- [ ] **Step 2: Run non-integration E2E tests via scheduled task**

Use the phased test runner with `UI_TESTS_ENABLED=true` in interactive session. Filter out `Integration` trait for the fast pass:

```bash
dotnet test tests/STIGForge.App.UiTests/ -c Release --no-build --filter "Category=UI&Category!=Integration" --logger "console;verbosity=normal"
```

- [ ] **Step 3: Review screenshot output**

Check `.artifacts/e2e/` for screenshots and `visual-report.json`.

- [ ] **Step 4: Run integration tests separately (on SRV01 or with real tools)**

```bash
dotnet test tests/STIGForge.App.UiTests/ -c Release --no-build --filter "Category=Integration" --logger "console;verbosity=normal"
```

- [ ] **Step 5: Final commit with any fixes**

```bash
git add -A
git commit -m "test(e2e): verify full E2E suite passes — 25 tests"
```

---

### Task 12: Push and Update PR

- [ ] **Step 1: Push**

```bash
git push
```

- [ ] **Step 2: Verify all tests pass in CI**

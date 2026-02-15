# Verification-First Stabilization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Verify and stabilize the current branch by running full tests/build first, then validating high-risk UX paths and fixing any regressions with minimal, targeted changes.

**Architecture:** Start with repository-wide health checks (`dotnet test`, Windows-targeted `dotnet build`) to establish baseline correctness. If failures appear, fix the smallest responsible code path and immediately re-run impacted tests before final full rerun. Conclude with focused runtime smoke checks on flows changed in this branch (Import, Machine Scan, Remote Scan, themed dialogs).

**Tech Stack:** .NET 8, WPF (Avalonia-like MVVM pattern in STIGForge), xUnit, Git, PowerShell/dotnet CLI

---

### Task 1: Capture Verification Baseline

**Files:**
- Modify: `docs/plans/2026-02-14-verification-pass-design.md`
- Modify: `docs/plans/2026-02-14-verification-first-stabilization-plan.md`
- Test: `tests/STIGForge.UnitTests/SmokeTests.cs`

**Step 1: Record verification intent in plan checklist**

```markdown
- [ ] Full unit tests pass
- [ ] Windows-targeted build passes
- [ ] Import/Machine/Remote smoke checks pass
```

**Step 2: Run working-tree status check**

Run: `git status --short`
Expected: Shows current modified files without destructive changes.

**Step 3: Commit (only if user explicitly requests)**

```bash
git add docs/plans/2026-02-14-verification-pass-design.md docs/plans/2026-02-14-verification-first-stabilization-plan.md
git commit -m "docs: add verification-first stabilization design and plan"
```

### Task 2: Run Full Unit Test Suite

**Files:**
- Test: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs`
- Test: `tests/STIGForge.UnitTests/SmokeTests.cs`

**Step 1: Run full tests**

Run: `dotnet test`
Expected: All test projects execute; failures (if any) list concrete test names.

**Step 2: If failure appears, isolate failing test**

Run: `dotnet test --filter "FullyQualifiedName~<FailingTestName>" -v normal`
Expected: Reproduces failure in isolation.

**Step 3: Commit (only if user explicitly requests)**

```bash
git add tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs tests/STIGForge.UnitTests/SmokeTests.cs
git commit -m "test: stabilize failing coverage for verification pass"
```

### Task 3: Run Windows-Targeted Build

**Files:**
- Modify: `src/STIGForge.App/App.xaml.cs`
- Modify: `src/STIGForge.App/MainWindow.xaml.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`

**Step 1: Execute Windows-targeted build**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: Build succeeds with 0 errors.

**Step 2: If build fails, apply minimal code fix in failing file**

```csharp
// Example pattern: null-safe fallback without changing behavior
var value = source ?? string.Empty;
```

**Step 3: Re-run build to confirm fix**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: Previous error is gone.

**Step 4: Commit (only if user explicitly requests)**

```bash
git add src/STIGForge.App/App.xaml.cs src/STIGForge.App/MainWindow.xaml.cs src/STIGForge.App/MainViewModel.cs
git commit -m "fix(app): resolve build regression from verification pass"
```

### Task 4: Validate Applicability and Auto-Select Behavior

**Files:**
- Modify: `src/STIGForge.Core/Services/PackApplicabilityRules.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Test: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`

**Step 1: Write or update failing regression test first (if behavior is wrong)**

```csharp
[Fact]
public void AndroidNamedPack_DoesNotMatch_WindowsChromeTarget()
{
    // arrange/act/assert
}
```

**Step 2: Run targeted test to confirm failure**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~PackApplicabilityRulesTests"`
Expected: New test fails before implementation.

**Step 3: Implement minimal rule correction**

```csharp
if (normalizedPack.Contains("android", StringComparison.OrdinalIgnoreCase))
{
    return false;
}
```

**Step 4: Re-run targeted and full tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~PackApplicabilityRulesTests"`
Expected: Targeted tests pass.

Run: `dotnet test`
Expected: Full suite still passes.

**Step 5: Commit (only if user explicitly requests)**

```bash
git add src/STIGForge.Core/Services/PackApplicabilityRules.cs src/STIGForge.App/MainViewModel.Import.cs tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs
git commit -m "fix(selection): enforce applicability guard and stable auto-select"
```

### Task 5: Validate SCAP Dedupe Preference Behavior

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportInboxScanner.cs`
- Modify: `src/STIGForge.Content/Import/ImportDedupService.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxModels.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs`

**Step 1: Add/update failing test for same-version-different-hash SCAP conflict**

```csharp
[Fact]
public void Prefer_NiwcEnhanced_ConsolidatedBundle_WhenVersionMatches()
{
    // arrange/act/assert
}
```

**Step 2: Run targeted dedupe tests to confirm failing scenario**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportDedupServiceTests"`
Expected: New case fails before fix.

**Step 3: Implement minimal dedupe decision logic + diagnostics**

```csharp
if (sameVersionDifferentHash && preferred.IsNiwcEnhancedFromConsolidated)
{
    return preferred;
}
```

**Step 4: Re-run targeted and full tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportDedupServiceTests"`
Expected: Targeted tests pass.

Run: `dotnet test`
Expected: Full suite passes.

**Step 5: Commit (only if user explicitly requests)**

```bash
git add src/STIGForge.Content/Import/ImportInboxScanner.cs src/STIGForge.Content/Import/ImportDedupService.cs src/STIGForge.Content/Import/ImportInboxModels.cs tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs
git commit -m "fix(import): prefer consolidated NIWC enhanced SCAP on dedupe conflicts"
```

### Task 6: Runtime Smoke Validation of Updated UX

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Modify: `src/STIGForge.App/MainViewModel.Dashboard.cs`
- Modify: `src/STIGForge.App/Views/PackComparisonDialog.xaml`
- Modify: `src/STIGForge.App/Views/DiffViewer.xaml`
- Modify: `src/STIGForge.App/Views/AboutDialog.xaml`

**Step 1: Launch app and exercise Import flow**

Run: `dotnet run --project src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: App launches; Import view scroll and applicable pack panel function.

**Step 2: Exercise Machine Scan + Remote Scan flows**

Run: `dotnet run --project src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: Remote discovery populates list, selection toggles work, scan command respects WinRM state.

**Step 3: Verify theme consistency for Compare/Diff/About dialogs**

Run: `dotnet run --project src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: Dialogs respect theme brushes and remain legible.

**Step 4: If issue found, apply minimal XAML/viewmodel fix and re-check**

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto" />
```

**Step 5: Commit (only if user explicitly requests)**

```bash
git add src/STIGForge.App/Views/ImportView.xaml src/STIGForge.App/MainViewModel.Dashboard.cs src/STIGForge.App/Views/PackComparisonDialog.xaml src/STIGForge.App/Views/DiffViewer.xaml src/STIGForge.App/Views/AboutDialog.xaml
git commit -m "fix(ui): complete verification smoke fixes for import and themed dialogs"
```

### Task 7: Final Verification Gate

**Files:**
- Modify: `docs/plans/2026-02-14-verification-first-stabilization-plan.md`

**Step 1: Re-run full tests**

Run: `dotnet test`
Expected: Pass.

**Step 2: Re-run Windows-targeted build**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
Expected: Pass.

**Step 3: Record pass/fail notes in plan doc**

```markdown
- Verification completed: PASS
- Remaining follow-ups: <none or list>
```

**Step 4: Commit (only if user explicitly requests)**

```bash
git add docs/plans/2026-02-14-verification-first-stabilization-plan.md
git commit -m "docs: record verification-first stabilization results"
```

## Verification Results (2026-02-14)

- Full repository `dotnet test`: PASS (0 failed)
- App build `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`: PASS (0 errors)
- App build `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net48`: BLOCKED in this Linux environment (`MSB3644`: missing .NET Framework 4.8 reference assemblies)
- Remaining manual validation: Import/Machine/Remote runtime smoke checks (deferred to operator manual review)

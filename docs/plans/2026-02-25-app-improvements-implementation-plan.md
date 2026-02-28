# Troubleshooting UX Improvement Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add actionable Scan/Verify failure cards so operators immediately know what failed and exactly what to do next.

**Architecture:** Extend `WorkflowViewModel` with a deterministic failure-classification layer that maps raw verification/preflight evidence to stable root-cause codes and card content. Bind a new dashboard panel to this state and reuse existing commands for recovery actions. Keep detailed diagnostics in mission output while presenting concise operator guidance in the UI.

**Tech Stack:** .NET 8, WPF (XAML), CommunityToolkit.Mvvm, xUnit.

---

## Implementation Rules

- Follow @superpowers:test-driven-development for all behavior changes.
- Follow @superpowers:verification-before-completion before marking each task done.
- Keep each commit scoped to one task; avoid opportunistic refactors.

### Task 1: Add failure card state model in workflow view model

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing test**

Add test to assert card state exists and starts empty:

```csharp
[Fact]
public void FailureCardState_DefaultsToNull()
{
    var vm = new WorkflowViewModel();
    Assert.Null(vm.CurrentFailureCard);
}
```

**Step 2: Run test to verify it fails**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~FailureCardState_DefaultsToNull
```

Expected: FAIL because `CurrentFailureCard` does not exist yet.

**Step 3: Write minimal implementation**

Add nested model + root-cause enum + observable property:

```csharp
public enum WorkflowRootCauseCode
{
    ElevationRequired,
    EvaluatePathInvalid,
    NoCklOutput,
    ToolExitNonZero,
    OutputNotWritable,
    UnknownFailure
}

public sealed class WorkflowFailureCard
{
    public WorkflowRootCauseCode RootCauseCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string WhatHappened { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string Confidence { get; init; } = "Medium";
}

[ObservableProperty]
private WorkflowFailureCard? currentFailureCard;
```

**Step 4: Run test to verify it passes**

Run same command from Step 2.

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(workflow): add failure card state model"
```

---

### Task 2: Implement classifier for known Scan/Verify failure signatures

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write failing tests**

Add tests for first mapped causes:

```csharp
[Fact]
public async Task RunScanStepCommand_WhenEvaluateExitCodeFive_SetsElevationFailureCard() { ... }

[Fact]
public async Task RunScanStepCommand_WhenNoCklDiagnostic_SetsNoCklFailureCard() { ... }

[Fact]
public async Task RunScanStepCommand_WhenEvaluatePathInvalid_SetsPathFailureCard() { ... }
```

Assert `CurrentFailureCard.RootCauseCode`, `Title`, and `NextStep` contain actionable text.

**Step 2: Run tests to verify they fail**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~SetsElevationFailureCard|FullyQualifiedName~SetsNoCklFailureCard|FullyQualifiedName~SetsPathFailureCard"
```

Expected: FAIL because card mapping is not implemented.

**Step 3: Write minimal implementation**

Add classifier helpers:

```csharp
private static WorkflowFailureCard BuildFailureCard(VerificationWorkflowResult result, string stage)
{
    var evaluateRun = FindEvaluateRun(result);
    if (evaluateRun is { Executed: true, ExitCode: 5 })
        return CreateCard(WorkflowRootCauseCode.ElevationRequired, ...);

    if (HasNoCklDiagnostic(result))
        return CreateCard(WorkflowRootCauseCode.NoCklOutput, ...);

    if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
        return CreateCard(WorkflowRootCauseCode.ToolExitNonZero, ...);

    return CreateCard(WorkflowRootCauseCode.UnknownFailure, ...);
}
```

Set `CurrentFailureCard` whenever Scan/Verify transitions to error.

**Step 4: Run tests to verify pass**

Run command from Step 2.

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(workflow): classify scan failures into actionable root causes"
```

---

### Task 3: Apply classifier consistently to Verify and unknown fallback paths

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write failing tests**

Add verify-side equivalents:

```csharp
[Fact]
public async Task RunVerifyStepCommand_WhenEvaluateExitCodeFive_SetsElevationFailureCard() { ... }

[Fact]
public async Task RunVerifyStepCommand_WhenUnknownError_SetsUnknownFailureCard() { ... }
```

**Step 2: Run tests to verify fail**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~RunVerifyStepCommand_WhenEvaluateExitCodeFive_SetsElevationFailureCard|FullyQualifiedName~RunVerifyStepCommand_WhenUnknownError_SetsUnknownFailureCard"
```

Expected: FAIL.

**Step 3: Write minimal implementation**

Ensure `RunVerifyAsync()` uses the same card builder and sets `CurrentFailureCard` on all relevant error branches.

Clear card on successful stage completion:

```csharp
CurrentFailureCard = null;
```

**Step 4: Run tests to verify pass**

Run command from Step 2.

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "fix(workflow): apply failure card mapping to verify and fallback cases"
```

---

### Task 4: Render actionable failure card panel in Dashboard UI

**Files:**
- Modify: `src/STIGForge.App/Views/DashboardView.xaml`
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`

**Step 1: Add failure card panel markup**

Add a panel below workflow cards with visibility bound to `CurrentFailureCard` non-null. Include:

- Title text
- What happened text
- Next step text
- Confidence badge text
- Action buttons bound to existing commands (`ShowSettingsCommand`, `OpenOutputFolderCommand`, `RunScanStepCommand`, `RunVerifyStepCommand`)

Example binding structure:

```xml
<Border Visibility="{Binding CurrentFailureCard, Converter={StaticResource NullToVisibilityConverter}}" ...>
  <StackPanel>
    <TextBlock Text="{Binding CurrentFailureCard.Title}" ... />
    <TextBlock Text="{Binding CurrentFailureCard.WhatHappened}" ... />
    <TextBlock Text="{Binding CurrentFailureCard.NextStep}" ... />
  </StackPanel>
</Border>
```

If no null converter exists, use a simple style trigger against `CurrentFailureCard`.

**Step 2: Build to verify XAML compiles**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" build src/STIGForge.App/STIGForge.App.csproj
```

Expected: Build succeeds.

**Step 3: Commit**

```bash
git add src/STIGForge.App/Views/DashboardView.xaml src/STIGForge.App/WorkflowViewModel.cs
git commit -m "feat(ui): add actionable scan verify failure card panel"
```

---

### Task 5: Add card visibility and command-wiring behavior tests

**Files:**
- Modify: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write failing tests**

Add tests that assert:

- card is populated on mapped failure
- card clears after successful retry
- card action preconditions are valid (commands executable under expected state)

Example:

```csharp
[Fact]
public async Task RunScanStepCommand_WhenFailureThenSuccess_ClearsFailureCardOnSuccess() { ... }
```

**Step 2: Run tests to verify fail**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~ClearsFailureCardOnSuccess
```

Expected: FAIL before implementation adjustments.

**Step 3: Write minimal implementation**

Adjust success/error transitions in `WorkflowViewModel` only as required so card lifecycle is deterministic.

**Step 4: Run tests to verify pass**

Run command from Step 2 and then:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~WorkflowViewModelTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs src/STIGForge.App/WorkflowViewModel.cs
git commit -m "test(workflow): validate failure card lifecycle and command wiring"
```

---

### Task 6: Persist root-cause code in mission diagnostics

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Modify: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write failing test**

Add test verifying `mission.json` diagnostics include root-cause line when a mapped failure occurs:

```csharp
Assert.Contains("RootCause=ElevationRequired", missionJson, StringComparison.OrdinalIgnoreCase);
```

**Step 2: Run test to verify fail**

Run:

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~RootCause
```

Expected: FAIL.

**Step 3: Write minimal implementation**

Extend mission diagnostics builder:

```csharp
if (CurrentFailureCard is not null)
    lines.Add($"RootCause={CurrentFailureCard.RootCauseCode}; Stage={stage}");
```

**Step 4: Run tests to verify pass**

Run command from Step 2 and then full `WorkflowViewModelTests`.

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(workflow): include root cause code in mission diagnostics"
```

---

### Task 7: Update user guidance for actionable failure cards

**Files:**
- Modify: `docs/UserGuide.md`

**Step 1: Add concise docs section**

In verify/dashboard guidance, add:

- what the failure card means
- how to use next-step and action buttons
- where detailed diagnostics still live (`mission.json`, output folder)

**Step 2: Validate doc formatting**

Run:

```bash
git diff --check docs/UserGuide.md
```

Expected: no whitespace/errors.

**Step 3: Commit**

```bash
git add docs/UserGuide.md
git commit -m "docs: explain actionable failure cards for scan verify"
```

---

### Task 8: Full verification gate

**Files:**
- Verify only (no new files unless fixing failures)

**Step 1: Run workflow-focused tests**

```bash
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~WorkflowViewModelTests
"C:\Program Files\dotnet\dotnet.exe" test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~WorkflowSettingsTests
```

Expected: all passing.

**Step 2: Run app build**

```bash
"C:\Program Files\dotnet\dotnet.exe" build src/STIGForge.App/STIGForge.App.csproj
```

Expected: build succeeds with 0 errors.

**Step 3: Run full solution tests (optional but preferred before merge)**

```bash
"C:\Program Files\dotnet\dotnet.exe" test STIGForge.sln --no-build
```

Expected: unit and integration suites pass.

**Step 4: Commit final fixes if any**

If verification uncovered regressions and required code changes, commit them with focused message(s). If no changes were needed, do not create an empty commit.

---

## Final Merge Readiness Checklist

- All root-cause mappings implemented and tested.
- Dashboard failure card appears only on Scan/Verify failures.
- Recovery action buttons are usable and relevant.
- Mission diagnostics include root-cause code.
- Workflow and settings test suites pass.
- App builds cleanly.

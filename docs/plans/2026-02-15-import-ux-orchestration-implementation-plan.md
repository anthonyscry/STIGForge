# Import UX Orchestration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver deterministic Import content selection where STIG is the only manual source-of-truth, dependencies are auto-derived, and missing SCAP is warning-only.

**Architecture:** Introduce a deterministic `ImportSelectionOrchestrator` in `STIGForge.Core.Services` so orchestration behavior is unit-testable without WPF. Keep scanner/dedupe internals unchanged; `MainViewModel.Import` becomes a thin adapter that prepares inputs, calls the orchestrator, and applies one immutable selection plan snapshot to UI state. Preserve current canonical SCAP selection behavior by reusing `CanonicalScapSelector` and existing applicability/tag rules.

**Tech Stack:** .NET 8 + net48 multi-targeting, WPF/MVVM Toolkit, xUnit, FluentAssertions, existing STIGForge Core services

---

### Task 1: Add Deterministic Orchestration Contracts and Ordering

**Files:**
- Create: `src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs`
- Test: `tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void BuildPlan_WithShuffledInput_ProducesStableOrdering()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var input = ImportSelectionTestData.ShuffledInventoryInput();

  var result = orchestrator.BuildPlan(input);

  result.Rows.Select(r => r.PackId).Should().Equal("stig-a", "stig-b", "scap-a", "gpo-a", "admx-a");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_WithShuffledInput_ProducesStableOrdering"`
Expected: FAIL (missing orchestrator/contracts or non-deterministic ordering).

**Step 3: Write minimal implementation**

```csharp
public sealed class ImportSelectionOrchestrator
{
  public ImportSelectionPlan BuildPlan(ImportSelectionInput input)
  {
    var rows = input.Inventory
      .OrderBy(p => SortRank(p.Format))
      .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(p => p.PackId, StringComparer.OrdinalIgnoreCase)
      .Select(p => new ImportSelectionRow { PackId = p.PackId, Format = p.Format, Name = p.Name })
      .ToList();

    return new ImportSelectionPlan { Rows = rows };
  }
}
```

**Step 4: Run test to verify it passes**

Run: same command as Step 2
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs
git commit -m "feat(selection): add deterministic import orchestration baseline"
```

### Task 2: Implement STIG-Driven Dependency Closure and Warning Semantics

**Files:**
- Modify: `src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs`
- Test: `tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void BuildPlan_MissingScapForSelectedStig_KeepsStigAndEmitsWarning()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var input = ImportSelectionTestData.StigWithoutScapInput();

  var result = orchestrator.BuildPlan(input);

  result.SelectedStigPackIds.Should().Contain("stig-win11");
  result.Warnings.Should().Contain(w => w.Code == "missing_scap_dependency" && w.Severity == "warning");
}

[Fact]
public void BuildPlan_SelectedStig_AutoIncludesScapGpoAndAdmxAsLocked()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var input = ImportSelectionTestData.HappyPathInput();

  var result = orchestrator.BuildPlan(input);

  result.Rows.Should().Contain(r => r.Format == "SCAP" && r.IsSelected && r.IsLocked);
  result.Rows.Should().Contain(r => r.Format == "GPO" && r.IsSelected && r.IsLocked);
  result.Rows.Should().Contain(r => r.Format == "ADMX" && r.IsSelected && r.IsLocked);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_MissingScapForSelectedStig_KeepsStigAndEmitsWarning|FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_SelectedStig_AutoIncludesScapGpoAndAdmxAsLocked"`
Expected: FAIL (no warning contract / dependency lock behavior yet).

**Step 3: Write minimal implementation**

```csharp
foreach (var stig in selectedStigs)
{
  var candidateScap = ResolveCanonicalScap(stig, input.Inventory, input.BenchmarkIdsByPackId);
  if (candidateScap != null)
  {
    autoSelectedIds.Add(candidateScap.PackId);
  }
  else
  {
    warnings.Add(new ImportSelectionWarning
    {
      Code = "missing_scap_dependency",
      Severity = "warning",
      Message = "Selected STIG has no matching SCAP import.",
      PackId = stig.PackId
    });
  }

  foreach (var dependent in ResolveGpoAndAdmx(stig, input.Inventory, input.MachineInfo))
    autoSelectedIds.Add(dependent.PackId);
}
```

**Step 4: Run tests to verify they pass**

Run: same command as Step 2
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs
git commit -m "feat(selection): derive dependency closure with warning-only missing SCAP"
```

### Task 3: Add STIG-Only Counts and Deterministic Plan Fingerprint

**Files:**
- Modify: `src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs`
- Test: `tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void BuildPlan_CountsUseOnlySelectedStigControlsAndRules()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var input = ImportSelectionTestData.CountingInput();

  var result = orchestrator.BuildPlan(input);

  result.Counts.StigSelected.Should().Be(2);
  result.Counts.ScapAutoIncluded.Should().Be(2);
  result.Counts.RuleCount.Should().Be(420); // from STIG-only metadata in fixture
}

[Fact]
public void BuildPlan_EquivalentLogicalInput_ProducesSameFingerprint()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var first = orchestrator.BuildPlan(ImportSelectionTestData.HappyPathInput());
  var second = orchestrator.BuildPlan(ImportSelectionTestData.HappyPathInputWithDifferentInputOrder());

  second.Fingerprint.Should().Be(first.Fingerprint);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_CountsUseOnlySelectedStigControlsAndRules|FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_EquivalentLogicalInput_ProducesSameFingerprint"`
Expected: FAIL (counts/fingerprint not implemented).

**Step 3: Write minimal implementation**

```csharp
plan.Counts = new ImportSelectionCounts
{
  StigSelected = selectedStigs.Count,
  ScapAutoIncluded = selectedRows.Count(r => r.Format == "SCAP" && r.IsLocked),
  GpoAutoIncluded = selectedRows.Count(r => r.Format == "GPO" && r.IsLocked),
  AdmxAutoIncluded = selectedRows.Count(r => r.Format == "ADMX" && r.IsLocked),
  RuleCount = selectedStigs.Sum(s => s.RuleCount)
};

plan.Fingerprint = ComputeStableFingerprint(plan);
```

**Step 4: Run tests to verify they pass**

Run: same command as Step 2
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs
git commit -m "feat(selection): add STIG-only counts and deterministic plan fingerprint"
```

### Task 4: Wire Orchestrator into MainViewModel Import Flow

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/App.xaml.cs`

**Step 1: Write the failing test (contract-level in orchestrator tests)**

```csharp
[Fact]
public void BuildPlan_ProducesStatusSummaryTextUsedByViewModel()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var result = orchestrator.BuildPlan(ImportSelectionTestData.HappyPathInput());

  result.SummaryLine.Should().Be("STIG: 1 | Auto SCAP: 1 | Auto GPO: 1 | Auto ADMX: 1");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_ProducesStatusSummaryTextUsedByViewModel"`
Expected: FAIL (summary contract not present yet).

**Step 3: Write minimal implementation + app wiring**

```csharp
// MainViewModel constructor field
private readonly ImportSelectionOrchestrator _importSelectionOrchestrator;

// App.xaml.cs DI
services.AddSingleton<ImportSelectionOrchestrator>();

// MainViewModel.Import.cs usage
var input = BuildImportSelectionInput(chosenStigIds, machineInfo);
var plan = _importSelectionOrchestrator.BuildPlan(input);
ApplySelectionPlan(plan);
SelectedContentSummary = plan.SummaryLine;
StatusText = plan.Warnings.Count > 0
  ? "Content selection updated with warnings."
  : "Content selection updated. STIG is source-of-truth; dependencies auto-derived.";
```

**Step 4: Run test and app build to verify pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_ProducesStatusSummaryTextUsedByViewModel"`
Expected: PASS

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: Build succeeds with 0 errors.

**Step 5: Commit**

```bash
git add src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/App.xaml.cs src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs
git commit -m "refactor(app): route import selection through deterministic orchestrator"
```

### Task 5: Update Content Picker Presentation for Explicit Auto-Includes and Warnings

**Files:**
- Modify: `src/STIGForge.App/Views/ContentPickerDialog.xaml`
- Modify: `src/STIGForge.App/Views/ContentPickerDialog.xaml.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`

**Step 1: Write failing test (presentation contract in orchestrator tests)**

```csharp
[Fact]
public void BuildPlan_WithMissingDependency_ExposesWarningLinesForPicker()
{
  var orchestrator = new ImportSelectionOrchestrator(new CanonicalScapSelector());
  var result = orchestrator.BuildPlan(ImportSelectionTestData.StigWithoutScapInput());

  result.WarningLines.Should().Contain(line => line.Contains("missing SCAP", StringComparison.OrdinalIgnoreCase));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests.BuildPlan_WithMissingDependency_ExposesWarningLinesForPicker"`
Expected: FAIL (warning-lines contract not present).

**Step 3: Write minimal implementation and bind warning text**

```xml
<!-- ContentPickerDialog.xaml -->
<ItemsControl x:Name="WarningList" Margin="0,6,0,0" Visibility="Collapsed" />
```

```csharp
// ContentPickerDialog.xaml.cs
WarningList.ItemsSource = warningLines;
WarningList.Visibility = warningLines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

// MainViewModel.Import.cs
var dialog = new ContentPickerDialog(items, ApplicablePackIds, plan.WarningLines, BuildPickerStatus);
```

**Step 4: Run targeted tests and app build**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests"`
Expected: PASS

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/ContentPickerDialog.xaml src/STIGForge.App/Views/ContentPickerDialog.xaml.cs src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs
git commit -m "feat(ui): show deterministic auto-include state and dependency warnings"
```

### Task 6: Final Verification and Documentation Touch-Up

**Files:**
- Modify: `docs/WpfGuide.md`
- Modify: `docs/UserGuide.md`

**Step 1: Run focused deterministic orchestration tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportSelectionOrchestratorTests"`
Expected: PASS

**Step 2: Run full unit tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
Expected: PASS

**Step 3: Run solution build**

Run: `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`
Expected: Build succeeds.

**Step 4: Update docs with new behavior**

```markdown
- STIG selection is manual; SCAP/GPO/ADMX are auto-included and locked.
- Missing SCAP dependency is warning-only and does not block mission setup.
- Selection summary counts use STIG source-of-truth semantics.
```

**Step 5: Commit**

```bash
git add docs/WpfGuide.md docs/UserGuide.md
git commit -m "docs(import): document deterministic STIG-driven selection behavior"
```

## Execution Notes

- Use `@test-driven-development` before each implementation task.
- If any test behaves unexpectedly, stop and use `@systematic-debugging` before changing code.
- Before declaring completion, use `@verification-before-completion` and capture command output in the task log.

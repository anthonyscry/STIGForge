# Import Auto-Classification Workspace Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver an exception-driven Import workspace that auto-processes packs from the project `import/` folder, auto-commits clean packs, and surfaces only hard blockers for operator action.

**Architecture:** Keep the existing `MainViewModel` partial structure and current import pipeline (`ImportInboxScanner` + `ImportDedupService` + `ImportQueuePlanner` + `ContentPackImporter`). Add a small processed-artifact ledger in `STIGForge.Content` to prevent repeated imports of the same archive content, and project queue outcomes into explicit UI buckets (auto-committed, needs attention, failed). Update `ImportView.xaml` into a subtabbed workspace (Auto Import, Classification Results, Exceptions Queue, Activity Log) without adding dependencies.

**Tech Stack:** .NET 8 WPF, CommunityToolkit.Mvvm, STIGForge.Content import pipeline, xUnit, FluentAssertions, dotnet CLI

---

### Task 1: Add Processed-Artifact Ledger Tests (TDD Red)

**Files:**
- Create: `tests/STIGForge.UnitTests/Content/ImportProcessedArtifactLedgerTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportProcessedArtifactLedgerTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void TryBegin_SameHashAndRoute_OnlyFirstRunProcesses()
{
  var ledger = new ImportProcessedArtifactLedger();

  var first = ledger.TryBegin("ABC123", ContentImportRoute.ConsolidatedZip);
  var second = ledger.TryBegin("ABC123", ContentImportRoute.ConsolidatedZip);

  first.Should().BeTrue();
  second.Should().BeFalse();
}

[Fact]
public void TryBegin_SameHashDifferentRoute_TreatsAsDistinctWorkItems()
{
  var ledger = new ImportProcessedArtifactLedger();

  var gpo = ledger.TryBegin("ABC123", ContentImportRoute.ConsolidatedZip);
  var admx = ledger.TryBegin("ABC123", ContentImportRoute.AdmxTemplatesFromZip);

  gpo.Should().BeTrue();
  admx.Should().BeTrue();
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportProcessedArtifactLedgerTests"`
Expected: FAIL because `ImportProcessedArtifactLedger` does not exist yet.

**Step 3: Add minimal implementation shell**

```csharp
public sealed class ImportProcessedArtifactLedger
{
  private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

  public bool TryBegin(string sha256, ContentImportRoute route)
  {
    var normalized = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
    var key = route + ":" + normalized;
    return _keys.Add(key);
  }
}
```

Place in `src/STIGForge.Content/Import/ImportProcessedArtifactLedger.cs`.

**Step 4: Run test to verify it passes**

Run: same command as Step 2
Expected: PASS

**Step 5: Commit**

```bash
git add tests/STIGForge.UnitTests/Content/ImportProcessedArtifactLedgerTests.cs src/STIGForge.Content/Import/ImportProcessedArtifactLedger.cs
git commit -m "test(import): add processed artifact ledger coverage"
```

### Task 2: Add Ledger Persistence and Queue Projection (TDD Green)

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportProcessedArtifactLedger.cs`
- Create: `src/STIGForge.Content/Import/ImportAutoQueueProjection.cs`
- Create: `tests/STIGForge.UnitTests/Content/ImportAutoQueueProjectionTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportAutoQueueProjectionTests.cs`

**Step 1: Write the failing projection test**

```csharp
[Fact]
public void Project_SplitsRowsIntoCommittedAndExceptions()
{
  var planned = new[]
  {
    new PlannedContentImport { FileName = "a.zip", ArtifactKind = ImportArtifactKind.Stig, Route = ContentImportRoute.ConsolidatedZip },
    new PlannedContentImport { FileName = "b.zip", ArtifactKind = ImportArtifactKind.Gpo, Route = ContentImportRoute.ConsolidatedZip }
  };

  var failures = new[] { "b.zip (ConsolidatedZip): parse failed" };
  var projection = ImportAutoQueueProjection.Project(planned, failures);

  projection.AutoCommitted.Should().ContainSingle(x => x.FileName == "a.zip");
  projection.Exceptions.Should().ContainSingle(x => x.FileName == "b.zip");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportAutoQueueProjectionTests"`
Expected: FAIL because `ImportAutoQueueProjection` is missing.

**Step 3: Implement minimal projection and ledger serialization hooks**

```csharp
public sealed class ImportAutoProjectionResult
{
  public IReadOnlyList<ImportAutoQueueRow> AutoCommitted { get; init; } = Array.Empty<ImportAutoQueueRow>();
  public IReadOnlyList<ImportAutoQueueRow> Exceptions { get; init; } = Array.Empty<ImportAutoQueueRow>();
}

public static class ImportAutoQueueProjection
{
  public static ImportAutoProjectionResult Project(IReadOnlyList<PlannedContentImport> planned, IReadOnlyList<string> failures)
  {
    var failedFiles = failures
      .Select(x => x.Split('(')[0].Trim())
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var committed = planned
      .Where(x => !failedFiles.Contains(x.FileName))
      .Select(x => new ImportAutoQueueRow { FileName = x.FileName, ArtifactKind = x.ArtifactKind, State = "AutoCommitted" })
      .ToList();

    var exceptions = planned
      .Where(x => failedFiles.Contains(x.FileName))
      .Select(x => new ImportAutoQueueRow { FileName = x.FileName, ArtifactKind = x.ArtifactKind, State = "Failed" })
      .ToList();

    return new ImportAutoProjectionResult { AutoCommitted = committed, Exceptions = exceptions };
  }
}
```

Also extend `ImportProcessedArtifactLedger` with simple snapshot APIs for persistence:

```csharp
public IReadOnlyList<string> Snapshot() => _keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
public void Load(IEnumerable<string> keys) { /* clear + normalized load */ }
```

**Step 4: Run targeted tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportProcessedArtifactLedgerTests|FullyQualifiedName~ImportAutoQueueProjectionTests"`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.Content/Import/ImportProcessedArtifactLedger.cs src/STIGForge.Content/Import/ImportAutoQueueProjection.cs tests/STIGForge.UnitTests/Content/ImportAutoQueueProjectionTests.cs
git commit -m "feat(import): add auto queue projection primitives"
```

### Task 3: Add Import Workspace ViewModel Surface

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write failing XAML contract tests for new bindings and subtab headers**

```csharp
[Fact]
public void ImportView_ContainsAutoWorkspaceSubtabs()
{
  var xaml = LoadImportViewXaml();

  Assert.Contains("Header=\"Auto Import\"", xaml, StringComparison.Ordinal);
  Assert.Contains("Header=\"Classification Results\"", xaml, StringComparison.Ordinal);
  Assert.Contains("Header=\"Exceptions Queue\"", xaml, StringComparison.Ordinal);
  Assert.Contains("Header=\"Activity Log\"", xaml, StringComparison.Ordinal);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests.ImportView_ContainsAutoWorkspaceSubtabs"`
Expected: FAIL because headers do not exist yet.

**Step 3: Add ViewModel properties and row types**

Add to `MainViewModel.cs`:

```csharp
[ObservableProperty] private bool autoImportEnabled = true;
[ObservableProperty] private string autoImportStatus = "Auto import idle.";
[ObservableProperty] private int selectedImportWorkspaceTabIndex;

public ObservableCollection<ImportQueueRow> AutoImportQueueRows { get; } = new();
public ObservableCollection<ImportQueueRow> ClassificationResultRows { get; } = new();
public ObservableCollection<ImportQueueRow> ExceptionQueueRows { get; } = new();
public ObservableCollection<string> ImportActivityLogRows { get; } = new();

public sealed class ImportQueueRow
{
  public string FileName { get; set; } = string.Empty;
  public string ArtifactKind { get; set; } = string.Empty;
  public string State { get; set; } = string.Empty;
  public string Detail { get; set; } = string.Empty;
}
```

**Step 4: Build app project to validate ViewModel compilation**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/MainViewModel.cs tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "feat(import-ui): add auto workspace viewmodel surface"
```

### Task 4: Implement Auto-Import Inbox Orchestration in MainViewModel.Import

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/MainViewModel.cs`
- Modify: `src/STIGForge.Content/Import/ImportScanSummary.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`

**Step 1: Add failing test for repeated scan idempotence at content boundary**

```csharp
[Fact]
public async Task RepeatedImportScan_SameZipAndRoute_DoesNotRequeueWhenLedgerContainsKey()
{
  var ledger = new ImportProcessedArtifactLedger();
  ledger.TryBegin("abc123", ContentImportRoute.ConsolidatedZip).Should().BeTrue();

  var second = ledger.TryBegin("abc123", ContentImportRoute.ConsolidatedZip);
  second.Should().BeFalse();
}
```

**Step 2: Run targeted content tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportInboxScannerTests|FullyQualifiedName~ContentPackImporterTests|FullyQualifiedName~ImportProcessedArtifactLedgerTests"`
Expected: FAIL first on missing orchestration wiring and/or new assertions.

**Step 3: Implement orchestration reuse + ledger persistence in ViewModel**

Refactor `ScanImportFolderAsync` into a shared core method used by manual command and auto mode.

```csharp
private readonly ImportProcessedArtifactLedger _processedLedger = new();

private async Task RunImportScanAsync(bool autoMode)
{
  // scan folder -> dedup -> build plan
  // skip planned rows already in ledger (sha256 + route)
  // import remaining rows
  // project results into AutoImportQueueRows / ClassificationResultRows / ExceptionQueueRows
  // append concise lines to ImportActivityLogRows
  // persist ledger snapshot to logs/import_auto_ledger.json
}
```

Hook startup auto-mode in `LoadAsync()` after `ScanImportFolderPath` is set:

```csharp
if (AutoImportEnabled)
  _ = RunImportScanAsync(autoMode: true);
```

**Step 4: Re-run targeted tests and app build**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportProcessedArtifactLedgerTests|FullyQualifiedName~ImportAutoQueueProjectionTests|FullyQualifiedName~ImportInboxScannerTests|FullyQualifiedName~ContentPackImporterTests"`
Expected: PASS

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/MainViewModel.cs src/STIGForge.Content/Import/ImportScanSummary.cs tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
git commit -m "feat(import): auto-process inbox with exception routing"
```

### Task 5: Rework ImportView into Subtabbed Workspace

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Modify: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Add failing UI contract test for bindings to new collections**

```csharp
[Fact]
public void ImportView_BindsWorkspaceCollections()
{
  var xaml = LoadImportViewXaml();

  Assert.Contains("{Binding AutoImportQueueRows}", xaml, StringComparison.Ordinal);
  Assert.Contains("{Binding ClassificationResultRows}", xaml, StringComparison.Ordinal);
  Assert.Contains("{Binding ExceptionQueueRows}", xaml, StringComparison.Ordinal);
  Assert.Contains("{Binding ImportActivityLogRows}", xaml, StringComparison.Ordinal);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests.ImportView_BindsWorkspaceCollections|FullyQualifiedName~ImportViewLayoutContractTests.ImportView_ContainsAutoWorkspaceSubtabs"`
Expected: FAIL until XAML is updated.

**Step 3: Implement subtabbed workspace in ImportView**

Add/reshape `TabControl` sections in `ImportView.xaml`:

```xml
<TabControl SelectedIndex="{Binding SelectedImportWorkspaceTabIndex}">
  <TabItem Header="Auto Import">
    <ListView ItemsSource="{Binding AutoImportQueueRows}" />
  </TabItem>
  <TabItem Header="Classification Results">
    <ListView ItemsSource="{Binding ClassificationResultRows}" />
  </TabItem>
  <TabItem Header="Exceptions Queue">
    <ListView ItemsSource="{Binding ExceptionQueueRows}" />
  </TabItem>
  <TabItem Header="Activity Log">
    <ListBox ItemsSource="{Binding ImportActivityLogRows}" />
  </TabItem>
</TabControl>
```

Keep existing primary commands (`ScanImportFolderCommand`, `OpenImportFolderCommand`, etc.) and preserve current import-library actions.

**Step 4: Run UI contract tests and app build**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: PASS

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/ImportView.xaml tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "refactor(import-ui): add subtabbed auto-import workspace"
```

### Task 6: Final Verification and Documentation

**Files:**
- Modify: `docs/WpfGuide.md`

**Step 1: Update WPF guide with new import workflow**

Document:

```markdown
- Import now auto-processes archives from the project `import/` folder.
- The Import workspace is split into Auto Import, Classification Results, Exceptions Queue, and Activity Log subtabs.
- Clean packs auto-commit; hard blockers are isolated to Exceptions Queue.
```

**Step 2: Run full unit test suite**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
Expected: PASS (0 failed)

**Step 3: Run solution build**

Run: `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`
Expected: PASS (0 errors)

**Step 4: Commit docs + verification result artifacts if applicable**

```bash
git add docs/WpfGuide.md
git commit -m "docs(import-ui): document auto import workspace flow"
```

## Execution Notes

- Use `@test-driven-development` at the start of each task.
- If any test/build result is unexpected, stop and apply `@systematic-debugging` before changing more code.
- Before any completion claim, run `@verification-before-completion` and report command evidence.
- Keep scope YAGNI: do not add background watchers, schedulers, or new dependencies beyond what this plan requires.

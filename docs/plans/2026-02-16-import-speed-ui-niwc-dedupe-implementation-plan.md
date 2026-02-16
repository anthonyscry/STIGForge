# Import Speed, Busy Cursor, and NIWC SCAP Dedupe Priority Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep import UX responsive and deterministic by offloading heavy import work off the UI thread, showing a wait cursor while busy, and always preferring NIWC enhanced consolidated SCAP candidates for same-content hash conflicts.

**Architecture:** Keep archive imports serial for SQLite safety, but move scan/import heavy operations to background workers and apply UI updates in batched form. Extend SCAP dedupe conflict selection in `ImportDedupService` to prioritize NIWC enhanced consolidated candidates whenever the logical SCAP content key conflicts by hash. Preserve existing routing and storage behavior.

**Tech Stack:** .NET 8, WPF (MVVM Toolkit), xUnit, Moq, SQLite (Dapper)

---

### Task 1: Extend SCAP NIWC-Priority Dedupe Coverage (TDD)

**Files:**
- Modify: `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs`
- Modify: `src/STIGForge.Content/Import/ImportDedupService.cs`
- Test: `tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

**Step 1: Write the failing tests**

Add tests for SCAP groups with same `ContentKey`, different hashes, and NIWC candidate present when versions differ or one version is missing.

```csharp
[Fact]
public void Resolve_ScapHashConflictWithVersionMismatch_PrefersNiwcEnhanced()
{
  // same ContentKey, different hashes, differing VersionTag values
  // expected winner: NIWC consolidated candidate
}

[Fact]
public void Resolve_ScapHashConflictWithMissingVersion_PrefersNiwcEnhanced()
{
  // same ContentKey, different hashes, one empty VersionTag
  // expected winner: NIWC consolidated candidate
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "Resolve_ScapHashConflictWithVersionMismatch_PrefersNiwcEnhanced|Resolve_ScapHashConflictWithMissingVersion_PrefersNiwcEnhanced"`

Expected: FAIL with non-NIWC winner selected.

**Step 3: Write minimal implementation**

In `ImportDedupService.SelectPreferred`, broaden NIWC-priority condition from "same parsed version + different hash" to "same logical SCAP group + different hash", while keeping deterministic fallback when NIWC is absent.

```csharp
if (group.All(c => c.ArtifactKind == ImportArtifactKind.Scap)
    && HasDifferentHashes(group)
    && TrySelectNiwcEnhanced(group, out var niwcWinner))
{
  return (niwcWinner, group.Where(c => !ReferenceEquals(c, niwcWinner)).ToList());
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportDedupServiceTests"`

Expected: PASS for all dedupe tests.

**Step 5: Commit**

```bash
git add tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs src/STIGForge.Content/Import/ImportDedupService.cs
git commit -m "fix: prioritize NIWC enhanced SCAP in hash conflicts"
```

### Task 2: Offload Heavy Import Work and Reduce UI Churn

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`

**Step 1: Reproduce current UI stall (manual failing check)**

Run the app, start `Scan Import Folder` on large input set, and verify UI interactions are sluggish while import work is active.

Expected (pre-fix): UI appears frozen/intermittently unresponsive.

**Step 2: Implement minimal background orchestration changes**

- Offload scanner call and archive import calls to background execution (`Task.Run`) while preserving serial import order.
- Stop assigning `SelectedPack` per imported pack.
- Insert imported packs and assign selection once at end of import batch.

```csharp
var scan = await Task.Run(() => scanner.ScanAsync(importFolder, _cts.Token), _cts.Token).Unwrap();

// ...serial import loop with await Task.Run(() => _importer.ImportConsolidatedZipAsync(...)).Unwrap();

foreach (var pack in importedBatch)
  ContentPacks.Insert(0, pack);

SelectedPack = importedBatch.Count > 0 ? importedBatch[^1] : SelectedPack;
```

**Step 3: Verify behavior manually**

Run import again on same large set.

Expected:
- UI remains responsive while import is running.
- Import completes with same counts and summary semantics.

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Import.cs
git commit -m "perf: keep import responsive and reduce UI selection churn"
```

### Task 3: Add Wait Cursor During Busy Operations

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Dashboard.cs`

**Step 1: Confirm current cursor behavior (manual failing check)**

Trigger a long import and verify cursor does not switch to wait state.

Expected (pre-fix): default cursor remains.

**Step 2: Implement cursor toggle in `OnIsBusyChanged`**

Set `Mouse.OverrideCursor` on UI thread whenever `IsBusy` changes.

```csharp
partial void OnIsBusyChanged(bool value)
{
  OnPropertyChanged(nameof(ActionsEnabled));
  Application.Current.Dispatcher.Invoke(() =>
    Mouse.OverrideCursor = value ? Cursors.Wait : null);
}
```

**Step 3: Verify manually**

Trigger import and observe wait cursor while busy, then normal cursor afterward.

Expected: cursor transitions correctly and always clears.

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Dashboard.cs
git commit -m "feat: show wait cursor while busy"
```

### Task 4: Full Verification Pass

**Files:**
- Test: `tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

**Step 1: Run focused import/dedupe suites**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~STIGForge.UnitTests.Content.ImportDedupServiceTests|FullyQualifiedName~STIGForge.UnitTests.Content.ImportInboxScannerTests|FullyQualifiedName~STIGForge.UnitTests.Content.ContentPackImporterTests|FullyQualifiedName~STIGForge.UnitTests.Content.ImportQueuePlannerTests"`

Expected: PASS.

**Step 2: Run full unit suite**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

Expected: PASS with zero failures.

**Step 3: Manual smoke check in app**

- Run app.
- Execute `Scan Import Folder`.
- Validate status updates, wait cursor, and final library counts.
- Inspect latest import scan log under `.stigforge/logs/` for dedupe decision notes.

**Step 4: Commit verification-related updates if needed**

```bash
git add .
git commit -m "test: verify import responsiveness and dedupe behavior"
```

### Task 5: Documentation Update

**Files:**
- Modify: `docs/plans/2026-02-16-import-speed-ui-niwc-dedupe-design.md`

**Step 1: Record implementation notes**

- Add a short "Implementation Notes" section listing final behavior and verification command outputs.

**Step 2: Verify docs readability**

Read through both design and implementation plan docs for consistency.

**Step 3: Commit**

```bash
git add docs/plans/2026-02-16-import-speed-ui-niwc-dedupe-design.md docs/plans/2026-02-16-import-speed-ui-niwc-dedupe-implementation-plan.md
git commit -m "docs: capture import responsiveness and NIWC dedupe plan"
```

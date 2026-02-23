# Phase 01 Foundations Gap Closure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close the two verified Phase 01 blockers by (1) producing real SHA-256 metadata for directory-based imports and (2) wiring deterministic overlay precedence/conflict handling into bundle build/apply flow.

**Architecture:** Keep import hashing in `STIGForge.Content` and keep overlay merge logic in `STIGForge.Build` so domain contracts stay stable and deterministic outputs remain centralized. Build should emit merged override artifacts and conflict reports, and orchestration should consume merged decisions for apply-time control filtering.

**Tech Stack:** .NET 8, xUnit, existing `IHashingService`, `BundleBuilder`, `BundleOrchestrator`, `ControlRecord`/`Overlay` models.

---

### Task 1: Fix Directory Import Hash Semantics

**Files:**
- Modify: `src/STIGForge.Content/Import/ContentPackImporter.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterDirectoryHashTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task ImportDirectoryAsPackAsync_ComputesRealManifestHash()
{
  using var fixture = new DirectoryImportFixture();
  fixture.WriteFile("a/xccdf.xml", "<Benchmark id='a' />");
  fixture.WriteFile("b/notes.txt", "deterministic");

  var importer = fixture.CreateImporter();
  var pack = await importer.ImportDirectoryAsPackAsync(
    fixture.SourceRoot,
    "dir-pack",
    "unit-test",
    CancellationToken.None);

  Assert.Matches("^[0-9a-f]{64}$", pack.ManifestSha256);
  Assert.NotEqual(pack.PackId, pack.ManifestSha256);
}

[Fact]
public async Task ImportDirectoryAsPackAsync_IsDeterministicAcrossFileOrder()
{
  using var fixture = new DirectoryImportFixture();
  fixture.WriteFile("z/file2.txt", "two");
  fixture.WriteFile("a/file1.txt", "one");

  var importer = fixture.CreateImporter();
  var first = await importer.ImportDirectoryAsPackAsync(fixture.SourceRoot, "pack-1", "unit-test", CancellationToken.None);
  var second = await importer.ImportDirectoryAsPackAsync(fixture.SourceRoot, "pack-2", "unit-test", CancellationToken.None);

  Assert.Equal(first.ManifestSha256, second.ManifestSha256);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterDirectoryHashTests"`

Expected: FAIL because `ManifestSha256` is currently set to `packId` in directory import path.

**Step 3: Write minimal implementation**

```csharp
private async Task<string> ComputeDirectoryManifestHashAsync(string root, CancellationToken ct)
{
  var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

  var lines = new List<string>(files.Count);
  foreach (var file in files)
  {
    ct.ThrowIfCancellationRequested();
    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
    var fileHash = await _hash.Sha256FileAsync(file, ct).ConfigureAwait(false);
    lines.Add(rel + ":" + fileHash);
  }

  var manifestPayload = string.Join("\n", lines);
  return await _hash.Sha256TextAsync(manifestPayload, ct).ConfigureAwait(false);
}
```

Then replace directory import assignment:

```csharp
ManifestSha256 = await ComputeDirectoryManifestHashAsync(rawRoot, ct).ConfigureAwait(false),
```

**Step 4: Run test to verify it passes**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterDirectoryHashTests"`
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Content/Import/ContentPackImporter.cs tests/STIGForge.UnitTests/Content/ContentPackImporterDirectoryHashTests.cs
git commit -m "fix(import): compute deterministic hash metadata for directory imports"
```

### Task 2: Add Deterministic Overlay Merge Engine

**Files:**
- Create: `src/STIGForge.Build/OverlayMergeService.cs`
- Test: `tests/STIGForge.UnitTests/Build/OverlayMergeServiceTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Merge_LaterOverlayWins_WithDeterministicConflictOrder()
{
  var baseline = TestData.Compiled("SV-100", ControlStatus.Open);
  var overlays = new[]
  {
    TestData.Overlay("ov-1", ("SV-100", ControlStatus.NotApplicable, "n/a 1")),
    TestData.Overlay("ov-2", ("SV-100", ControlStatus.Fail, "override fail"))
  };

  var result = new OverlayMergeService().Merge(baseline, overlays);

  Assert.Equal(ControlStatus.Fail, result.Controls.Single().Status);
  Assert.Single(result.Conflicts);
  Assert.Equal("RULE:SV-100", result.Conflicts[0].ControlKey);
  Assert.Equal("ov-2", result.Conflicts[0].WinningOverlayId);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~OverlayMergeServiceTests"`

Expected: FAIL because `OverlayMergeService` does not exist yet.

**Step 3: Write minimal implementation**

```csharp
public sealed class OverlayMergeService
{
  public OverlayMergeResult Merge(IReadOnlyList<CompiledControl> baseline, IReadOnlyList<Overlay> overlays)
  {
    var controls = baseline.ToDictionary(GetControlKey, c => c, StringComparer.OrdinalIgnoreCase);
    var winners = new Dictionary<string, (string OverlayId, ControlOverride Override)>(StringComparer.OrdinalIgnoreCase);
    var conflicts = new List<OverlayConflict>();

    foreach (var overlay in overlays)
    {
      foreach (var ovr in (overlay.Overrides ?? Array.Empty<ControlOverride>()).OrderBy(GetOverrideKey, StringComparer.OrdinalIgnoreCase))
      {
        var key = GetOverrideKey(ovr);
        if (!controls.TryGetValue(key, out var current))
          continue;

        if (winners.TryGetValue(key, out var prior) && IsConflict(prior.Override, ovr))
          conflicts.Add(OverlayConflict.From(key, prior.OverlayId, overlay.OverlayId, prior.Override, ovr));

        controls[key] = new CompiledControl(
          current.Control,
          ovr.StatusOverride ?? current.Status,
          string.IsNullOrWhiteSpace(ovr.NaReason) ? current.Comment : ovr.NaReason,
          false,
          null);

        winners[key] = (overlay.OverlayId, ovr);
      }
    }

    return new OverlayMergeResult(
      controls.Values.OrderBy(c => c.Control.ExternalIds.RuleId, StringComparer.OrdinalIgnoreCase).ToList(),
      conflicts.OrderBy(c => c.ControlKey, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.WinningOverlayId, StringComparer.OrdinalIgnoreCase).ToList());
  }
}
```

Include model records in same file for deterministic serialization/reporting:
- `OverlayMergeResult`
- `OverlayConflict`
- `OverlayAppliedDecision`

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~OverlayMergeServiceTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Build/OverlayMergeService.cs tests/STIGForge.UnitTests/Build/OverlayMergeServiceTests.cs
git commit -m "feat(build): add deterministic overlay precedence and conflict engine"
```

### Task 3: Wire Overlay Merge into Bundle Build Outputs

**Files:**
- Modify: `src/STIGForge.Build/BundleBuilder.cs`
- Test: `tests/STIGForge.UnitTests/Build/BundleBuilderOverlayMergeTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task BuildAsync_WritesMergedOverlayArtifacts_AndUsesMergedStatuses()
{
  var fixture = await BundleBuilderFixture.CreateAsync();
  var request = fixture.CreateRequestWithOverlayConflict();

  var result = await fixture.Builder.BuildAsync(request, CancellationToken.None);

  var reportsRoot = Path.Combine(result.BundleRoot, "Reports");
  Assert.True(File.Exists(Path.Combine(reportsRoot, "overlay_conflicts.csv")));
  Assert.True(File.Exists(Path.Combine(reportsRoot, "overlay_decisions.json")));

  var reviewCsv = File.ReadAllText(Path.Combine(reportsRoot, "review_required.csv"));
  Assert.DoesNotContain("SV-100", reviewCsv); // overlay marks this control NotApplicable
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleBuilderOverlayMergeTests"`

Expected: FAIL because merge artifacts are not written and merged statuses are not used.

**Step 3: Write minimal implementation**

In `BuildAsync` after scope compilation:

```csharp
var merge = new OverlayMergeService().Merge(compiled.Controls, request.Overlays);
var mergedControls = merge.Controls;
var reviewQueue = mergedControls.Where(c => c.NeedsReview).ToList();

WriteOverlayConflictReport(Path.Combine(reportsDir, "overlay_conflicts.csv"), merge.Conflicts);
WriteOverlayDecisionReport(Path.Combine(reportsDir, "overlay_decisions.json"), merge.Decisions);
```

Update report writers to consume `mergedControls` instead of `compiled.Controls`.

Add deterministic writer helpers:
- `WriteOverlayConflictReport(...)`
- `WriteOverlayDecisionReport(...)`

**Step 4: Run test to verify it passes**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleBuilderOverlayMergeTests"`
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~SmokeTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Build/BundleBuilder.cs tests/STIGForge.UnitTests/Build/BundleBuilderOverlayMergeTests.cs
git commit -m "feat(build): emit deterministic overlay merge decisions and conflicts"
```

### Task 4: Consume Merged Decisions During Orchestration

**Files:**
- Modify: `src/STIGForge.Build/BundleOrchestrator.cs`
- Test: `tests/STIGForge.UnitTests/Build/BundleOrchestratorControlOverrideTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void LoadBundleControls_ExcludesNotApplicableRulesFromMergedDecisions()
{
  using var fixture = OrchestratorFixture.Create();
  fixture.WritePackControls("SV-1", "SV-2");
  fixture.WriteOverlayDecisions(("RULE:SV-2", "NotApplicable"));

  var controls = BundleOrchestratorTestProxy.LoadBundleControlsForTest(fixture.BundleRoot);
  Assert.Single(controls);
  Assert.Equal("SV-1", controls[0].ExternalIds.RuleId);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleOrchestratorControlOverrideTests"`

Expected: FAIL because orchestrator currently ignores merged control decisions.

**Step 3: Write minimal implementation**

In `BundleOrchestrator`:

```csharp
private static HashSet<string> LoadExcludedRuleIds(string bundleRoot)
{
  var path = Path.Combine(bundleRoot, "Reports", "overlay_decisions.json");
  if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

  var decisions = JsonSerializer.Deserialize<List<OverlayAppliedDecision>>(File.ReadAllText(path),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

  return decisions
    .Where(d => string.Equals(d.FinalStatus, "NotApplicable", StringComparison.OrdinalIgnoreCase))
    .Select(d => d.RuleId)
    .Where(id => !string.IsNullOrWhiteSpace(id))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
```

Filter controls before `PowerStigDataGenerator.CreateFromControls(...)`.

**Step 4: Run test to verify it passes**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleOrchestratorControlOverrideTests"`
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~PowerStigDataGeneratorTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Build/BundleOrchestrator.cs tests/STIGForge.UnitTests/Build/BundleOrchestratorControlOverrideTests.cs
git commit -m "feat(orchestrate): honor merged overlay control decisions during apply"
```

### Task 5: Phase 01 Gate Re-Verification

**Files:**
- Modify: `.planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md`
- Create: `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md`

**Step 1: Write the failing verification checklist first**

```markdown
- [ ] Directory imports emit real 64-char SHA-256 manifest hash
- [ ] Overlay merge precedence is deterministic and conflict-visible
- [ ] Bundle reports include overlay conflict + decision artifacts
- [ ] Apply path excludes NotApplicable override decisions where intended
```

**Step 2: Run verification commands**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterDirectoryHashTests|FullyQualifiedName~OverlayMergeServiceTests|FullyQualifiedName~BundleBuilderOverlayMergeTests|FullyQualifiedName~BundleOrchestratorControlOverrideTests"`
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
- `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`

Expected: PASS.

**Step 3: Update verification report with evidence**

Update `.planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md`:
- Mark Truth #2 and Truth #3 as VERIFIED.
- Add exact test/build command evidence and artifact file paths.

**Step 4: Create phase summary**

Write `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md` with:
- implemented files
- evidence commands/results
- remaining manual checks (if any)

**Step 5: Commit**

```bash
git add .planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md .planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md
git commit -m "docs(phase-01): close verification gaps with deterministic import and overlay merge evidence"
```

## Verification Sequence

1. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterDirectoryHashTests"`
2. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~OverlayMergeServiceTests"`
3. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleBuilderOverlayMergeTests"`
4. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleOrchestratorControlOverrideTests"`
5. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
6. `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`

## Success Criteria

- Directory import path no longer uses `packId` as hash surrogate.
- Phase 01 truth #2 (hash metadata) verifies for ZIP and directory routes.
- Overlay precedence/conflicts are deterministic, visible, and tested.
- Build/apply path consumes merged overlay decisions for control handling.
- Phase 01 verification report can be advanced from `gaps_found` toward gate pass.

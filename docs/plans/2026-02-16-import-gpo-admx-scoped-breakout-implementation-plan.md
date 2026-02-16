# Import GPO/ADMX Scoped Breakout Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Import ADMX and GPO content from DISA bundle ZIPs as meaningful scoped packs (ADMX by template folder, LGPO by OS scope, domain GPO by domain scope) without showing outer bundle placeholder packs.

**Architecture:** Keep existing scan -> dedup -> queue -> importer flow, but make route handlers scope-aware. `ImportAdmxTemplatesFromZipAsync` becomes strict `ADMX Templates/*` folder-grouped import; `ImportConsolidatedZipAsync` imports local and domain GPO scoped roots directly and skips outer fallback pack creation when scoped roots are found.

**Tech Stack:** C#/.NET 8, xUnit, Moq, WPF MVVM, existing `STIGForge.Content.Import` pipeline.

---

### Task 1: Add failing tests for strict ADMX folder grouping

**Files:**
- Modify: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`

**Step 1: Write the failing tests**

Add tests:
1. `ImportAdmxTemplatesFromZipAsync_GroupsByTemplateFolder_NotPerFile`
2. `ImportAdmxTemplatesFromZipAsync_StrictScope_IgnoresAdmxOutsideAdmxTemplatesFolder`

Use a fixture ZIP with entries like:
- `ADMX Templates/Microsoft/windows.admx`
- `ADMX Templates/Microsoft/edge.admx`
- `ADMX Templates/Office 2016-2019-M365/office.admx`
- `misc/rogue.admx` (must be ignored in strict mode)

Expected assertions:
- Imported pack count equals number of template folders (2 in this fixture).
- No pack generated from `misc/rogue.admx`.
- Pack names begin with `ADMX Templates - ` and include folder identity.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportAdmxTemplatesFromZipAsync_GroupsByTemplateFolder_NotPerFile|FullyQualifiedName~ImportAdmxTemplatesFromZipAsync_StrictScope_IgnoresAdmxOutsideAdmxTemplatesFolder"`

Expected: FAIL (current implementation imports one pack per `.admx` file).

**Step 3: Commit**

```bash
git add tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
git commit -m "test: capture strict ADMX folder grouping behavior"
```

### Task 2: Implement strict ADMX scoped grouping

**Files:**
- Modify: `src/STIGForge.Content/Import/ContentPackImporter.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`

**Step 1: Write minimal implementation**

In `ImportAdmxTemplatesFromZipAsync`:
1. Enumerate `.admx` only under `ADMX Templates/<folder>/...`.
2. Group files by immediate `<folder>` under `ADMX Templates`.
3. For each group, stage all group files into one temp root and call `ImportDirectoryAsPackAsync` once.
4. Build name as `ADMX Templates - <folder>`.
5. Ignore `.admx` outside this subtree and collect warning text for trace output.

Minimal helper shape:

```csharp
private static bool TryGetAdmxTemplateFolderScope(string fullPath, string extractionRoot, out string scope)
{
  scope = string.Empty;
  // derive relative path and parse "ADMX Templates/<scope>/..."
}
```

**Step 2: Run tests to verify pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportAdmxTemplatesFromZipAsync_GroupsByTemplateFolder_NotPerFile|FullyQualifiedName~ImportAdmxTemplatesFromZipAsync_StrictScope_IgnoresAdmxOutsideAdmxTemplatesFolder"`

Expected: PASS.

**Step 3: Commit**

```bash
git add src/STIGForge.Content/Import/ContentPackImporter.cs tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
git commit -m "feat: import ADMX by template folder in strict scope mode"
```

### Task 3: Add failing tests for local/domain GPO scoped split and no outer fallback pack

**Files:**
- Modify: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`

**Step 1: Write the failing test**

Add test:
- `ImportConsolidatedZipAsync_SplitsLocalPoliciesByOsAndDomainPoliciesByDomain_WithoutOuterPack`

Fixture ZIP entries:
- `Support Files/Local Policies/Windows 11/local.pol`
- `Support Files/Local Policies/Windows Server 2022/local.pol`
- `gpos/example.com/DomainSysvol/GPO/{GUID}/Machine/registry.pol`

Expected assertions:
- Imported packs include:
  - `Local Policy - Windows 11`
  - `Local Policy - Windows Server 2022`
  - `Domain GPO - example.com`
- No imported pack name equals outer zip base name.
- `SourceLabel` values:
  - local => `gpo_lgpo_import`
  - domain => `gpo_domain_import`

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportConsolidatedZipAsync_SplitsLocalPoliciesByOsAndDomainPoliciesByDomain_WithoutOuterPack"`

Expected: FAIL (current implementation may create single outer pack fallback).

**Step 3: Commit**

```bash
git add tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
git commit -m "test: cover scoped LGPO/domain GPO split without outer fallback pack"
```

### Task 4: Implement GPO scoped split in consolidated importer

**Files:**
- Modify: `src/STIGForge.Content/Import/ContentPackImporter.cs`
- Test: `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs`

**Step 1: Write minimal implementation**

Inside `ImportConsolidatedZipAsync` when no nested ZIPs:
1. Discover local roots:
   - `Support Files/Local Policies/<scope>` (or root itself if no child scope and contains files).
2. Discover domain roots:
   - `gpos/<scope>` (or root itself if no child scope and contains files).
3. Import each discovered root with `ImportDirectoryAsPackAsync`.
4. Return imported scoped packs immediately when any scoped roots exist.

Suggested helper methods:
- `GetGpoScopedImportRoots(string extractionRoot)`
- `HasAnyFiles(string root)`

**Step 2: Run targeted tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportConsolidatedZipAsync_SplitsLocalPoliciesByOsAndDomainPoliciesByDomain_WithoutOuterPack|FullyQualifiedName~ImportZipRoutes_ForNoLeadingDotGpoSupportFiles_CompletesBothRoutesWithoutHang"`

Expected: PASS.

**Step 3: Commit**

```bash
git add src/STIGForge.Content/Import/ContentPackImporter.cs tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
git commit -m "feat: split consolidated GPO bundles into local and domain scoped packs"
```

### Task 5: Ensure scanner detects domain GPO structures

**Files:**
- Modify: `src/STIGForge.Content/Import/ImportInboxScanner.cs`
- Modify: `tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs`
- Test: `tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs`

**Step 1: Write failing test**

Add test:
- `ScanAsync_DetectsDomainGpoStructureUnderGpos`

Fixture ZIP entries:
- `gpos/example.com/DomainSysvol/GPO/{GUID}/Machine/registry.pol`

Expected:
- Candidate with `ArtifactKind == ImportArtifactKind.Gpo` exists.

**Step 2: Run test to verify fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ScanAsync_DetectsDomainGpoStructureUnderGpos"`

Expected: FAIL if scanner only keys on local policy path.

**Step 3: Implement minimal scanner change**

In `BuildCandidatesAsync`:
1. Add `hasDomainGpoObjects` detection for `/gpos/` path signals.
2. Set `hasGpo = hasLocalPolicies || hasDomainGpoObjects`.
3. Add clear `Reasons` message for domain structure.

**Step 4: Run test to verify pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ScanAsync_DetectsDomainGpoStructureUnderGpos"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Content/Import/ImportInboxScanner.cs tests/STIGForge.UnitTests/Content/ImportInboxScannerTests.cs
git commit -m "fix: detect domain GPO bundles from gpos directory structure"
```

### Task 6: Add domain GPO applicability guardrails

**Files:**
- Modify: `src/STIGForge.Core/Services/PackApplicabilityRules.cs`
- Modify: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`
- Test: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`

**Step 1: Write failing tests**

Add tests:
1. `Evaluate_DomainGpo_OnWorkstation_ReturnsUnknown`
2. `Evaluate_DomainGpo_OnDomainController_ReturnsApplicableHigh`

Expected reason codes:
- `domain_gpo_requires_domain_context`
- `domain_gpo_dc_role_match`

**Step 2: Run tests to verify fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~Evaluate_DomainGpo_OnWorkstation_ReturnsUnknown|FullyQualifiedName~Evaluate_DomainGpo_OnDomainController_ReturnsApplicableHigh"`

Expected: FAIL before rule additions.

**Step 3: Implement minimal rule logic**

In `Evaluate`:
1. Identify domain GPO by source label (`gpo_domain_import`) and/or name marker.
2. If host role is DC -> `Applicable/High`.
3. Else -> `Unknown/Low` with domain-context reason.

**Step 4: Run tests to verify pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~Evaluate_DomainGpo_OnWorkstation_ReturnsUnknown|FullyQualifiedName~Evaluate_DomainGpo_OnDomainController_ReturnsApplicableHigh"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/PackApplicabilityRules.cs tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs
git commit -m "feat: apply domain-context applicability rules for domain GPO packs"
```

### Task 7: End-to-end verification and real bundle smoke check

**Files:**
- Verify only (no required file edits)

**Step 1: Run targeted import pipeline tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterTests|FullyQualifiedName~ImportInboxScannerTests|FullyQualifiedName~ImportQueuePlannerTests|FullyQualifiedName~PackApplicabilityRulesTests"`

Expected: PASS.

**Step 2: Run full unit suite**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`

Expected: PASS, 0 failed.

**Step 3: Manual smoke in app (Windows runtime)**

Run build and app:
- `"C:\Program Files\dotnet\dotnet.exe" build "C:\Projects\STIGForge\src\STIGForge.App\STIGForge.App.csproj"`
- `"C:\Program Files\dotnet\dotnet.exe" run --project "C:\Projects\STIGForge\src\STIGForge.App\STIGForge.App.csproj"`

Perform Import tab scan and verify latest `.stigforge/logs/import_scan_*.json`:
1. ADMX count reflects folder groups, not file count.
2. GPO count includes multiple local/domain scoped packs where present.
3. No library entry named after outer consolidated bundle.

**Step 4: Commit verification artifacts only if intentionally tracked**

```bash
git status
```

Do not commit runtime logs unless explicitly required by repo policy.

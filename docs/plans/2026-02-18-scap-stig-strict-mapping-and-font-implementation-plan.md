# SCAP-to-STIG Strict Mapping and Font Refresh Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prevent incorrect SCAP-to-STIG fallback pairing (for example firewall on unrelated STIG rows), set the app default font to Segoe UI, and document strict mapping behavior in the spec.

**Architecture:** Move fallback match compatibility into `PackApplicabilityRules` as a reusable rule, then consume it from `MainViewModel.Import` during per-STIG SCAP candidate derivation. Keep canonical deterministic selection intact, but only after strict candidate filtering. Apply typography globally through `App.xaml` so windows inherit one default font.

**Tech Stack:** C# (.NET 8), WPF XAML, xUnit, FluentAssertions.

---

### Task 1: Add failing tests for strict SCAP fallback compatibility

**Files:**
- Modify: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`
- Test: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`

**Step 1: Write the failing tests**

Add tests that assert:
- firewall SCAP does not fallback-match a STIG with only Win11 tags,
- firewall SCAP does fallback-match a firewall STIG,
- generic OS SCAP fallback requires explicit overlapping OS tags.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~PackApplicabilityRulesTests`
Expected: FAIL because new fallback compatibility API does not exist yet.

### Task 2: Implement strict fallback compatibility and wire into per-STIG candidate derivation

**Files:**
- Modify: `src/STIGForge.Core/Services/PackApplicabilityRules.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Test: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`

**Step 1: Write minimal implementation in rules service**

Add a public helper that evaluates STIG-tag to SCAP-tag fallback compatibility:
- feature-tag overlap required for feature-specific SCAP,
- explicit OS overlap required for featureless SCAP,
- return false for empty/weak tag sets.

**Step 2: Use helper in SCAP candidate builder**

Update `GetScapCandidatesForStigAsync` to use per-STIG tags and strict fallback compatibility; remove broad selected-STIG union/machine-feature fallback for SCAP candidate inclusion.

**Step 3: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~PackApplicabilityRulesTests`
Expected: PASS.

### Task 3: Apply Segoe UI default and update spec memory

**Files:**
- Modify: `src/STIGForge.App/App.xaml`
- Modify: `src/STIGForge.App/MainWindow.xaml`
- Modify: `src/STIGForge.App/Views/ContentPickerDialog.xaml`
- Modify: `spec-stigforge.md`

**Step 1: Set app-wide default font**

Add `Window` style in `App.xaml` with `FontFamily="Segoe UI"` and size `13`.

**Step 2: Remove hardcoded Bahnschrift at window level**

Remove explicit `FontFamily="Bahnschrift"` from affected windows so they inherit the global default.

**Step 3: Add strict SCAP matching requirement to spec**

Document benchmark-first per-STIG mapping and no forced broad fallback on ambiguity.

**Step 4: Run focused verification**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~PackApplicabilityRulesTests`
- `dotnet build src/STIGForge.App/STIGForge.App.csproj`

Expected: tests pass and app build succeeds.

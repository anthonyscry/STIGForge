# STIG-SCAP 1:1 Mapping + Applicability + Layout Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enforce one canonical SCAP per STIG, eliminate known applicability false positives (FortiGate/Symantec) with explicit evidence and Unknown state, and keep Import tab layout readable when machine scan details are expanded.

**Architecture:** Add two focused core services: (1) canonical SCAP selector with deterministic tie-breaks and (2) richer applicability decision output with evidence and confidence. Wire Import scan view-model to consume these services while keeping existing workflows intact. Keep UI work localized to Import view row sizing and scroll behavior.

**Tech Stack:** .NET 8, C#, WPF (XAML), xUnit, FluentAssertions

---

### Task 1: Add canonical SCAP selector service (test-first)

**Files:**
- Create: `src/STIGForge.Core/Services/CanonicalScapSelector.cs`
- Create: `tests/STIGForge.UnitTests/Services/CanonicalScapSelectorTests.cs`

**Step 1: Write failing tests for deterministic selection**

```csharp
[Fact]
public void Select_PrefersVersionMatch_ThenNiwcEnhanced()
{
  // STIG V2R1, two SCAP V2R1 candidates, NIWC candidate wins.
}

[Fact]
public void Select_WhenVersionTied_UsesNewestReleaseDate()
{
  // Same version, non-NIWC candidates, latest date wins.
}

[Fact]
public void Select_WhenAllTied_UsesStableLexicalFallback()
{
  // Deterministic by PackId/Name ordering.
}
```

**Step 2: Run tests and confirm they fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter CanonicalScapSelectorTests`

Expected: FAIL with missing type/member errors.

**Step 3: Implement minimal selector**

```csharp
public sealed record CanonicalScapSelectionResult(string? ScapPackId, string ScapName, IReadOnlyList<string> Reasons, bool HasConflict);

public sealed class CanonicalScapSelector
{
  public CanonicalScapSelectionResult Select(...)
  {
    // 1) version alignment
    // 2) NIWC enhanced preference
    // 3) newest release/import date
    // 4) stable lexical fallback
  }
}
```

**Step 4: Re-run tests and verify pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter CanonicalScapSelectorTests`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/CanonicalScapSelector.cs tests/STIGForge.UnitTests/Services/CanonicalScapSelectorTests.cs
git commit -m "feat(core): add deterministic canonical SCAP selector"
```

### Task 2: Add tri-state applicability decision with evidence (test-first)

**Files:**
- Modify: `src/STIGForge.Core/Services/PackApplicabilityRules.cs`
- Modify: `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`

**Step 1: Add failing regression tests for false positives and Unknown behavior**

```csharp
[Fact]
public void Evaluate_FortiGate_WithoutStrongSignals_ReturnsUnknown() { }

[Fact]
public void Evaluate_FortiGate_WithServiceOrRegistrySignal_ReturnsApplicableHigh() { }

[Fact]
public void Evaluate_SymantecEndpoint_WithoutStrongSignals_ReturnsUnknown() { }

[Fact]
public void IsApplicable_CompatibilityShim_ReturnsFalse_ForUnknown() { }
```

**Step 2: Run tests and confirm fail**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter PackApplicabilityRulesTests`

Expected: FAIL with missing API/state assertions.

**Step 3: Implement decision model and compatibility shim**

```csharp
public enum ApplicabilityState { Applicable, NotApplicable, Unknown }
public enum ApplicabilityConfidence { High, Low }

public sealed class PackApplicabilityDecision
{
  public ApplicabilityState State { get; init; }
  public ApplicabilityConfidence Confidence { get; init; }
  public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
  public string ReasonCode { get; init; } = string.Empty;
}

public static PackApplicabilityDecision Evaluate(PackApplicabilityInput input) { ... }
public static bool IsApplicable(PackApplicabilityInput input) => Evaluate(input).State == ApplicabilityState.Applicable;
```

Implementation rules:
- Require explicit vendor/product evidence for FortiGate and Symantec Endpoint packs.
- If strong evidence absent, return `Unknown` (not `Applicable`).
- Keep existing baseline OS/role/filter behavior for non-product-specific packs.

**Step 4: Re-run applicability tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter PackApplicabilityRulesTests`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Services/PackApplicabilityRules.cs tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs
git commit -m "fix(core): add tri-state applicability with strong-evidence checks"
```

### Task 3: Capture stronger host evidence in machine scan path

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`

**Step 1: Add failing integration-style expectation comments/tests in code path validation (if no app-level tests exist, add trace assertions in unit-testable helper methods)**

```csharp
// Add helper methods that can be unit-tested in core if possible.
```

**Step 2: Implement host evidence collection helpers**

Add localized helpers in `MainViewModel.Import.cs`:
- `CollectInstalledProgramSignals(...)` from uninstall registry hives.
- `CollectServiceSignals(...)` for Windows services.
- `CollectKnownFileSignals(...)` for vendor install paths.

Populate `MachineInfo` with a `HostSignals` list (normalized tokens such as `service:forticlient`, `registry:symantec_endpoint`, `file:forticlient.exe`).

**Step 3: Build app project**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`

Expected: PASS.

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Import.cs
git commit -m "feat(app): collect explicit host evidence for applicability reasoning"
```

### Task 4: Enforce 1:1 STIG->SCAP mapping in machine scan view-model

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify (if needed for display fields only): `src/STIGForge.App/MainViewModel.cs`

**Step 1: Add/adjust failing tests for selector behavior (if gaps remain)**

```csharp
// Add cases for version alignment + NIWC + deterministic fallback if not already covered.
```

**Step 2: Replace multi-row SCAP emission with canonical selection**

Implementation details:
- For each applicable STIG, gather SCAP candidates (current `matchingScap`).
- Call `CanonicalScapSelector.Select(...)`.
- Add exactly one `ApplicablePackPair` row:
  - winner => `MatchState="Matched"`
  - no candidate => `ScapName="(none)"`, `MatchState="Missing"`
- Never add multiple SCAP rows for one STIG.

**Step 3: Route conflict notes to diagnostics only**

Append selector reasons to `MachineSelectionDiagnostics` and keep primary table clean.

**Step 4: Verify compile + selector/applicability tests**

Run:
- `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "CanonicalScapSelectorTests|PackApplicabilityRulesTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/MainViewModel.cs
git commit -m "fix(app): enforce canonical one-to-one STIG to SCAP mapping"
```

### Task 5: Surface "Why applicable?" debug reasoning (Unknown kept out of main table)

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/Views/ImportView.xaml` (label text tweaks only)

**Step 1: Implement diagnostic formatting**

Emit structured lines in `MachineSelectionDiagnostics`:
- pack name + format,
- state + confidence,
- evidence lines (service/registry/file/feature),
- canonical mapping conflict reason when present.

**Step 2: Ensure default table excludes Unknown rows**

Only high-confidence applicable STIGs appear in `ApplicablePackPairs`.

**Step 3: Build and smoke-check**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`

Expected: PASS.

**Step 4: Commit**

```bash
git add src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/Views/ImportView.xaml
git commit -m "feat(app): add machine applicability debug reasoning with confidence tiers"
```

### Task 6: Keep Import layout cohesive when machine scan panel expands

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`

**Step 1: Add layout guardrails**

Changes:
- Give top/bottom Import regions explicit min heights.
- Constrain machine detail panel growth with internal scrolling.
- Ensure content library list keeps a sensible minimum size.

**Step 2: Build and run manual UI verification**

Run:
- `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`
- `dotnet run --project src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`

Manual checks:
- Expand Machine scan details.
- Confirm STIG Library remains readable and scrollable.
- Confirm no panel is compressed to unusable height.

**Step 3: Commit**

```bash
git add src/STIGForge.App/Views/ImportView.xaml
git commit -m "fix(ui): enforce minimum layout sizes for expanded machine scan"
```

### Task 7: Final verification gate

**Files:**
- Modify (if needed): `docs/plans/2026-02-14-verification-first-stabilization-plan.md`

**Step 1: Run focused and full tests**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "CanonicalScapSelectorTests|PackApplicabilityRulesTests"`
- `dotnet test`

Expected: PASS (0 failed).

**Step 2: Run Windows app build**

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows`

Expected: PASS (0 errors).

**Step 3: Record outcomes in verification notes**

Document:
- no multi-SCAP rows for a STIG in applicable table,
- FortiGate/Symantec regressions covered by tests,
- expanded machine scan keeps library usable.

**Step 4: Commit verification notes**

```bash
git add docs/plans/2026-02-14-verification-first-stabilization-plan.md
git commit -m "test: record verification evidence for mapping and applicability fixes"
```

# Merged Checklist and Compliance Dashboard Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build snapshot-merged Verify outputs (Evaluate-STIG + SCC + manual precedence) that export as STIG Viewer-importable merged CKL, and add a dashboard compliance donut with full rule count.

**Architecture:** Add a dedicated snapshot merge service in the Verify layer, then wire workflow output contracts to carry summary counts used by both export and UI. Keep `CklExporter` as serialization-first, but enforce one-control-per-key and CKL-safe statuses. Add a lightweight custom WPF donut control with viewmodel-backed metrics, no third-party chart dependency.

**Tech Stack:** .NET 8, C#, WPF (XAML), xUnit, FluentAssertions.

---

### Task 1: Add Snapshot Merge Service (Core Merge Semantics)

**Files:**
- Create: `src/STIGForge.Verify/SnapshotMergeService.cs`
- Modify: `src/STIGForge.Verify/VerifyModels.cs`
- Test: `tests/STIGForge.UnitTests/Verify/SnapshotMergeServiceTests.cs`

**Step 1: Write the failing tests**

```csharp
[Fact]
public void Merge_PrefersSccOverEvaluate_ForSameBenchmarkAndControl()
{
  var inputs = new[]
  {
    new ControlResult { VulnId = "V-1001", RuleId = "SV-1001", BenchmarkId = "Windows_11", Status = "Open", Tool = "Evaluate-STIG", FindingDetails = "eval evidence" },
    new ControlResult { VulnId = "V-1001", RuleId = "SV-1001", BenchmarkId = "Windows_11", Status = "NotAFinding", Tool = "SCC", FindingDetails = "scc evidence" }
  };

  var merged = SnapshotMergeService.Merge(inputs, "MINI-TONY");

  merged.Should().ContainSingle();
  merged[0].Status.Should().Be("NotAFinding");
  merged[0].FindingDetails.Should().Contain("eval evidence").And.Contain("scc evidence");
}
```

Add additional failing tests for:
- `Manual` override beats SCC/Evaluate.
- Merge key uses `BenchmarkId + VulnId` and falls back to `RuleId`.
- Append-only provenance ordering is deterministic.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~SnapshotMergeServiceTests"`

Expected: FAIL with missing `SnapshotMergeService` and/or missing `BenchmarkId` on `ControlResult`.

**Step 3: Write minimal implementation**

```csharp
public static class SnapshotMergeService
{
  public static IReadOnlyList<ControlResult> Merge(IEnumerable<ControlResult> results, string assetId)
  {
    // group by asset + benchmark + control key
    // resolve status by precedence: Manual > SCC > Evaluate-STIG > baseline
    // append evidence/comments with provenance headers
  }
}
```

In `ControlResult`, add minimal fields needed for merge identity/provenance:

```csharp
public string? BenchmarkId { get; set; }
public string? AssetId { get; set; }
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~SnapshotMergeServiceTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Verify/SnapshotMergeService.cs src/STIGForge.Verify/VerifyModels.cs tests/STIGForge.UnitTests/Verify/SnapshotMergeServiceTests.cs
git commit -m "feat(verify): add snapshot merge service with precedence rules"
```

### Task 2: Wire Merge into Verification Workflow and Add Summary Counts

**Files:**
- Modify: `src/STIGForge.Core/Abstractions/Services.cs`
- Modify: `src/STIGForge.Verify/VerifyReportWriter.cs`
- Modify: `src/STIGForge.Verify/VerificationWorkflowService.cs`
- Test: `tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs`
- Test: `tests/STIGForge.UnitTests/Verify/VerifyReportWriterTests.cs`

**Step 1: Write the failing tests**

Add failing assertions that `VerificationWorkflowResult` includes:

```csharp
result.TotalRuleCount.Should().Be(3);
result.PassCount.Should().Be(1);
result.FailCount.Should().Be(1);
result.NotApplicableCount.Should().Be(1);
result.NotReviewedCount.Should().Be(0);
result.ErrorCount.Should().Be(0);
```

And a failing test that workflow uses merged snapshot counts (not raw duplicate rows).

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~VerificationWorkflowServiceTests|FullyQualifiedName~VerifyReportWriterTests"`

Expected: FAIL due to missing properties and/or incorrect count behavior.

**Step 3: Write minimal implementation**

Update `VerificationWorkflowResult` contract:

```csharp
public int TotalRuleCount { get; set; }
public int PassCount { get; set; }
public int FailCount { get; set; }
public int NotApplicableCount { get; set; }
public int NotReviewedCount { get; set; }
public int ErrorCount { get; set; }
```

In workflow service:
- run existing CKL parsing,
- call `SnapshotMergeService.Merge(...)`,
- write merged results,
- populate new summary fields.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~VerificationWorkflowServiceTests|FullyQualifiedName~VerifyReportWriterTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Abstractions/Services.cs src/STIGForge.Verify/VerifyReportWriter.cs src/STIGForge.Verify/VerificationWorkflowService.cs tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs tests/STIGForge.UnitTests/Verify/VerifyReportWriterTests.cs
git commit -m "feat(verify): surface merged compliance summary counts"
```

### Task 3: Enforce STIG Viewer-Safe Merged CKL Output

**Files:**
- Modify: `src/STIGForge.Export/CklExporter.cs`
- Test: `tests/STIGForge.UnitTests/Export/CklExporterTests.cs`
- Test: `tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs`

**Step 1: Write the failing tests**

Add failing tests for:
- one `VULN` per `Vuln_Num` per `iSTIG` (dedupe guard),
- status normalization to CKL-safe values,
- merged evidence/comments preserved in exported nodes.

```csharp
doc.Descendants("iSTIG")
  .SelectMany(s => s.Descendants("VULN"))
  .GroupBy(v => ExtractVulnNum(v))
  .Should().OnlyContain(g => g.Count() == 1);
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~CklExporterTests"`

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~CklExporterIntegrationTests"`

Expected: FAIL until exporter behavior is tightened.

**Step 3: Write minimal implementation**

In exporter path:
- ensure per-`iSTIG` dedupe by canonical control key,
- map unknown statuses to `Not_Reviewed`,
- emit merged `FindingDetails`/`Comments` exactly once per merged control.

**Step 4: Run test to verify it passes**

Run the same two commands.

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Export/CklExporter.cs tests/STIGForge.UnitTests/Export/CklExporterTests.cs tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs
git commit -m "fix(export): enforce merged CKL dedupe and STIG Viewer-safe statuses"
```

### Task 4: Project Verify Summary into WorkflowViewModel Metrics

**Files:**
- Modify: `src/STIGForge.App/WorkflowViewModel.cs`
- Modify: `src/STIGForge.App/WorkflowSettings.cs` (only if persistence needed)
- Test: `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`

**Step 1: Write the failing tests**

Add failing tests that verify, after `RunVerifyStepCommand`:

```csharp
vm.TotalRuleCount.Should().Be(240);
vm.CompliancePassCount.Should().Be(180);
vm.ComplianceFailCount.Should().Be(40);
vm.ComplianceOtherCount.Should().Be(20);
vm.CompliancePercent.Should().BeApproximately(81.82, 0.01);
```

Include a pre-verify test asserting metrics remain zero/default.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~WorkflowViewModelTests"`

Expected: FAIL with missing properties or incorrect projections.

**Step 3: Write minimal implementation**

Add observable properties:

```csharp
[ObservableProperty] private int _totalRuleCount;
[ObservableProperty] private int _compliancePassCount;
[ObservableProperty] private int _complianceFailCount;
[ObservableProperty] private int _complianceOtherCount;
[ObservableProperty] private double _compliancePercent;
```

Populate these directly from `VerificationWorkflowResult` counts after verify completion.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~WorkflowViewModelTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/WorkflowViewModel.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(app): project verify compliance totals into dashboard metrics"
```

### Task 5: Add Compliance Donut Control and Dashboard Card

**Files:**
- Create: `src/STIGForge.App/Views/Controls/ComplianceDonutChart.xaml`
- Create: `src/STIGForge.App/Views/Controls/ComplianceDonutChart.xaml.cs`
- Modify: `src/STIGForge.App/Views/DashboardView.xaml`
- Test: `tests/STIGForge.UnitTests/Views/DashboardViewContractTests.cs`

**Step 1: Write the failing tests**

Create a contract test that reads `DashboardView.xaml` and asserts:
- `Compliance Summary` label exists,
- chart control tag exists,
- `TotalRuleCount` and `CompliancePercent` bindings exist.

```csharp
content.Should().Contain("Compliance Summary");
content.Should().Contain("ComplianceDonutChart");
content.Should().Contain("TotalRuleCount");
content.Should().Contain("CompliancePercent");
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~DashboardViewContractTests"`

Expected: FAIL before XAML/control changes.

**Step 3: Write minimal implementation**

Create a lightweight donut control with dependency properties:

```csharp
public double PassValue { get; set; }
public double FailValue { get; set; }
public double OtherValue { get; set; }
public double TotalValue { get; set; }
```

Render arcs with deterministic segment ordering and theme brushes.

In `DashboardView.xaml`, add `Compliance Summary` card below workflow step grid and above failure/results sections with:
- donut control bound to compliance metrics,
- total rules and percent labels,
- pre-verify placeholder message.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~DashboardViewContractTests|FullyQualifiedName~WorkflowViewModelTests"`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/Controls/ComplianceDonutChart.xaml src/STIGForge.App/Views/Controls/ComplianceDonutChart.xaml.cs src/STIGForge.App/Views/DashboardView.xaml tests/STIGForge.UnitTests/Views/DashboardViewContractTests.cs tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs
git commit -m "feat(ui): add dashboard compliance donut with total rule count"
```

### Task 6: Final Verification and Regression Gate

**Files:**
- Verify only (no new files unless a small test gap appears)

**Step 1: Run targeted test suites**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~SnapshotMergeServiceTests|FullyQualifiedName~VerificationWorkflowServiceTests|FullyQualifiedName~VerifyReportWriterTests|FullyQualifiedName~CklExporterTests|FullyQualifiedName~WorkflowViewModelTests|FullyQualifiedName~DashboardViewContractTests"`
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --framework net8.0-windows --filter "FullyQualifiedName~CklExporterIntegrationTests"`

Expected: PASS.

**Step 2: Run full solution build**

Run: `dotnet build STIGForge.sln`

Expected: `0 Warning(s)`, `0 Error(s)`.

**Step 3: Manual smoke checks**

1. Run workflow with Evaluate + SCC enabled.
2. Confirm merged CKL exports.
3. Import merged CKL into STIG Viewer.
4. Verify compliance donut shows totals and percent after verify.

**Step 4: Commit verification-only adjustments (if any)**

```bash
git add <only-files-changed-during-gate>
git commit -m "test: add final regression coverage for merged verify and dashboard metrics"
```

**Step 5: Prepare PR notes**

Capture:
- merge precedence behavior,
- STIG Viewer import proof,
- dashboard metrics formula.

## Execution Notes

- Follow `superpowers:test-driven-development` for each code task.
- Follow `superpowers:verification-before-completion` before claiming done.
- Keep commits small and task-scoped.
- Do not refactor unrelated components.

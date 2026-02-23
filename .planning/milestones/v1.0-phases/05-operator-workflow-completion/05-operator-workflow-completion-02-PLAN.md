---
phase: 05-operator-workflow-completion
plan: 02
type: execute
wave: 2
depends_on:
  - 05-operator-workflow-completion-01
files_modified:
  - src/STIGForge.Verify/VerificationArtifactAggregationService.cs
  - src/STIGForge.Build/BundleOrchestrator.cs
  - src/STIGForge.App/App.xaml.cs
  - src/STIGForge.App/MainViewModel.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - src/STIGForge.App/MainViewModel.Dashboard.cs
  - tests/STIGForge.UnitTests/Verify/VerificationArtifactAggregationServiceTests.cs
autonomous: true
must_haves:
  truths:
    - "WPF verify/orchestrate runs produce overlap artifacts without requiring a separate CLI command."
    - "Dashboard labels and metrics reflect the same mission summary as CLI bundle-summary."
    - "Verify/report summaries in app tabs represent real consolidated artifacts across configured tools."
  artifacts:
    - path: "src/STIGForge.Verify/VerificationArtifactAggregationService.cs"
      provides: "Reusable overlap/control-source artifact generation"
      contains: "WriteCoverageArtifacts"
    - path: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      provides: "WPF verify pipeline wiring to shared aggregation"
      contains: "VerifyRunAsync|Orchestrate"
    - path: "src/STIGForge.App/MainViewModel.Dashboard.cs"
      provides: "Dashboard metrics driven by shared mission summary"
      contains: "RefreshDashboard"
  key_links:
    - from: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      to: "src/STIGForge.Verify/VerificationArtifactAggregationService.cs"
      via: "post-verify coverage artifact generation"
      pattern: "WriteCoverageArtifacts"
    - from: "src/STIGForge.App/MainViewModel.Dashboard.cs"
      to: "src/STIGForge.Core/Services/BundleMissionSummaryService.cs"
      via: "dashboard refresh summary load"
      pattern: "LoadSummary"
---

<objective>
Wire the app workflow to shared verify/reporting infrastructure so operators get complete mission diagnostics directly from WPF.

Purpose: Remove remaining CLI fallback for overlap and mission-summary visibility in day-to-day UI operations.
Output: Shared verify artifact aggregation, WPF verify/orchestrate integration, and dashboard parity with CLI summary semantics.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/05-operator-workflow-completion/05-operator-workflow-completion-01-PLAN.md
@src/STIGForge.App/MainViewModel.ApplyVerify.cs
@src/STIGForge.App/MainViewModel.Dashboard.cs
@src/STIGForge.Build/BundleOrchestrator.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Extract reusable verify coverage artifact aggregation</name>
  <files>src/STIGForge.Verify/VerificationArtifactAggregationService.cs, src/STIGForge.Build/BundleOrchestrator.cs</files>
  <action>Move overlap/control-source artifact generation from `BundleOrchestrator` private helpers into a reusable Verify service that accepts labeled consolidated-report roots and emits `coverage_by_tool`, `control_sources`, and `coverage_overlap` outputs with existing file names/schemas unchanged.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~VerificationArtifactAggregationServiceTests"</verify>
  <done>Both orchestration and UI flows can generate overlap artifacts through one shared implementation.</done>
</task>

<task type="auto">
  <name>Task 2: Update WPF verify and orchestrate flows to emit complete reporting artifacts</name>
  <files>src/STIGForge.App/MainViewModel.ApplyVerify.cs, src/STIGForge.App/MainViewModel.cs, src/STIGForge.App/App.xaml.cs</files>
  <action>Inject and use the aggregation service after Verify/Orchestrate runs so WPF writes overlap artifacts and derives `VerifySummary`/`ReportSummary` from generated consolidated outputs. Keep current tool-config UX and existing error handling paths intact.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~VerifyCommandFlowTests"</verify>
  <done>Running verify from WPF produces overlap/report artifacts and non-empty summary text when tool output exists.</done>
</task>

<task type="auto">
  <name>Task 3: Drive dashboard metrics from shared mission summary service</name>
  <files>src/STIGForge.App/MainViewModel.Dashboard.cs, src/STIGForge.App/MainViewModel.cs, src/STIGForge.App/App.xaml.cs</files>
  <action>Replace ad-hoc dashboard parsing with `BundleMissionSummaryService` from Plan 01 so `DashPackLabel`, `DashProfileLabel`, verify counts/percent, and manual progress all use normalized summary values. Ensure manifest parsing uses `run.*` fields and remains resilient to missing files.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleMissionSummaryServiceTests|FullyQualifiedName~VerificationArtifactAggregationServiceTests"</verify>
  <done>Dashboard values match shared mission summary semantics and no longer drift from CLI bundle summary output.</done>
</task>

</tasks>

<verification>
Execute verify-focused integration tests and confirm WPF runs now generate overlap artifacts and dashboard metrics aligned with shared summary logic.
</verification>

<success_criteria>
Operators can run verify/orchestrate entirely from WPF and immediately see accurate overlap, pack/profile labels, and mission metrics without CLI fallback.
</success_criteria>

<output>
After completion, create `.planning/phases/05-operator-workflow-completion/05-operator-workflow-completion-02-SUMMARY.md`
</output>

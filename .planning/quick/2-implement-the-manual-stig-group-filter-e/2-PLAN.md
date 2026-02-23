---
phase: 2-implement-the-manual-stig-group-filter-e
plan: 2
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.App/MainViewModel.cs
  - src/STIGForge.App/MainViewModel.Manual.cs
  - src/STIGForge.App/Views/ManualView.xaml
  - src/STIGForge.Cli/Commands/VerifyCommands.cs
  - src/STIGForge.Verify/VerificationWorkflowService.cs
  - tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs
  - tests/STIGForge.UnitTests/Views/ManualControlsLoadingContractTests.cs
  - tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs
autonomous: true
requirements:
  - QK-01
  - QK-02
must_haves:
  truths:
    - "Manual controls can be filtered by STIG group from the Manual tab without losing existing search/status/CAT filtering behavior."
    - "Manual STIG-group options are driven from current manual control data and include a dedicated 'All' default."
    - "Zero CKL-result verification runs surface clear diagnostics and fail-closed guidance in CLI behavior."
  artifacts:
    - path: "src/STIGForge.App/MainViewModel.cs"
      provides: "Manual filter properties and STIG-group data sources for the UI"
      contains: "ManualStigGroupFilter|ManualStigGroupFilters"
    - path: "src/STIGForge.App/MainViewModel.Manual.cs"
      provides: "Filter predicate wiring for manual STIG-group filtering"
      contains: "ManualStigGroupFilter"
    - path: "src/STIGForge.App/Views/ManualView.xaml"
      provides: "STIG Group combo-box in manual controls filter bar"
      contains: "ManualStigGroupFilters|ManualStigGroupFilter"
    - path: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      provides: "Explicit diagnostics when zero CKL results are produced"
      contains: "No CKL results were found"
    - path: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      provides: "CLI verification exit/result behavior for no-results verification runs"
      contains: "ExitCode"
  key_links:
    - from: "src/STIGForge.App/MainViewModel.cs"
      to: "src/STIGForge.App/Views/ManualView.xaml"
      via: "Observable filter properties bound to filter controls"
      pattern: "ManualStigGroupFilters|ManualStigGroupFilter"
    - from: "src/STIGForge.App/MainViewModel.Manual.cs"
      to: "src/STIGForge.App/MainViewModel.cs"
      via: "partial filter-change hooks and ManualControls refresh logic"
      pattern: "OnManualStigGroupFilterChanged|ConfigureManualView|ManualControlsView.Filter"
    - from: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      to: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      via: "ConsolidatedResultCount + Diagnostics passed into command handlers"
      pattern: "ConsolidatedResultCount|Diagnostics"
---

<objective>
Implement manual STIG-group filtering in the manual-control UI and prevent silent SCC/SCAP zero-result verification flows by surfacing explicit no-result failures in CLI behavior.

Purpose: Operators need immediate control over STIG scope in manual review and clear signal when SCC-like workflows return no controls.
Output: Filter-aware manual view + verification commands that report no-result conditions deterministically.
</objective>

<execution_context>
@/home/anthonyscry/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/anthonyscry/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.App/MainViewModel.cs
@src/STIGForge.App/MainViewModel.Manual.cs
@src/STIGForge.App/Views/ManualView.xaml
@src/STIGForge.Verify/VerificationWorkflowService.cs
@src/STIGForge.Cli/Commands/VerifyCommands.cs
@tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs
@tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs
@.planning/quick/2-implement-the-manual-stig-group-filter-e
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add manual STIG-group filter data + bindings in Manual view model</name>
  <files>src/STIGForge.App/MainViewModel.cs, src/STIGForge.App/MainViewModel.Manual.cs</files>
  <action>In `MainViewModel.cs`, add observable properties for `ManualStigGroupFilter` (default `"All"`) and `ManualStigGroupFilters` plus stable refresh on change. Ensure a `partial` property-change handler calls `RefreshManualView()`. In `LoadManualControlsAsync`, clear and rebuild the `ManualStigGroupFilters` list from distinct `ManualControlItem.StigGroup` values in the loaded manual set and keep a stable sorted order with `Unknown`/`(unknown)` handled consistently. In `MainViewModel.Manual.cs`, extend `ConfigureManualView` filter logic so selected STIG-group applies case-insensitive exact filtering before text search and other filter checks, with no behavior change to status/CAT/search when `All` is selected.</action>
  <verify>Run `rg --line-number "ManualStigGroupFilter|ManualStigGroupFilters|OnManualStigGroupFilterChanged|ManualControlsView.Filter" src/STIGForge.App/MainViewModel.cs src/STIGForge.App/MainViewModel.Manual.cs` and confirm all tokens exist and are connected via the same `ManualControlsView` refresh path.</verify>
  <done>Manual list respects STIG-group filtering and resets correctly when filter changes or data reloads.</done>
</task>

<task type="auto">
  <name>Task 2: Add STIG-group selector control in Manual XAML</name>
  <files>src/STIGForge.App/Views/ManualView.xaml</files>
  <action>Add a new `ComboBox` in the manual filter row, bound to `ManualStigGroupFilters` and `ManualStigGroupFilter`, with label `STIG Group:`. Place it adjacent to status/CAT controls so keyboard and screen-reader semantics remain consistent with existing filters. Keep existing list columns (including STIG Group display) intact so this adds a second filter axis, not a column replacement.</action>
  <verify>Run `rg --line-number "STIG Group|ManualStigGroupFilters|ManualStigGroupFilter|ComboBox" src/STIGForge.App/Views/ManualView.xaml` and verify the new control binds to both properties and uses `Manual` filter context.</verify>
  <done>Operator can set STIG group filter from UI and see filtered control set update immediately.</done>
</task>

<task type="auto">
  <name>Task 3: Treat zero-result SCC/SCAP verification as explicit warning/fail condition in CLI path</name>
  <files>src/STIGForge.Verify/VerificationWorkflowService.cs, src/STIGForge.Cli/Commands/VerifyCommands.cs, tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs, tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs</files>
  <action>In `VerificationWorkflowService`, keep the diagnostics list and enrich the zero-result message with required-tool context so SCC/SCAP runs produce deterministic user guidance when configured execution produced no CKL results (e.g., include executed run list and output root). In both `verify-evaluate-stig` and `verify-scap` handlers, if the selected workflow was enabled and `workflowResult.ConsolidatedResultCount == 0`, set a non-zero command exit code (configurable in one constant/field), print a clear guidance message, and avoid reporting success with empty results. Update unit/integration verification tests to assert non-empty diagnostics in this condition and command-result expectations when no ckls exist under output-root but verification was requested.</action>
  <verify>Run `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~VerificationWorkflowServiceTests|FullyQualifiedName~ManualControlsLoadingContractTests"` and `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~VerifyCommandFlowTests"`.</verify>
  <done>Zero-result SCC/SCAP verification flows emit deterministic diagnostics and no longer exit like successful verification.</done>
</task>

</tasks>

<verification>
- Confirm manual filter filter-path wiring from `ManualStigGroupFilter` setter through `ManualControlsView.Filter` and `RefreshManualView()` in unit-level compile and behavior checks.
- Confirm CLI command behavior with manually forced no-result runs sets explicit non-zero exit semantics and diagnostics.
- Run targeted tests and a release-targeted build: `dotnet build STIGForge.sln --configuration Release -p:EnableWindowsTargeting=true`.
</verification>

<success_criteria>
- Manual controls can be narrowed to one STIG group and combined with status/CAT/search filters.
- Manual STIG groups are populated from loaded manual controls and include an `All` option by default.
- No-result verification runs include actionable diagnostics and exit with a blocking status in CLI commands when checks were requested.
</success_criteria>

<output>
After completion, create `.planning/quick/2-implement-the-manual-stig-group-filter-e/2-SUMMARY.md`.
</output>

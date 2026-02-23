---
phase: 05-operator-workflow-completion
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Core/Abstractions/Services.cs
  - src/STIGForge.Core/Services/BundleMissionSummaryService.cs
  - src/STIGForge.Cli/Commands/BundleCommands.cs
  - tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs
autonomous: true
must_haves:
  truths:
    - "Bundle mission status is computed consistently from manifest, verify artifacts, and manual answers."
    - "Manual and verify statuses are normalized into stable operator-facing categories."
    - "CLI bundle-summary relies on shared logic instead of in-command parsing."
  artifacts:
    - path: "src/STIGForge.Core/Services/BundleMissionSummaryService.cs"
      provides: "Reusable bundle summary and status normalization"
      contains: "LoadSummary|NormalizeStatus"
    - path: "src/STIGForge.Cli/Commands/BundleCommands.cs"
      provides: "CLI bundle-summary wired to shared service"
      contains: "RegisterBundleSummary"
    - path: "tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs"
      provides: "Regression coverage for manifest/report/manual aggregation"
  key_links:
    - from: "src/STIGForge.Core/Services/BundleMissionSummaryService.cs"
      to: "src/STIGForge.Core/Services/ManualAnswerService.cs"
      via: "manual progress aggregation"
      pattern: "GetProgressStats"
    - from: "src/STIGForge.Cli/Commands/BundleCommands.cs"
      to: "src/STIGForge.Core/Services/BundleMissionSummaryService.cs"
      via: "command summary rendering"
      pattern: "LoadSummary"
---

<objective>
Create one canonical bundle-mission summary path used by operator surfaces so status math and labels never drift between CLI and WPF.

Purpose: Remove hidden CLI-only logic and make mission-state reporting deterministic.
Output: Shared summary service, normalized status mapping, and CLI command refactor with tests.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Cli/Commands/BundleCommands.cs
@src/STIGForge.App/MainViewModel.Dashboard.cs
@src/STIGForge.Core/Services/ManualAnswerService.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add shared bundle mission summary service in Core</name>
  <files>src/STIGForge.Core/Abstractions/Services.cs, src/STIGForge.Core/Services/BundleMissionSummaryService.cs</files>
  <action>Create a Core service that reads `Manifest/manifest.json` (`run.packName`, `run.profileName`), aggregates verify artifacts from `Verify/**/consolidated-results.json`, computes manual progress from `Manual/answers.json`, and normalizes status aliases (`NotAFinding`/`Pass`, `Open`/`Fail`, `NotApplicable`). Preserve offline-first behavior and avoid introducing external dependencies.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ManualAnswerServiceTests"</verify>
  <done>Service returns deterministic pack/profile/control/manual/verify summary values for any valid bundle layout.</done>
</task>

<task type="auto">
  <name>Task 2: Refactor CLI bundle-summary to consume shared service</name>
  <files>src/STIGForge.Cli/Commands/BundleCommands.cs</files>
  <action>Replace inline manifest/manual/verify parsing in `bundle-summary` with the shared Core service while keeping the existing `--json` shape and human-readable output structure unchanged so current operator scripts do not break.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~CliCommandTests"</verify>
  <done>`bundle-summary` outputs the same contract but now depends on one shared summary implementation.</done>
</task>

<task type="auto">
  <name>Task 3: Add regression tests for summary aggregation and status normalization</name>
  <files>tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs</files>
  <action>Add unit tests for manifest `run` field parsing, multi-tool verify aggregation, and mixed status alias handling to ensure future workflow changes keep operator summaries accurate.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~BundleMissionSummaryServiceTests"</verify>
  <done>Tests fail on summary drift and pass when bundle metrics/normalization are correct.</done>
</task>

</tasks>

<verification>
Run unit + CLI integration tests and confirm shared summary logic is the single source for bundle mission metrics.
</verification>

<success_criteria>
Mission summary calculations are centralized, status aliases are normalized, and CLI summary output remains stable without duplicated parsing logic.
</success_criteria>

<output>
After completion, create `.planning/phases/05-operator-workflow-completion/05-operator-workflow-completion-01-SUMMARY.md`
</output>

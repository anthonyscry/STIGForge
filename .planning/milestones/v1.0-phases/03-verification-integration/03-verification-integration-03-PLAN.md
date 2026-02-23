---
phase: 03-verification-integration
plan: 03
type: execute
wave: 2
depends_on:
  - 03-verification-integration-01
  - 03-verification-integration-02
files_modified:
  - src/STIGForge.Cli/Commands/VerifyCommands.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - src/STIGForge.Build/BundleOrchestrator.cs
  - tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs
autonomous: true
must_haves:
  truths:
    - "CLI verify commands and WPF verify workflow generate matching consolidated artifacts."
    - "Operator-visible summary reflects consolidated results from the shared workflow path."
    - "Coverage and overlap outputs still generate after refactor."
  artifacts:
    - path: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      provides: "CLI path uses shared verification workflow"
      contains: "verify-evaluate-stig|verify-scap"
    - path: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      provides: "WPF verify and orchestrate paths use shared workflow"
      contains: "VerifyRunAsync|Orchestrate"
    - path: "tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs"
      provides: "Cross-surface parity regression test"
  key_links:
    - from: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      to: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      via: "service invocation"
      pattern: "IVerificationWorkflowService"
    - from: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      to: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      via: "shared verify execution"
      pattern: "IVerificationWorkflowService"
---

<objective>
Integrate CLI and WPF verify workflows with the shared service and prove output parity across surfaces.

Purpose: Eliminate execution drift between command-line and app flows while preserving current operator-visible behavior.
Output: Refactored integration points and parity-focused integration tests.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Cli/Commands/VerifyCommands.cs
@src/STIGForge.App/MainViewModel.ApplyVerify.cs
@src/STIGForge.Build/BundleOrchestrator.cs
@.planning/phases/03-verification-integration/03-verification-integration-01-PLAN.md
@.planning/phases/03-verification-integration/03-verification-integration-02-PLAN.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Refactor CLI verify commands to shared workflow service</name>
  <files>src/STIGForge.Cli/Commands/VerifyCommands.cs</files>
  <action>Replace command-local runner/report-writing duplication with calls to the shared verification workflow service. Preserve existing command options, logging, and output messaging so operator scripts do not break. Keep coverage-overlap generation path intact after consolidated result creation.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~VerifyCommandFlowTests"</verify>
  <done>`verify-evaluate-stig` and `verify-scap` continue to work with existing flags while using common verification execution internals.</done>
</task>

<task type="auto">
  <name>Task 2: Refactor WPF verify/orchestrate flow to shared workflow</name>
  <files>src/STIGForge.App/MainViewModel.ApplyVerify.cs, src/STIGForge.Build/BundleOrchestrator.cs</files>
  <action>Update app verify entry points and orchestrated verify stage to call the same shared workflow service used by CLI. Preserve status text updates and summary refresh behavior. Keep BundleOrchestrator outputs aligned with existing bundle layout contracts.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~MainViewModel"</verify>
  <done>WPF verify and orchestration run through shared service while keeping expected status messaging and summary refresh semantics.</done>
</task>

<task type="auto">
  <name>Task 3: Add parity integration tests for CLI and app paths</name>
  <files>tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs</files>
  <action>Add integration tests validating that CLI and app-invoked verification produce equivalent consolidated result counts, status distributions, and required output artifacts (`consolidated-results.json`, `consolidated-results.csv`, coverage files). Use deterministic fixtures and temporary output roots.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~VerifyCommandFlowTests"</verify>
  <done>Parity tests fail when CLI and app verification diverge, and pass when both surfaces produce equivalent consolidated artifacts.</done>
</task>

</tasks>

<verification>
Run targeted integration tests and confirm generated verify artifact structure is unchanged across CLI and WPF execution surfaces.
</verification>

<success_criteria>
Verification execution behavior is unified across surfaces with parity tests preventing regressions in output consistency.
</success_criteria>

<output>
After completion, create `.planning/phases/03-verification-integration/03-verification-integration-03-SUMMARY.md`
</output>

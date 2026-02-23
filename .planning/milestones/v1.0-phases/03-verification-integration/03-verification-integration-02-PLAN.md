---
phase: 03-verification-integration
plan: 02
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Core/Abstractions/Services.cs
  - src/STIGForge.Verify/VerificationWorkflowService.cs
  - src/STIGForge.Verify/VerificationWorkflowModels.cs
  - src/STIGForge.Cli/Program.cs
  - tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs
autonomous: true
must_haves:
  truths:
    - "Verification execution logic is shared instead of duplicated per surface."
    - "CLI and app layers can request verification runs via a common service contract."
    - "Verification execution emits consolidated outputs with consistent paths and metadata."
  artifacts:
    - path: "src/STIGForge.Core/Abstractions/Services.cs"
      provides: "Verification workflow service contract"
      contains: "interface"
    - path: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      provides: "Shared execution implementation"
      contains: "RunEvaluateStig|RunScap"
    - path: "tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs"
      provides: "Contract behavior regression coverage"
  key_links:
    - from: "src/STIGForge.Cli/Program.cs"
      to: "src/STIGForge.Verify/VerificationWorkflowService.cs"
      via: "dependency injection registration"
      pattern: "AddSingleton|AddScoped"
---

<objective>
Create one reusable verification workflow service so command and UI layers stop maintaining duplicate execution/report-writing code.

Purpose: Reduce divergence risk between operational surfaces and simplify future verification feature changes.
Output: New verification workflow contract, implementation, DI wiring, and unit-test coverage.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Cli/Program.cs
@src/STIGForge.Verify/EvaluateStigRunner.cs
@src/STIGForge.Verify/ScapRunner.cs
@src/STIGForge.Verify/VerifyReportWriter.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Define verification workflow contract and models</name>
  <files>src/STIGForge.Core/Abstractions/Services.cs, src/STIGForge.Verify/VerificationWorkflowModels.cs</files>
  <action>Add a focused service contract for verification execution requests/results (tool config, output root, timestamps, generated report paths, diagnostics). Keep model fields aligned with current bundle folder conventions and do not redesign unrelated service interfaces.</action>
  <verify>dotnet build STIGForge.sln</verify>
  <done>Core abstraction exposes a clear verification workflow API that both CLI and WPF can consume without tool-specific branching duplication.</done>
</task>

<task type="auto">
  <name>Task 2: Implement shared verification workflow service</name>
  <files>src/STIGForge.Verify/VerificationWorkflowService.cs</files>
  <action>Implement service methods to execute Evaluate-STIG and SCAP runners, create expected output directories, write consolidated JSON/CSV reports, and return structured result metadata. Preserve existing offline-first assumptions and avoid introducing remote dependencies or background daemons.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~VerificationWorkflowServiceTests"</verify>
  <done>Service runs each configured tool and consistently emits consolidated verify artifacts plus machine-readable operation diagnostics.</done>
</task>

<task type="auto">
  <name>Task 3: Register workflow service and add contract tests</name>
  <files>src/STIGForge.Cli/Program.cs, tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs</files>
  <action>Register the new workflow service in dependency injection and add tests validating output path creation, report file emission, and null/empty tool configuration behavior. Keep tests deterministic using fixture outputs and temporary directories.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~VerificationWorkflowService"</verify>
  <done>DI resolves the shared workflow service and tests prove deterministic file output behavior for configured and skipped tool paths.</done>
</task>

</tasks>

<verification>
Build solution and run focused workflow tests to confirm API stability and deterministic report output creation.
</verification>

<success_criteria>
One shared verification workflow service is available through DI, replacing duplicated execution/report-writing primitives for downstream integration.
</success_criteria>

<output>
After completion, create `.planning/phases/03-verification-integration/03-verification-integration-02-SUMMARY.md`
</output>

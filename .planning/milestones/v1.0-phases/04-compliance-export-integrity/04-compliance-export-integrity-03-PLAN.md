---
phase: 04-compliance-export-integrity
plan: 03
type: execute
wave: 3
depends_on:
  - 04-compliance-export-integrity-01
  - 04-compliance-export-integrity-02
files_modified:
  - src/STIGForge.Export/EmassExporter.cs
  - src/STIGForge.Cli/Commands/VerifyCommands.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs
autonomous: true
must_haves:
  truths:
    - "CLI and WPF export flows expose package validation outcomes clearly after export."
    - "Export process emits validation reports alongside package artifacts."
    - "Integration tests prove end-to-end package integrity for generated export bundles."
  artifacts:
    - path: "src/STIGForge.Export/EmassExporter.cs"
      provides: "Validation report file emission and result wiring"
      contains: "ValidatePackage|WriteValidationReport"
    - path: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      provides: "CLI-facing validation diagnostics"
      contains: "export-emass"
    - path: "tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs"
      provides: "End-to-end export integrity regression coverage"
  key_links:
    - from: "src/STIGForge.Export/EmassExporter.cs"
      to: "src/STIGForge.Export/EmassPackageValidator.cs"
      via: "validation invocation and report writing"
      pattern: "ValidatePackage"
    - from: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      to: "ExportResult.ValidationResult"
      via: "operator-visible console output"
      pattern: "ValidationResult"
---

<objective>
Complete export integrity delivery by surfacing validation diagnostics in operator flows and verifying full package behavior end-to-end.

Purpose: Ensure operators can trust export readiness decisions and quickly remediate submission blockers.
Output: Validation report artifacts, updated CLI/WPF diagnostics, and integration tests for complete export flow.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Export/EmassExporter.cs
@src/STIGForge.Cli/Commands/VerifyCommands.cs
@src/STIGForge.App/MainViewModel.ApplyVerify.cs
@.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-01-PLAN.md
@.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-02-PLAN.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Emit persistent validation reports from export pipeline</name>
  <files>src/STIGForge.Export/EmassExporter.cs</files>
  <action>After package validation, write both text and JSON validation reports into `00_Manifest` and include report paths/summary metrics in `ExportResult`. Keep current export directory layout and hash generation behavior intact.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ExportGeneratorTests"</verify>
  <done>Each export includes persistent validation reports and callers receive report locations through `ExportResult`.</done>
</task>

<task type="auto">
  <name>Task 2: Surface validation diagnostics in CLI and WPF export UX</name>
  <files>src/STIGForge.Cli/Commands/VerifyCommands.cs, src/STIGForge.App/MainViewModel.ApplyVerify.cs</files>
  <action>Update CLI and WPF export-result messaging to include validation pass/fail, error/warning counts, and report file locations. Preserve existing success-path messaging tone and avoid breaking command options or UI state refresh behavior.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~Export"</verify>
  <done>Operators can immediately see whether export is submission-ready and where detailed validation diagnostics are stored.</done>
</task>

<task type="auto">
  <name>Task 3: Add end-to-end eMASS export integrity integration tests</name>
  <files>tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs</files>
  <action>Add integration tests constructing realistic bundle inputs (verify results + manual answers/evidence), running `EmassExporter.ExportAsync`, and asserting package structure, cross-artifact consistency, and validation-report outputs. Include one negative case proving invalid package detection.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~EmassExporterIntegrationTests"</verify>
  <done>Integration coverage guards full export integrity behavior and catches regressions in package readiness checks.</done>
</task>

</tasks>

<verification>
Run export-focused integration tests and verify generated export packages contain validation reports plus expected artifact linkage.
</verification>

<success_criteria>
Export workflows produce submission-grade diagnostics and end-to-end integration tests enforce package integrity behavior.
</success_criteria>

<output>
After completion, create `.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-03-SUMMARY.md`
</output>

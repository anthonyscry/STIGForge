---
phase: 06-security-and-operational-hardening
plan: 04
type: execute
wave: 2
depends_on:
  - 06-01
files_modified:
  - src/STIGForge.Apply/ApplyRunner.cs
  - src/STIGForge.Apply/Reboot/RebootCoordinator.cs
  - src/STIGForge.Apply/Snapshot/RollbackScriptGenerator.cs
  - src/STIGForge.Export/EmassExporter.cs
  - src/STIGForge.Cli/Commands/VerifyCommands.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - src/STIGForge.Core/Services/BundleMissionSummaryService.cs
  - src/STIGForge.Cli/Commands/SupportBundleBuilder.cs
  - tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs
  - tests/STIGForge.UnitTests/Apply/RebootCoordinatorTests.cs
  - tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs
  - tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs
  - tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs
autonomous: true
must_haves:
  truths:
    - "Integrity-critical failures block mission completion"
    - "Export readiness status cannot be marked ready when package validation is invalid"
    - "Rollback remains operator-initiated with explicit recovery artifact pointers"
    - "Invalid or exhausted resume context requires explicit operator decision before continuation"
    - "Support bundles default to least-disclosure and exclude secrets/credentials by default"
    - "Run summaries distinguish blocking failures from warnings and optional skips"
  artifacts:
    - path: "src/STIGForge.Apply/ApplyRunner.cs"
      provides: "Fail-closed completion behavior for integrity-critical checkpoints"
    - path: "src/STIGForge.Export/EmassExporter.cs"
      provides: "Blocking export-readiness semantics for invalid package validation"
    - path: "src/STIGForge.Core/Services/BundleMissionSummaryService.cs"
      provides: "Mission summary classification for blocking failures vs warnings"
    - path: "src/STIGForge.Cli/Commands/SupportBundleBuilder.cs"
      provides: "Least-disclosure support bundle defaults with explicit sensitive-data controls"
  key_links:
    - from: "src/STIGForge.Export/EmassExporter.cs"
      to: "src/STIGForge.Export/EmassPackageValidator.cs"
      via: "validation result drives readiness blocking"
      pattern: "ValidationResult|IsValid|errors"
    - from: "src/STIGForge.Cli/Commands/VerifyCommands.cs"
      to: "export-emass command exit"
      via: "non-zero exit on integrity-invalid readiness"
      pattern: "Environment.ExitCode|invalid|validation"
    - from: "src/STIGForge.Apply/Reboot/RebootCoordinator.cs"
      to: "operator recovery flow"
      via: "invalid/exhausted resume context requires explicit stop-and-decide"
      pattern: "ResumeAfterReboot|invalid|decision|stop"
---

<objective>
Enforce fail-closed integrity checkpoints and mission-summary classification so readiness and completion states are safety-accurate.

Purpose: Deliver the Phase 06 requirement that integrity evidence is mandatory for mission completion and submission readiness.
Output: Apply/export/orchestrate flows that block on integrity-critical failures and produce clear blocking/warning/skip summaries.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/06-security-and-operational-hardening/06-CONTEXT.md
@.planning/phases/06-security-and-operational-hardening/06-RESEARCH.md
@.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-03-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Make integrity checks blocking for apply/export completion</name>
  <files>src/STIGForge.Apply/ApplyRunner.cs, src/STIGForge.Apply/Reboot/RebootCoordinator.cs, src/STIGForge.Apply/Snapshot/RollbackScriptGenerator.cs, src/STIGForge.Export/EmassExporter.cs, src/STIGForge.Cli/Commands/VerifyCommands.cs, src/STIGForge.App/MainViewModel.ApplyVerify.cs, tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs, tests/STIGForge.UnitTests/Apply/RebootCoordinatorTests.cs</files>
  <action>Implement fail-closed behavior for integrity-critical checkpoints: invalid audit chain, hash/tamper mismatch, required-artifact missing, and invalid export package readiness must block mission completion semantics with explicit failure status in CLI and WPF. Keep warning-only treatment for non-critical optional capability gaps. Preserve operator-initiated rollback and add explicit recovery artifact pointers in completion/failure messaging. Enforce that invalid or exhausted resume context stops continuation and requires explicit operator decision before proceeding.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~EmassPackageValidatorTests|FullyQualifiedName~ApplyRunnerTests|FullyQualifiedName~RebootCoordinatorTests"</verify>
  <done>Integrity-invalid conditions no longer allow successful completion/readiness states, and rollback/resume recovery behavior is explicit, operator-driven, and policy-compliant.</done>
</task>

<task type="auto">
  <name>Task 2: Add run-summary classification, least-disclosure support bundles, and end-to-end assertions</name>
  <files>src/STIGForge.Core/Services/BundleMissionSummaryService.cs, src/STIGForge.Cli/Commands/SupportBundleBuilder.cs, tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs, tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs, tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs</files>
  <action>Extend mission summary/report generation to classify outcomes into blocking failures, recoverable warnings, and optional skips, matching locked policy decisions. Update support bundle defaults to least-disclosure so secrets/credential material is excluded by default, with explicit high-risk opt-in semantics for sensitive additions. Add integration and regression tests that assert integrity-critical blocking behavior and support-bundle least-disclosure defaults.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~SupportBundleBuilderTests|FullyQualifiedName~EmassPackageValidatorTests" && dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~EmassExporterIntegrationTests"</verify>
  <done>Mission summaries clearly classify severity tiers, support bundles default to least disclosure, and tests prove fail-closed behavior for integrity-critical states.</done>
</task>

</tasks>

<verification>
- Execute targeted unit and integration suites for apply/export integrity behavior.
- Run representative `export-emass` flow and confirm invalid readiness produces blocking status and non-zero CLI exit.
</verification>

<success_criteria>
- Integrity-critical failure states are fail-closed across apply/export completion surfaces.
- Mission summary output clearly distinguishes blocking vs warning vs skipped outcomes.
</success_criteria>

<output>
After completion, create `.planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-04-SUMMARY.md`
</output>

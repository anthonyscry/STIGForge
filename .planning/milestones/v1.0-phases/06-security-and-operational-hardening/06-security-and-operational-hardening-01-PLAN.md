---
phase: 06-security-and-operational-hardening
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Cli/Commands/BuildCommands.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - src/STIGForge.Core/Services/ManualAnswerService.cs
  - src/STIGForge.Build/BundleOrchestrator.cs
  - tests/STIGForge.UnitTests/Cli/BuildCommandsTests.cs
  - tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs
autonomous: true
must_haves:
  truths:
    - "Operators cannot invoke safety-bypass actions silently"
    - "Every break-glass action captures an explicit reason"
    - "CLI and WPF apply/orchestrate surfaces enforce equivalent guard semantics"
  artifacts:
    - path: "src/STIGForge.Cli/Commands/BuildCommands.cs"
      provides: "Break-glass acknowledgment and reason validation for bypass flags"
    - path: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      provides: "WPF parity for high-risk action acknowledgment and reason capture"
    - path: "src/STIGForge.Build/BundleOrchestrator.cs"
      provides: "Audit entry emission for high-risk bypass usage"
  key_links:
    - from: "src/STIGForge.Cli/Commands/BuildCommands.cs"
      to: "IAuditTrailService"
      via: "break-glass audit entry with reason"
      pattern: "break-glass|reason|RecordAsync"
    - from: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      to: "STIGForge.Apply.ApplyRunner"
      via: "guarded bypass execution path"
      pattern: "skip-snapshot|high-risk|reason"
---

<objective>
Enforce explicit break-glass contracts for high-risk and safety-bypass operations so no destructive path executes silently.

Purpose: Align operator behavior with Phase 06 locked decisions for secure-by-default execution and auditable emergency overrides.
Output: Guarded CLI/WPF bypass flows with required reason capture and audit entries.
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
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add explicit break-glass contract to CLI high-risk flags</name>
  <files>src/STIGForge.Cli/Commands/BuildCommands.cs, tests/STIGForge.UnitTests/Cli/BuildCommandsTests.cs</files>
  <action>For bypass-capable actions (at minimum `--skip-snapshot` and `--force-auto-apply`), require explicit acknowledgment plus non-empty reason text before execution. Reject bypass invocation when acknowledgment or reason is missing. Keep safe defaults unchanged. Emit clear high-risk messaging and return non-zero exit on invalid invocation. Do not add new capabilities; only enforce guard semantics on existing commands.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BuildCommandsTests"</verify>
  <done>CLI bypass paths fail without acknowledgment/reason, succeed with valid break-glass input, and tests cover both positive and negative cases.</done>
</task>

<task type="auto">
  <name>Task 2: Add WPF parity and audit trace for break-glass behavior</name>
  <files>src/STIGForge.App/MainViewModel.ApplyVerify.cs, src/STIGForge.Core/Services/ManualAnswerService.cs, src/STIGForge.Build/BundleOrchestrator.cs, tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs</files>
  <action>Mirror the same high-risk acknowledgment semantics used in CLI for WPF apply/orchestrate paths so users cannot bypass safeguards silently in UI. Record audit entries with reason for break-glass paths and ensure reason-required validation remains consistent where risk decisions are captured. Preserve existing mission flow and keep deferred governance features out of scope.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~ManualAnswerServiceTests"</verify>
  <done>WPF and CLI enforce equivalent break-glass semantics and break-glass activity is auditable with explicit operator reason.</done>
</task>

</tasks>

<verification>
- Run CLI apply/build commands with and without break-glass acknowledgment to confirm guarded behavior.
- Confirm audit output includes high-risk action records with reason text.
</verification>

<success_criteria>
- No silent safety-bypass path remains in CLI or WPF for targeted operations.
- Break-glass actions are explicit, reasoned, and auditable.
</success_criteria>

<output>
After completion, create `.planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-01-SUMMARY.md`
</output>

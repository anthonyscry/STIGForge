---
phase: 08-upgrade-rebase-operator-workflow
plan: 02
type: execute
wave: 1
depends_on:
  - 08-upgrade-rebase-operator-workflow-01
files_modified:
  - src/STIGForge.Cli/Commands/DiffRebaseCommands.cs
  - src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs
  - src/STIGForge.App/Views/RebaseWizard.xaml
  - src/STIGForge.Core/Services/OverlayRebaseService.cs
  - tests/STIGForge.IntegrationTests/Cli/CliCommandTests.cs
  - tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs
  - docs/release/UpgradeAndRebaseValidation.md
autonomous: true
must_haves:
  truths:
    - "Operator-facing rebase flows provide explicit next actions for every unresolved blocking conflict."
    - "Rebase apply path remains deterministic and auditable even when action counts are high."
    - "Recovery and rollback guidance is visible in both CLI and WPF operator paths."
  artifacts:
    - path: "src/STIGForge.Cli/Commands/DiffRebaseCommands.cs"
      provides: "Operator-facing blocking conflict diagnostics and next-step guidance"
    - path: "src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs"
      provides: "WPF review-first UX for unresolved rebase actions"
    - path: "docs/release/UpgradeAndRebaseValidation.md"
      provides: "Updated release-review evidence requirements for rebase outcomes"
---

<objective>
Complete the Phase 08 operator safety layer so unresolved rebase outcomes are reviewable, actionable, and recovery-safe.

Purpose: Ensure operators can resolve conflicts intentionally without silent drift or ambiguous recovery paths.
Output: explicit conflict-resolution guidance in CLI/WPF, strengthened rebase diagnostics, and test-validated recovery semantics.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/ROADMAP.md
@.planning/REQUIREMENTS.md
@.planning/STATE.md
@.planning/phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md
@.planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-PLAN.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add operator-ready unresolved-conflict guidance in CLI and WPF paths</name>
  <files>src/STIGForge.Cli/Commands/DiffRebaseCommands.cs, src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs, src/STIGForge.App/Views/RebaseWizard.xaml</files>
  <action>When unresolved blocking actions remain, surface deterministic, control-level next-step guidance that tells operators what must be reviewed before rebase apply is allowed. Keep output ordering stable and include clear links to generated report artifacts.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.RebaseOverlay"</verify>
  <done>Operators can identify and act on unresolved conflicts without ambiguity in both CLI and WPF flows.</done>
</task>

<task type="auto">
  <name>Task 2: Strengthen recovery semantics and release evidence contracts</name>
  <files>src/STIGForge.Core/Services/OverlayRebaseService.cs, tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs, docs/release/UpgradeAndRebaseValidation.md</files>
  <action>Harden service-level recovery semantics so failed or blocked rebase runs emit consistent diagnostics and auditable artifact references. Extend tests for high-action-count and mixed-severity scenarios, then update release documentation with required evidence expectations.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~OverlayRebaseServiceTests"</verify>
  <done>Recovery semantics are deterministic and release review has explicit evidence requirements for blocked or warning-heavy rebase outcomes.</done>
</task>

</tasks>

<verification>
- Run targeted rebase unit and CLI integration tests.
- Validate unresolved-conflict user guidance appears in CLI and WPF for a representative blocking scenario.
</verification>

<success_criteria>
- Rebase unresolved conflicts are operator-actionable and fail-closed by default.
- Recovery guidance and required evidence are explicit and deterministic.
</success_criteria>

<output>
After completion, create `.planning/phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md`
</output>

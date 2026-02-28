---
phase: 08-upgrade-rebase-operator-workflow
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Core/Services/BaselineDiffService.cs
  - src/STIGForge.Core/Services/OverlayRebaseService.cs
  - src/STIGForge.Cli/Commands/DiffRebaseCommands.cs
  - src/STIGForge.App/ViewModels/DiffViewerViewModel.cs
  - src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs
  - tests/STIGForge.UnitTests/Services/BaselineDiffServiceTests.cs
  - tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs
  - tests/STIGForge.IntegrationTests/Cli/CliCommandTests.cs
  - docs/release/UpgradeAndRebaseValidation.md
autonomous: true
must_haves:
  truths:
    - "Diff outputs classify control changes with deterministic operator-actionable semantics, including review-required paths."
    - "Rebase completion is fail-closed when blocking conflicts remain unresolved."
    - "CLI and WPF surfaces expose equivalent rebase conflict/action intent for the same overlay and content packs."
  artifacts:
    - path: "src/STIGForge.Cli/Commands/DiffRebaseCommands.cs"
      provides: "Deterministic JSON and Markdown diff/rebase output semantics"
    - path: "src/STIGForge.Core/Services/OverlayRebaseService.cs"
      provides: "Blocking conflict classification and unresolved-action policy"
    - path: "src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs"
      provides: "Fail-closed rebase completion behavior in WPF"
  key_links:
    - from: "src/STIGForge.Core/Services/BaselineDiffService.cs"
      to: "src/STIGForge.Cli/Commands/DiffRebaseCommands.cs"
      via: "diff model classification maps to emitted operator reports"
      pattern: "ComparePacksAsync|ModifiedControls|Impact"
    - from: "src/STIGForge.Core/Services/OverlayRebaseService.cs"
      to: "src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs"
      via: "rebase action policy drives apply completion gate"
      pattern: "RebaseActionType|ReviewRequired|ApplyRebase"
---

<objective>
Harden diff/rebase deterministic behavior and fail-closed operator semantics as the first v1.1 execution slice.

Purpose: Deliver the requirement baseline for upgrade/rebase operator workflows before WPF parity and quality-gate expansion phases.
Output: deterministic diff/rebase artifacts, blocking unresolved-conflict policy, and regression tests proving behavior.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/REQUIREMENTS.md
@.planning/STATE.md
@.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md
@docs/release/UpgradeAndRebaseValidation.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Normalize deterministic diff/rebase reporting contracts</name>
  <files>src/STIGForge.Core/Services/BaselineDiffService.cs, src/STIGForge.Core/Services/OverlayRebaseService.cs, src/STIGForge.Cli/Commands/DiffRebaseCommands.cs, src/STIGForge.App/ViewModels/DiffViewerViewModel.cs, docs/release/UpgradeAndRebaseValidation.md</files>
  <action>Align diff and rebase report semantics so machine-readable and operator-readable artifacts carry the same deterministic classifications for changed controls and rebase actions. Ensure review-required signals are explicit and stable across reruns for identical inputs. Update release documentation to describe the normalized semantics and required evidence fields.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests"</verify>
  <done>Diff/rebase outputs and release documentation express one deterministic classification contract with explicit review-required semantics.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce unresolved-conflict fail-closed behavior in operator flows</name>
  <files>src/STIGForge.Core/Services/OverlayRebaseService.cs, src/STIGForge.Cli/Commands/DiffRebaseCommands.cs, src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs, tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs, tests/STIGForge.IntegrationTests/Cli/CliCommandTests.cs</files>
  <action>Gate rebase completion so unresolved blocking conflicts cannot be silently applied in CLI or WPF paths. Emit actionable diagnostics listing blocking controls and required operator actions. Keep non-blocking warnings visible but non-fatal. Preserve deterministic output ordering and error semantics for automated gate consumption.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~OverlayRebaseServiceTests" && dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"</verify>
  <done>Rebase apply paths fail closed on unresolved blocking conflicts while providing explicit operator recovery guidance.</done>
</task>

</tasks>

<verification>
- Run targeted unit and integration suites for diff/rebase deterministic contract behavior.
- Manually validate a representative rebase scenario in WPF that includes at least one review-required action.
</verification>

<success_criteria>
- Diff/rebase artifact contracts are deterministic and release-review ready.
- Rebase completion semantics are fail-closed for unresolved blocking conflicts.
- CLI and WPF expose equivalent conflict/action intent for the same scenario.
</success_criteria>

<output>
After completion, create `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md`
</output>

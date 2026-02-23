---
phase: 09-wpf-parity-and-recovery-ux
plan: 01
type: execute
wave: 1
depends_on:
  - 08-upgrade-rebase-operator-workflow-02
files_modified:
  - src/STIGForge.App/ViewModels/DiffViewerViewModel.cs
  - src/STIGForge.App/Views/DiffViewer.xaml
  - src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs
  - src/STIGForge.App/Views/RebaseWizard.xaml
  - src/STIGForge.App/MainViewModel.Dashboard.cs
  - docs/WpfGuide.md
  - docs/UserGuide.md
autonomous: true
must_haves:
  truths:
    - "WPF diff and rebase flows expose deterministic operator-readable and machine-readable artifacts without CLI fallback for standard paths."
    - "WPF rebase apply stays fail-closed when blocking conflicts remain unresolved."
    - "WPF rebase orchestration reuses core service semantics used by CLI."
  artifacts:
    - path: "src/STIGForge.App/ViewModels/DiffViewerViewModel.cs"
      provides: "Diff export parity for Markdown and JSON artifacts"
    - path: "src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs"
      provides: "Rebase analysis/apply orchestration with conflict and artifact guidance"
    - path: "docs/WpfGuide.md"
      provides: "Operator-facing WPF parity and export workflow documentation"
---

<objective>
Deliver Phase 09 Plan 01 by completing WPF-first diff/rebase workflow parity for standard operator paths.

Purpose: Remove operator dependency on CLI-only artifact export and conflict visibility for diff/rebase workflows.
Output: WPF diff/rebase surfaces include deterministic exports, blocking-conflict guidance, and shared fail-closed apply semantics.
</objective>

<tasks>

<task type="auto">
  <name>Task 1: Add WPF diff artifact parity to match CLI operator workflows</name>
  <files>src/STIGForge.App/ViewModels/DiffViewerViewModel.cs, src/STIGForge.App/Views/DiffViewer.xaml</files>
  <action>Expose review-required controls directly in the WPF diff viewer and add both Markdown and JSON export paths with deterministic naming to remove CLI-only artifact dependency.</action>
  <verify>dotnet build src/STIGForge.App/STIGForge.App.csproj --configuration Release -p:EnableWindowsTargeting=true</verify>
  <done>WPF diff flow provides deterministic review visibility and machine/operator artifact exports.</done>
</task>

<task type="auto">
  <name>Task 2: Complete WPF rebase analysis/apply parity and audit wiring</name>
  <files>src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs, src/STIGForge.App/Views/RebaseWizard.xaml, src/STIGForge.App/MainViewModel.Dashboard.cs</files>
  <action>Add confidence/risk summaries, Markdown/JSON report exports, blocking-conflict recommended action visibility, and wire WPF rebase service construction to include audit service for parity with CLI semantics.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests"</verify>
  <done>WPF rebase flow uses shared fail-closed core behavior with in-app artifact generation and operator guidance.</done>
</task>

<task type="auto">
  <name>Task 3: Update WPF operator documentation for parity workflows</name>
  <files>docs/WpfGuide.md, docs/UserGuide.md</files>
  <action>Document in-app diff/rebase usage, artifact export options, and blocking-conflict apply behavior so operators can complete standard quarterly update flows without switching to CLI.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"</verify>
  <done>Operator docs describe WPF-first workflow parity and expected conflict handling behavior.</done>
</task>

</tasks>

<verification>
- Build WPF app project with Windows targeting enabled.
- Run focused diff/rebase unit and integration tests.
- Run full net8 unit and integration test suites as regression guard.
- Build full solution with Windows targeting enabled.
</verification>

<success_criteria>
- WPF operators can execute diff/rebase flows and export release artifacts without CLI fallback for standard paths.
- WPF rebase apply remains blocked while unresolved blocking conflicts exist.
- WPF and CLI rely on shared core semantics for conflict classification and recommended actions.
</success_criteria>

<output>
After completion, create `.planning/phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md`.
</output>

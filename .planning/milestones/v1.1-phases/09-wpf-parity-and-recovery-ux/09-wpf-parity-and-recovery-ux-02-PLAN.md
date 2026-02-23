---
phase: 09-wpf-parity-and-recovery-ux
plan: 02
type: execute
wave: 1
depends_on:
  - 09-wpf-parity-and-recovery-ux-01
files_modified:
  - src/STIGForge.App/MainViewModel.cs
  - src/STIGForge.App/MainViewModel.Dashboard.cs
  - src/STIGForge.App/MainViewModel.ApplyVerify.cs
  - src/STIGForge.App/MainWindow.xaml
  - src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs
  - src/STIGForge.App/Views/RebaseWizard.xaml
  - docs/WpfGuide.md
  - docs/UserGuide.md
autonomous: true
must_haves:
  truths:
    - "WPF mission summaries use the same blocking/warning/optional-skip semantics as CLI bundle summaries."
    - "WPF apply and rebase failure paths provide actionable operator recovery guidance."
    - "Recovery guidance includes required artifacts, next action, and rollback stance."
  artifacts:
    - path: "src/STIGForge.App/MainViewModel.Dashboard.cs"
      provides: "Mission severity + recovery guidance surfaced in dashboard"
    - path: "src/STIGForge.App/MainViewModel.ApplyVerify.cs"
      provides: "Severity-consistent verify/report summary strings and apply/orchestrate recovery guidance"
    - path: "src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs"
      provides: "Rebase blocking guidance and recovery instructions"
---

<objective>
Deliver Phase 09 Plan 02 by aligning WPF mission severity language with CLI semantics and exposing explicit recovery guidance for failed apply/rebase outcomes.

Purpose: Ensure operators get consistent severity interpretation and next-step guidance regardless of UI or CLI surface.
Output: WPF dashboard/verify/report/rebase surfaces show severity-consistent summaries and operator recovery instructions.
</objective>

<tasks>

<task type="auto">
  <name>Task 1: Align WPF mission summary semantics to CLI severity model</name>
  <files>src/STIGForge.App/MainViewModel.Dashboard.cs, src/STIGForge.App/MainViewModel.ApplyVerify.cs, src/STIGForge.App/MainViewModel.cs, src/STIGForge.App/MainWindow.xaml</files>
  <action>Surface CLI-aligned mission severity counters (`blocking`, `warnings`, `optional-skips`) and recovery guidance in dashboard and summary surfaces, ensuring verify/report messages use the same severity framing.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BundleMissionSummaryServiceTests"</verify>
  <done>WPF mission summaries communicate severity and operational impact with the same semantics as CLI bundle summary outputs.</done>
</task>

<task type="auto">
  <name>Task 2: Add explicit recovery guidance for failed apply/rebase operator flows</name>
  <files>src/STIGForge.App/MainViewModel.ApplyVerify.cs, src/STIGForge.App/ViewModels/RebaseWizardViewModel.cs, src/STIGForge.App/Views/RebaseWizard.xaml</files>
  <action>Add actionable guidance text to failure/blocked states with required artifacts, next actions, and rollback position for operator decision points.</action>
  <verify>dotnet build src/STIGForge.App/STIGForge.App.csproj --configuration Release -p:EnableWindowsTargeting=true</verify>
  <done>WPF apply/rebase failure surfaces provide deterministic guidance without requiring CLI lookup.</done>
</task>

<task type="auto">
  <name>Task 3: Update operator docs for severity and recovery UX changes</name>
  <files>docs/WpfGuide.md, docs/UserGuide.md</files>
  <action>Document dashboard severity interpretation and new recovery guidance behavior so operators understand expected action flow for blocked/warning outcomes.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"</verify>
  <done>Operator documentation reflects WPF parity behavior and severity/recovery semantics.</done>
</task>

</tasks>

<verification>
- Build WPF app project.
- Run mission-summary/rebase/diff targeted tests.
- Run full net8 unit and integration suites.
- Build full solution with Windows targeting.
- Run C# LSP diagnostics on changed WPF viewmodel files.
</verification>

<success_criteria>
- WPF mission summaries classify blocking/warning/optional-skip in parity with CLI semantics.
- Failed apply/rebase states include required artifacts, next action, and rollback guidance in WPF surfaces.
- Documentation reflects updated severity and recovery behavior.
</success_criteria>

<output>
After completion, create `.planning/phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md`.
</output>

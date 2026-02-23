---
phase: 05-operator-workflow-completion
plan: 03
type: execute
wave: 2
depends_on:
  - 05-operator-workflow-completion-01
files_modified:
  - src/STIGForge.Core/Services/ManualAnswerService.cs
  - src/STIGForge.App/MainViewModel.Manual.cs
  - src/STIGForge.App/MainWindow.xaml
  - src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs
  - src/STIGForge.App/Views/ManualCheckWizard.xaml
  - tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs
  - tests/STIGForge.UnitTests/Evidence/EvidenceAutopilotTests.cs
autonomous: true
must_haves:
  truths:
    - "Manual answers saved from wizard and tab views use one canonical status vocabulary."
    - "Operators can collect control-linked evidence during manual review without leaving the wizard flow."
    - "Manual review UI surfaces actionable progress and evidence shortcuts for faster throughput."
  artifacts:
    - path: "src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs"
      provides: "Wizard status normalization, validation, and evidence collection commands"
      contains: "SaveAndNext|CollectEvidence"
    - path: "src/STIGForge.App/MainViewModel.Manual.cs"
      provides: "Manual-tab status/evidence ergonomics"
      contains: "SaveManualAnswer|LoadManualControls"
    - path: "tests/STIGForge.UnitTests/Evidence/EvidenceAutopilotTests.cs"
      provides: "Autopilot evidence collection regression coverage"
  key_links:
    - from: "src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs"
      to: "src/STIGForge.Core/Services/ManualAnswerService.cs"
      via: "save/load normalized manual statuses"
      pattern: "SaveAnswer|Normalize"
    - from: "src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs"
      to: "src/STIGForge.Evidence/EvidenceAutopilot.cs"
      via: "in-wizard evidence capture"
      pattern: "CollectEvidenceAsync"
---

<objective>
Finish high-ROI operator workflow by making manual review faster, status-consistent, and evidence-friendly directly inside the app.

Purpose: Reduce review friction and remove tool thrash between manual answering and evidence capture.
Output: Canonical manual status handling, evidence autopilot integration in wizard flow, and regression tests.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/05-operator-workflow-completion/05-operator-workflow-completion-01-PLAN.md
@src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs
@src/STIGForge.App/MainViewModel.Manual.cs
@src/STIGForge.Evidence/EvidenceAutopilot.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Canonicalize manual-answer status handling across app workflows</name>
  <files>src/STIGForge.Core/Services/ManualAnswerService.cs, src/STIGForge.App/MainViewModel.Manual.cs, src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs</files>
  <action>Add shared normalization so legacy aliases (`NotAFinding`, `Open`) map cleanly to app vocabulary (`Pass`, `Fail`, `NotApplicable`, `Open`) while preserving backward compatibility for existing answer files. Update wizard/tab save paths to write canonical statuses and require reason text for `Fail` and `NotApplicable` decisions.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ManualAnswerServiceTests"</verify>
  <done>Manual progress, filters, and dashboard calculations stay consistent regardless of answer entry path or legacy status wording.</done>
</task>

<task type="auto">
  <name>Task 2: Add evidence autopilot actions inside manual review flow</name>
  <files>src/STIGForge.App/ViewModels/ManualCheckWizardViewModel.cs, src/STIGForge.App/Views/ManualCheckWizard.xaml, src/STIGForge.App/MainViewModel.Manual.cs, src/STIGForge.App/MainWindow.xaml</files>
  <action>Introduce a wizard-level `Collect Evidence` action for the current control using `EvidenceAutopilot`, display collection outcome (files/errors), and add manual-tab shortcuts that pre-fill `EvidenceRuleId` from selected control plus open that control's evidence folder. Keep all evidence artifacts under bundle-local paths for offline operation.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~EvidenceCollectorTests"</verify>
  <done>Operators can capture and inspect control-linked evidence during manual review without switching to CLI or manually typing rule IDs.</done>
</task>

<task type="auto">
  <name>Task 3: Add regression tests for manual/evidence workflow behavior</name>
  <files>tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs, tests/STIGForge.UnitTests/Evidence/EvidenceAutopilotTests.cs</files>
  <action>Extend manual-answer tests for alias normalization and reason-required scenarios, and add evidence-autopilot tests that validate per-control output directory creation and graceful error capture when a probe command/file is unavailable.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ManualAnswerServiceTests|FullyQualifiedName~EvidenceAutopilotTests"</verify>
  <done>Manual/evidence workflow regressions are test-guarded across canonical status handling and autopilot artifact generation.</done>
</task>

</tasks>

<verification>
Run manual/evidence unit tests and validate that manual review now supports in-flow evidence capture with consistent status semantics.
</verification>

<success_criteria>
Manual wizard throughput improves through canonical statuses and direct evidence capture, enabling end-to-end manual control completion in WPF alone.
</success_criteria>

<output>
After completion, create `.planning/phases/05-operator-workflow-completion/05-operator-workflow-completion-03-SUMMARY.md`
</output>

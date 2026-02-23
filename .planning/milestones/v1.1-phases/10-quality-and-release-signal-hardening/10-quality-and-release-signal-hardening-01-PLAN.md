---
phase: 10-quality-and-release-signal-hardening
plan: 01
type: execute
wave: 1
depends_on:
  - 09-wpf-parity-and-recovery-ux-02
files_modified:
  - tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs
  - tools/release/Invoke-ReleaseGate.ps1
  - .github/workflows/ci.yml
  - .github/workflows/release-package.yml
  - .github/workflows/vm-smoke-matrix.yml
  - docs/release/UpgradeAndRebaseValidation.md
autonomous: true
must_haves:
  truths:
    - "CI has deterministic, explicit coverage for diff/rebase conflict handling and mission-severity parity semantics."
    - "Release-gate upgrade/rebase evidence includes WPF parity-critical mission-summary contract signals."
    - "Release-package and VM smoke workflows fail when parity evidence is missing or degraded."
  artifacts:
    - path: "tools/release/Invoke-ReleaseGate.ps1"
      provides: "Deterministic parity-critical contract step in upgrade/rebase evidence"
    - path: ".github/workflows/release-package.yml"
      provides: "Workflow-level enforcement that parity evidence remains present and passed"
    - path: ".github/workflows/vm-smoke-matrix.yml"
      provides: "VM runner enforcement for parity evidence in go/no-go inputs"
---

<objective>
Deliver Phase 10 Plan 01 by hardening deterministic automated coverage and gating for v1.1 diff/rebase + parity-critical paths.

Purpose: prevent regressions in core upgrade/rebase and mission-severity parity behavior from reaching release promotion.
Output: explicit contract coverage in CI and release-gate/workflow evidence that includes parity-critical validation.
</objective>

<tasks>

<task type="auto">
  <name>Task 1: Expand deterministic parity-critical test coverage</name>
  <files>tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs</files>
  <action>Add targeted tests for mission summary normalization and manifest fallback paths that back WPF/CLI parity semantics used in release decisions.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BundleMissionSummaryServiceTests"</verify>
  <done>Parity-critical mission-summary logic is explicitly covered with deterministic tests.</done>
</task>

<task type="auto">
  <name>Task 2: Promote parity contract into release-gate evidence model</name>
  <files>tools/release/Invoke-ReleaseGate.ps1, docs/release/UpgradeAndRebaseValidation.md</files>
  <action>Add an explicit upgrade/rebase parity contract step (mission summary parity coverage), include it in required evidence lists/reporting, and document promotion expectations.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests|FullyQualifiedName~BundleMissionSummaryServiceTests"</verify>
  <done>Release gate emits parity-inclusive upgrade/rebase evidence and docs reflect the expanded contract set.</done>
</task>

<task type="auto">
  <name>Task 3: Enforce parity evidence in CI + promotion workflows</name>
  <files>.github/workflows/ci.yml, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Add deterministic CI contract run and explicit workflow checks that the new parity evidence step exists and remains passed before promotion.</action>
  <verify>dotnet build STIGForge.sln --configuration Release -p:EnableWindowsTargeting=true</verify>
  <done>CI/release workflows fail closed when parity validation evidence is missing or regressed.</done>
</task>

</tasks>

<verification>
- Run targeted mission-summary + diff/rebase contract tests.
- Run full net8 unit and integration suites.
- Build full solution with Windows targeting.
- Run C# LSP diagnostics on changed C# files.
</verification>

<success_criteria>
- QA-01 coverage is explicit and deterministic in CI for diff/rebase + conflict handling + parity-critical mission summary logic.
- QA-02 evidence includes parity-critical validation signals in release gate outputs consumed by release-package and VM workflows.
- Workflow checks fail closed when parity evidence is absent or failed.
</success_criteria>

<output>
After completion, create `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-01-SUMMARY.md`.
</output>

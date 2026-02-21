---
phase: 07-release-readiness-and-compatibility
plan: 03
type: execute
wave: 2
depends_on:
  - 07-01
files_modified:
  - tools/release/Invoke-ReleaseGate.ps1
  - tools/release/Invoke-SecurityGate.ps1
  - tools/release/quarterly-regression-pack.psd1
  - tools/release/Run-QuarterlyRegressionPack.ps1
  - .github/workflows/release-package.yml
  - docs/release/QuarterlyRegressionPack.md
autonomous: true
must_haves:
  truths:
    - "Quarterly regression packs are versioned, repeatable, and auditable."
    - "Compatibility drift detection is explicit and machine-readable."
    - "Regression pack execution is integrated into release readiness workflow."
  artifacts:
    - "tools/release/quarterly-regression-pack.psd1"
    - "tools/release/Run-QuarterlyRegressionPack.ps1"
    - "docs/release/QuarterlyRegressionPack.md"
  key_links:
    - "quarterly pack manifest -> release gate/report artifacts"
    - "regression drift report -> release promotion decision"
---

<objective>
Create quarterly compatibility regression pack execution and drift detection as first-class release artifacts.

Purpose: Ensure quarterly content updates have repeatable, comparable compatibility evidence before release promotion.
Output: Quarterly pack manifest + runner, drift report artifacts, and release workflow integration.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/07-release-readiness-and-compatibility/07-RESEARCH.md
@.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-01-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Implement quarterly regression pack manifest and deterministic runner</name>
  <files>tools/release/quarterly-regression-pack.psd1, tools/release/Run-QuarterlyRegressionPack.ps1, docs/release/QuarterlyRegressionPack.md</files>
  <action>Define an immutable manifest format for quarterly regression packs (fixture set, expected outputs, baseline references, drift thresholds). Implement a deterministic runner that executes the pack and emits machine-readable summary + drift outputs suitable for release gate consumption. Keep design aligned with offline-first and PowerShell 5.1 constraints.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Run-QuarterlyRegressionPack.ps1 -PackPath .\tools\release\quarterly-regression-pack.psd1 -OutputRoot .\.artifacts\quarterly-pack\phase07</verify>
  <done>Quarterly regression pack can be executed repeatably and produces explicit compatibility drift artifacts.</done>
</task>

<task type="auto">
  <name>Task 2: Integrate quarterly pack and drift results into release gates</name>
  <files>tools/release/Invoke-ReleaseGate.ps1, tools/release/Invoke-SecurityGate.ps1, .github/workflows/release-package.yml</files>
  <action>Wire quarterly pack execution and drift-report interpretation into release workflow so compatibility drift is treated as a release-candidate quality signal. Ensure failures/warnings are surfaced in existing gate reports without breaking current deterministic offline security semantics.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\phase07-quarterly</verify>
  <done>Release gate artifacts include quarterly compatibility drift outcomes and reflect pass/fail behavior according to manifest policy.</done>
</task>

</tasks>

<verification>
- Execute quarterly regression runner and inspect summary + drift artifact outputs.
- Execute release gate and confirm quarterly drift data is included in report artifacts.
</verification>

<success_criteria>
- Quarterly compatibility regression is automated, deterministic, and evidence-producing.
- Drift detection is a formal input to release readiness decisions.
</success_criteria>

<output>
After completion, create `.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-03-SUMMARY.md`
</output>

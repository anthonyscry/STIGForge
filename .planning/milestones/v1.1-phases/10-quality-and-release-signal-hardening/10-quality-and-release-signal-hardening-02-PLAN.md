---
phase: 10-quality-and-release-signal-hardening
plan: 02
type: execute
wave: 1
depends_on:
  - 10-quality-and-release-signal-hardening-01
files_modified:
  - .github/workflows/ci.yml
  - .github/workflows/release-package.yml
  - .github/workflows/vm-smoke-matrix.yml
  - docs/testing/StabilityBudget.md
  - docs/release/QuarterlyRegressionPack.md
  - docs/release/ReleaseCandidatePlaybook.md
  - docs/release/ShipReadinessChecklist.md
autonomous: true
must_haves:
  truths:
    - "Promotion workflows fail closed when trendable stability/compatibility artifacts are missing or indicate drift failure."
    - "Release decision flow references machine-readable summaries, not only command exit codes."
    - "Trend signal artifact paths remain deterministic across CI, release-package, and VM workflows."
  artifacts:
    - path: ".github/workflows/release-package.yml"
      provides: "Release-package enforcement of quarterly drift and trend-signal summary artifacts"
    - path: ".github/workflows/vm-smoke-matrix.yml"
      provides: "VM-level enforcement of quarterly drift and stability summary semantics"
    - path: "docs/testing/StabilityBudget.md"
      provides: "Operator-facing trend signal policy and required artifact contract"
---

<objective>
Deliver Phase 10 Plan 02 by promoting stability/compatibility trend signals into explicit release decision gates.

Purpose: ensure go/no-go decisions are based on deterministic machine-readable trend evidence instead of implicit pass assumptions.
Output: CI/release/VM workflows validate presence and pass state of quarterly + stability summaries and fail closed on missing/regressed signals.
</objective>

<tasks>

<task type="auto">
  <name>Task 1: Enforce quarterly trend signal checks in promotion workflows</name>
  <files>.github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Add explicit checks for quarterly-pack summary/report artifacts and required summary status/decision fields before promotion continues.</action>
  <verify>dotnet build STIGForge.sln --configuration Release -p:EnableWindowsTargeting=true</verify>
  <done>Release-package and VM workflows fail closed when quarterly drift evidence is missing or non-passing.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce stability trend signal checks in CI and release flow</name>
  <files>.github/workflows/ci.yml, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Validate stability-budget summary/report artifacts exist and indicate pass conditions where required; ensure release flow references these summaries as decision evidence.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0</verify>
  <done>Stability summary artifacts are treated as first-class release inputs across CI and VM evidence paths.</done>
</task>

<task type="auto">
  <name>Task 3: Document trend-signal promotion policy</name>
  <files>docs/testing/StabilityBudget.md, docs/release/QuarterlyRegressionPack.md, docs/release/ReleaseCandidatePlaybook.md, docs/release/ShipReadinessChecklist.md</files>
  <action>Update docs so release operators have explicit trend-signal requirements and artifact expectations aligned with workflow enforcement.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0</verify>
  <done>Documentation matches enforced workflow behavior for trend-signal go/no-go decisions.</done>
</task>

</tasks>

<verification>
- Run full net8 unit and integration suites.
- Build full solution with Windows targeting.
- Re-run C# LSP diagnostics on any changed C# files (if changed).
</verification>

<success_criteria>
- QA-03 satisfied with explicit workflow checks for trendable stability/compatibility artifacts.
- Release promotion flow includes deterministic machine-readable trend signal validation.
- Documentation and workflow checks are aligned.
</success_criteria>

<output>
After completion, create `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-02-SUMMARY.md`.
</output>

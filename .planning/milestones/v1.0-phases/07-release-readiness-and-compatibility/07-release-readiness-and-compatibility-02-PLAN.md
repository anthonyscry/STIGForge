---
phase: 07-release-readiness-and-compatibility
plan: 02
type: execute
wave: 2
depends_on:
  - 07-01
files_modified:
  - tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs
  - tests/STIGForge.IntegrationTests/Apply/SnapshotIntegrationTests.cs
  - tests/STIGForge.UnitTests/SmokeTests.cs
  - .github/workflows/vm-smoke-matrix.yml
  - .github/workflows/ci.yml
  - docs/testing/StabilityBudget.md
autonomous: true
must_haves:
  truths:
    - "Long-run stability and smoke checks are explicit and reproducible."
    - "Target-environment smoke coverage is a tracked release signal."
    - "Stability budget failures are visible before release promotion."
  artifacts:
    - "docs/testing/StabilityBudget.md"
    - "tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs"
    - ".github/workflows/vm-smoke-matrix.yml"
  key_links:
    - "fixture matrix -> soak/smoke execution inputs"
    - "stability budget thresholds -> CI and VM smoke gating"
---

<objective>
Implement long-run stability validation using deterministic tests and explicit stability budgets tied to release promotion.

Purpose: Convert existing smoke and E2E coverage into release-quality stability evidence across target environments.
Output: Stability budget definitions, hardened deterministic long-run tests, and CI/VM smoke gate integration.
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
  <name>Task 1: Harden long-run/soak test determinism and define stability budget</name>
  <files>tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs, tests/STIGForge.IntegrationTests/Apply/SnapshotIntegrationTests.cs, tests/STIGForge.UnitTests/SmokeTests.cs, docs/testing/StabilityBudget.md</files>
  <action>Eliminate avoidable time/environment flakiness in long-run test paths (fixed time inputs where practical, explicit skip semantics when privileged prerequisites are absent, deterministic assertions). Define a stability budget document with explicit pass/fail thresholds for release readiness (flake tolerance, rerun policy, required passing suites).</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~E2E|FullyQualifiedName~SnapshotIntegrationTests"</verify>
  <done>Long-run tests are more deterministic, and stability budget criteria exist with clear release-gate interpretation.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce stability budget signals in CI and VM smoke workflows</name>
  <files>.github/workflows/vm-smoke-matrix.yml, .github/workflows/ci.yml</files>
  <action>Wire stability budget checks into CI and VM smoke workflows so release promotion consumes explicit stability signals (not informal/manual interpretation). Keep existing release/security gates intact while adding compatibility/stability budget evidence as required artifacts.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\phase07-stability</verify>
  <done>CI and VM smoke workflows surface and enforce stability-budget evidence needed for release-candidate decisions.</done>
</task>

</tasks>

<verification>
- Run targeted integration suites for long-run and snapshot paths.
- Confirm CI/VM workflows publish stability-budget evidence artifacts.
</verification>

<success_criteria>
- Stability readiness is measured with explicit thresholds and automated signals.
- Target environment smoke checks contribute directly to release go/no-go confidence.
</success_criteria>

<output>
After completion, create `.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-02-SUMMARY.md`
</output>

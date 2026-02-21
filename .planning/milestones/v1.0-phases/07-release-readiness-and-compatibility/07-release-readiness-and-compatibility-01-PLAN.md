---
phase: 07-release-readiness-and-compatibility
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/STIGForge.UnitTests/fixtures
  - tests/STIGForge.IntegrationTests/fixtures
  - tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
  - tests/STIGForge.IntegrationTests/Content/RoundTripTests.cs
  - docs/testing/CompatibilityFixtureMatrix.md
  - .github/workflows/ci.yml
autonomous: true
must_haves:
  truths:
    - "Fixture coverage for supported formats and update paths is explicit and reviewable."
    - "Compatibility matrix semantics are regression-tested and fail CI on drift."
    - "Fixture updates are deterministic and governed by a documented contract."
  artifacts:
    - "docs/testing/CompatibilityFixtureMatrix.md"
    - "tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs"
    - "tests/STIGForge.IntegrationTests/Content/RoundTripTests.cs"
  key_links:
    - "compatibility_matrix.json generation -> fixture contract assertions"
    - "fixture corpus updates -> CI compatibility gate"
---

<objective>
Establish the Phase 07 fixture and compatibility baseline so release validation is data-driven instead of ad hoc.

Purpose: Build a deterministic fixture matrix and enforce compatibility contract checks early in CI.
Output: Expanded fixture matrix docs, stronger fixture-backed compatibility tests, and CI wiring for compatibility contract gating.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/07-release-readiness-and-compatibility/07-RESEARCH.md
@docs/release/ShipReadinessChecklist.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Define and document deterministic compatibility fixture matrix</name>
  <files>docs/testing/CompatibilityFixtureMatrix.md, tests/STIGForge.UnitTests/fixtures, tests/STIGForge.IntegrationTests/fixtures</files>
  <action>Create a fixture matrix document mapping formats (STIG/XCCDF/OVAL/SCAP/GPO), update scenarios (baseline, quarterly delta, malformed/adversarial), and expected compatibility outcomes. Add or normalize fixture assets where matrix gaps exist, keeping deterministic naming and stable contents. Do not add new product features; only strengthen release-compatibility evidence inputs.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~Content"</verify>
  <done>Fixture matrix exists, fixture corpus covers identified high-risk compatibility gaps, and content tests target matrix-defined scenarios.</done>
</task>

<task type="auto">
  <name>Task 2: Gate compatibility-matrix contract behavior in automated validation</name>
  <files>tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs, tests/STIGForge.IntegrationTests/Content/RoundTripTests.cs, .github/workflows/ci.yml</files>
  <action>Add explicit assertions for compatibility matrix contract output (required keys, deterministic structure, and expected warning/error classifications) and wire these checks into CI. Keep failure behavior deterministic and actionable so quarterly-content drift is surfaced as a gate failure rather than runtime surprise.</action>
  <verify>dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~RoundTrip"</verify>
  <done>Compatibility-matrix contract behavior is covered by tests and enforced in CI as a release-readiness signal.</done>
</task>

</tasks>

<verification>
- Run unit and integration content tests for fixture and compatibility-matrix validation.
- Confirm CI workflow includes compatibility contract checks.
</verification>

<success_criteria>
- Fixture coverage and compatibility expectations are explicitly documented and test-enforced.
- Compatibility drift appears as a deterministic CI failure.
</success_criteria>

<output>
After completion, create `.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-01-SUMMARY.md`
</output>

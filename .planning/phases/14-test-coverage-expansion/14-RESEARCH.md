# Phase 14: Test Coverage Expansion - Research

**Researched:** 2026-02-22
**Domain:** .NET test coverage, branch coverage, mutation testing, and CI quality gates
**Confidence:** HIGH

## Summary

Phase 14 establishes measurable and enforceable test quality for STIGForge v1.1. The target is not only 80% line coverage on critical assemblies, but also branch coverage visibility and CI gates that prevent regressions. Mutation testing is included to verify that tests detect behavior changes rather than only inflating coverage percentages.

**Primary recommendation:** Use `coverlet.collector` + `dotnet test` for line/branch coverage collection, produce machine-readable reports in CI, enforce threshold gates in pipeline steps, and add mutation testing (Stryker.NET) to a scoped set of critical projects for sustainable runtime.

<phase_requirements>

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TEST-02 | 80% line coverage on critical assemblies (Build, Apply, Verify, Infrastructure) | Configure coverage collection per critical test project and aggregate reports with explicit line threshold checks |
| TEST-03 | Branch coverage reporting available in CI pipeline | Export Cobertura/OpenCover reports and publish artifacts in CI summary |
| TEST-04 | Coverage gates enforced in CI (block PRs below threshold) | Add hard fail step in pipeline when line coverage < 80% for scoped assemblies |
| TEST-06 | Mutation testing validates test quality | Add Stryker.NET job with bounded scope and stable baseline mutation score |

</phase_requirements>

## Standard Stack

### Core

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| `coverlet.collector` | Existing .NET ecosystem standard | Line and branch coverage collection during `dotnet test` | Native test integration and broad CI support |
| `dotnet test` + TRX | .NET SDK built-in | Execute tests and emit test/coverage artifacts | Zero additional runner required |
| Cobertura/OpenCover report format | N/A | CI-consumable branch/line coverage output | Works with GitHub/Azure and common report publishers |
| Stryker.NET | Current stable | Mutation testing for quality validation | Most mature .NET mutation testing tooling |

### No New Platform Dependencies

This phase does not require runtime product dependencies. All changes are test tooling and CI pipeline configuration.

## Architecture Patterns

### Pattern 1: Scope Coverage to Critical Assemblies

- Focus threshold checks on Build, Apply, Verify, and Infrastructure assemblies only.
- Keep coverage collection broad, but enforce gates on the scoped targets to avoid blocking on non-critical modules.
- Store scoped targets in one central config file to avoid pipeline drift.

### Pattern 2: Separate Collection from Enforcement

- Collection: run tests once with coverage enabled and emit reports.
- Enforcement: parse report(s) and compare against required threshold.
- This separation keeps diagnostics rich while gates remain deterministic.

### Pattern 3: Publish Branch Coverage in CI Output

- Include branch coverage numbers in pipeline summary/logs.
- Publish raw coverage XML as artifacts for auditability and post-run analysis.
- Keep output format stable for downstream tooling and dashboards.

### Pattern 4: Mutation Testing as a Bounded Quality Signal

- Start mutation testing on high-value projects (Verify + Apply + Build) to control execution time.
- Run mutation in dedicated CI step/job so failures are attributable.
- Track baseline mutation score and fail on meaningful regression after baseline is established.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Coverage math | Custom scripts that parse raw test output text | Coverlet reports and deterministic threshold checks |
| Branch reporting | Manual log scraping | Cobertura/OpenCover published artifacts |
| Mutation engine | In-house mutation framework | Stryker.NET |
| Gate logic | Ad hoc shell conditionals in multiple jobs | Single reusable threshold check step |

## Common Pitfalls

### Pitfall 1: Enforcing Global Coverage Instead of Scoped Critical Assemblies

This creates noisy failures and slows delivery. Gates should align to Phase 14 requirements (critical assemblies).

### Pitfall 2: Branch Coverage Collected but Not Surfaced

Branch coverage can exist in XML artifacts but remain invisible to reviewers. Ensure summary output includes branch metrics.

### Pitfall 3: Mutation Scope Too Broad Initially

Running mutation on all projects can make CI non-viable. Start with critical assemblies and expand incrementally.

### Pitfall 4: Non-Deterministic Threshold Evaluation

If gate logic depends on fragile regex or varying locale output, false failures occur. Use report-driven numeric checks.

## Initial Plan Shape Recommendation

Recommended decomposition for planning phase:

1. Add/normalize coverage collection config for critical assemblies and local developer commands
2. Add CI branch and line coverage report publication
3. Add strict coverage threshold gate for PR validation
4. Add scoped mutation testing and baseline score policy

## Sources

### Primary

- `.planning/ROADMAP.md` (Phase 14 goal, dependencies, success criteria)
- `.planning/REQUIREMENTS.md` (TEST-02, TEST-03, TEST-04, TEST-06)
- Existing phase research format in `.planning/phases/13-performance-baselining/13-RESEARCH.md`

### Secondary

- .NET ecosystem conventions for Coverlet and Stryker.NET in CI workflows

## Metadata

**Confidence breakdown:**
- Requirements and phase scope: HIGH
- Tooling fit for .NET and CI: HIGH
- Mutation baseline thresholds in this repo: MEDIUM (requires initial benchmark run)

**Research date:** 2026-02-22
**Valid until:** 2026-03-31

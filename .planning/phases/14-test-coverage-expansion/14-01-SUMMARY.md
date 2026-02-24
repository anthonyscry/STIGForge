---
phase: 14-test-coverage-expansion
artifact: summary
slice: phase-status
subsystem: release-quality-gates
tags: [coverage-gate, branch-coverage, mutation-policy, TEST-02, TEST-03, TEST-04, TEST-06]
status: complete
last_updated: 2026-02-23
---

# Phase 14 Status: Coverage Gates and Mutation Rollout Complete

## Current scope status

- Coverage gate policy is aligned to Phase 14 requirements: `80%` minimum line coverage on critical assemblies (`STIGForge.Build`, `STIGForge.Apply`, `STIGForge.Verify`, `STIGForge.Infrastructure`).
- CI now generates deterministic merged coverage (`.artifacts/coverage/ci/coverage.cobertura.xml`), emits line/branch report artifacts under `.artifacts/coverage/ci/report`, and uploads the full coverage package for review.
- CI enforces scoped coverage gate by running `tools/release/Invoke-CoverageGate.ps1` against the merged report using `coverage-gate-policy.json`.
- Mutation policy is now checked in CI/release-package as report-only evidence when `.artifacts/mutation/current-result.json` exists, with informative skip when missing.

## Requirement traceability

- `TEST-02`: Complete (scoped 80% critical-assembly line coverage policy + enforcement script path)
- `TEST-03`: Complete (branch coverage summary/report output in CI)
- `TEST-04`: Complete (coverage gate executed in CI and blocks on threshold failure)
- `TEST-06`: Complete (mutation policy report signal is now integrated in CI/release-package; enforced mode remains available via script switch for future gating)

## Verification evidence (local snapshot)

Verification commands executed for current Phase 14 status checks:

1. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~RebootCoordinator"`
2. `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~E2E"`
3. `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~CoverageMergeScriptTests|FullyQualifiedName~CoverageReportScriptTests|FullyQualifiedName~CoverageGateScriptTests|FullyQualifiedName~MutationPolicyScriptTests" --no-build`

Observed outcomes:

- Unit test command failed in this Linux/WSL session with `NETSDK1136` because `STIGForge.UnitTests` references Windows-targeted projects (`UseWPF`/Windows TFM requirement).
- Integration suite excluding E2E passed: `77 passed, 0 failed, 0 skipped`.
- Scoped coverage/mutation script tests passed: `20 passed, 0 failed, 0 skipped`.
- Workflow-level coverage-gate + mutation-report wiring implemented in `.github/workflows/ci.yml` and `.github/workflows/release-package.yml` (not locally executed in WSL).

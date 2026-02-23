---
phase: 14-test-coverage-expansion
artifact: summary
slice: phase-status
subsystem: release-quality-gates
tags: [coverage-gate, branch-coverage, mutation-policy, TEST-02, TEST-03, TEST-04, TEST-06]
status: in-progress
last_updated: 2026-02-23
---

# Phase 14 Status: Coverage Gates Complete, Mutation Rollout In Progress

## Current scope status

- Coverage gate policy is aligned to Phase 14 requirements: `80%` minimum line coverage on critical assemblies (`STIGForge.Build`, `STIGForge.Apply`, `STIGForge.Verify`, `STIGForge.Infrastructure`).
- Coverage reporting pipeline includes branch coverage metrics and deterministic artifact selection in CI.
- CI enforces scoped coverage gate by running `tools/release/Invoke-CoverageGate.ps1` against generated Cobertura artifacts.
- Mutation policy script is integrated with report mode default and optional enforcement mode, including a seeded-fallback guard for enforcement safety.

## Requirement traceability

- `TEST-02`: Complete (scoped 80% critical-assembly line coverage policy + enforcement script path)
- `TEST-03`: Complete (branch coverage summary/report output in CI)
- `TEST-04`: Complete (coverage gate executed in CI and blocks on threshold failure)
- `TEST-06`: In progress (policy/evaluation plumbing implemented; full mutation execution rollout remains pending)

## Verification evidence (local snapshot)

Verification commands executed for current Phase 14 status checks:

1. `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~RebootCoordinator"`
2. `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~E2E"`
3. `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CoverageGateScriptTests|FullyQualifiedName~CoverageReportScriptTests|FullyQualifiedName~MutationPolicyScriptTests"`

Observed outcomes:

- Unit test command failed in this Linux/WSL session with `NETSDK1136` because `STIGForge.UnitTests` references Windows-targeted projects (`UseWPF`/Windows TFM requirement).
- Integration suite excluding E2E passed: `77 passed, 0 failed, 0 skipped`.
- Scoped coverage/mutation script tests passed: `17 passed, 0 failed, 0 skipped`.

# Stability Budget

This document defines deterministic stability signals required for release readiness.

## Budget policy

- CI long-run suite budget:
  - Scope: `E2E` + `SnapshotIntegrationTests`
  - Attempts per run: `3`
  - Required passes: `2`
  - Allowed failures: `1`
- VM smoke matrix budget:
  - Scope: `E2E`
  - Attempts per runner: `1`
  - Required passes: `1`
  - Allowed failures: `0`
- Re-run policy: retries are in-job and deterministic (fixed attempt count, no ad-hoc reruns).

## Required artifacts

- CI: `.artifacts/stability-budget/ci/stability-budget-summary.json`
- CI: `.artifacts/stability-budget/ci/stability-budget-report.md`
- VM matrix: `.artifacts/stability-budget/<runner>/stability-budget-summary.json`
- VM matrix: `.artifacts/stability-budget/<runner>/stability-budget-report.md`

Each summary must include policy settings, per-attempt outcomes, and final pass/fail decision.

## Command scope

- CI command:
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --nologo --filter "FullyQualifiedName~E2E|FullyQualifiedName~SnapshotIntegrationTests"`
- VM smoke command:
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --nologo --filter "FullyQualifiedName~E2E"`

## Release readiness interpretation

- Stability budget pass: release candidate remains eligible for promotion.
- Stability budget fail: release candidate is blocked until deterministic stability returns.
- Stability warnings are captured in artifacts and must be reviewed before promotion.

## Promotion enforcement

- CI workflow (`.github/workflows/ci.yml`) validates both stability summary and report artifacts after budget execution.
- VM workflow (`.github/workflows/vm-smoke-matrix.yml`) validates per-runner stability summary/report artifacts before upload.
- Release decision flow must treat `stability-budget-summary.json` as required machine-readable evidence, not optional log output.

## Related quality gates (Phase 14)

- Coverage gate policy (`tools/release/coverage-gate-policy.json`) enforces `80%` line coverage for critical assemblies:
  - `STIGForge.Build`
  - `STIGForge.Apply`
  - `STIGForge.Verify`
  - `STIGForge.Infrastructure`
- Coverage report step publishes branch coverage summary artifacts under `.artifacts/test-coverage/ci`.
- Mutation policy step (`tools/release/Invoke-MutationPolicy.ps1`) is active in report mode by default and supports optional enforcement mode.
- Mutation policy currently evaluates mutation score artifacts; deterministic mutation execution generation is tracked separately.

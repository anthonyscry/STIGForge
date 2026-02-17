---
phase: 10-quality-and-release-signal-hardening
plan: 01
status: completed
completed_at: 2026-02-09
commits: []
requirements-completed: [QA-01, QA-02, QA-03]
---

# Plan 01 Summary

Completed deterministic quality-gate hardening for parity-critical v1.1 workflows by promoting mission-summary parity contracts into CI, release-gate evidence, and promotion workflow checks.

## What Was Built

- Expanded parity-critical mission summary coverage in `tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs`:
  - Added legacy manifest metadata fallback coverage.
  - Added informational/warning status normalization coverage for recoverable warning semantics.
- Updated `tools/release/Invoke-ReleaseGate.ps1` to add a new `upgrade-rebase-parity-contract` step:
  - Runs `BundleMissionSummaryServiceTests` as part of upgrade/rebase evidence.
  - Includes parity contract in required evidence arrays and release-gate report/summary outputs.
- Updated workflow enforcement to fail closed when parity evidence is missing or failed:
  - `.github/workflows/ci.yml` now runs explicit parity contracts and validates `upgrade-rebase-parity-contract` in summary output.
  - `.github/workflows/release-package.yml` now verifies `upgrade-rebase-parity-contract` exists and succeeded.
  - `.github/workflows/vm-smoke-matrix.yml` now verifies `upgrade-rebase-parity-contract` exists and succeeded per VM runner.
- Updated release documentation for the expanded evidence model:
  - `docs/release/UpgradeAndRebaseValidation.md`
  - `docs/release/ReleaseCandidatePlaybook.md`
  - `docs/release/ShipReadinessChecklist.md`

## Verification

- Passed:
  - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BundleMissionSummaryServiceTests|FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests"`
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"`
  - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0`
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0`
  - `dotnet build STIGForge.sln --configuration Release -p:EnableWindowsTargeting=true`
- LSP diagnostics clean:
  - `tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs`

## Notes

- CI/release workflow checks now enforce parity-contract evidence presence the same way upgrade/rebase status was previously enforced.
- Trendable stability/compatibility artifacts remain in existing deterministic roots (`.artifacts/stability-budget/*`, `.artifacts/release-gate/*`, `.artifacts/release-gate/*/quarterly-pack/*`) and are now directly connected to parity evidence gating.

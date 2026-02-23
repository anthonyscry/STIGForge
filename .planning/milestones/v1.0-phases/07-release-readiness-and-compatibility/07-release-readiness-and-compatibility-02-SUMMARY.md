---
phase: 07-release-readiness-and-compatibility
plan: 02
status: completed
completed_at: 2026-02-08
commits: []
---

# Plan 02 Summary

Implemented stability budget policy and workflow-level gating for long-run and VM smoke signals.

## What Was Built

- Added explicit stability budget policy and artifact requirements in `docs/testing/StabilityBudget.md`.
- Hardened deterministic test behavior in:
  - `tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs` (fixed deterministic verify timestamps)
  - `tests/STIGForge.IntegrationTests/Apply/SnapshotIntegrationTests.cs` (fixed deterministic snapshot timestamp in rollback script generation test)
  - `tests/STIGForge.UnitTests/SmokeTests.cs` (locked deterministic export-root naming assertion)
- Wired CI stability budget enforcement in `.github/workflows/ci.yml`:
  - Multi-attempt stability evaluation (`E2E|SnapshotIntegrationTests`)
  - Machine-readable summary/report artifacts
  - Hard fail when policy thresholds are not met
- Wired VM smoke stability budget enforcement in `.github/workflows/vm-smoke-matrix.yml`:
  - Per-runner E2E stability summary/report artifacts
  - Budget pass/fail signal included with release-gate artifacts

## Verification

- Passed:
  - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~E2E|FullyQualifiedName~SnapshotIntegrationTests"`
  - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~SmokeTests"`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File ./tools/release/Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot ./.artifacts/release-gate/phase07-quarterly-resume-5`

## Notes

- Stability budget policy and workflow gating are implemented.
- Gate artifacts now include stability-budget and quarterly compatibility evidence for promotion review.

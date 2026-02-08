# Upgrade and Rebase Validation

This document defines required compatibility evidence for release promotion when moving from one STIG content baseline to another.

## Validation scope

Release evidence must show:

1. Baseline-to-target diff behavior remains deterministic.
2. Overlay rebase behavior is stable and review semantics are preserved.
3. CLI diff/rebase integration flows continue to work end-to-end.
4. Rollback safety guardrails and operator-decision boundaries are enforced.

## Automated gate contracts

`tools/release/Invoke-ReleaseGate.ps1` runs explicit upgrade/rebase contracts and writes evidence:

- `upgrade-rebase-diff-contract`
  - `tests/STIGForge.UnitTests/Services/BaselineDiffServiceTests.cs`
- `upgrade-rebase-overlay-contract`
  - `tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs`
- `upgrade-rebase-cli-contract`
  - `tests/STIGForge.IntegrationTests/Cli/CliCommandTests.cs` (`DiffPacks*`, `RebaseOverlay*`)
- `upgrade-rebase-rollback-safety`
  - `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs`

Promotion gate: every contract above must pass.

## Local validation commands

Run locally for pre-release verification:

```powershell
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~BaselineDiffServiceTests|FullyQualifiedName~OverlayRebaseServiceTests"

dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay"

dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~ApplyRunnerTests"
```

## Required release artifacts

Release gate output root must contain:

- `upgrade-rebase/upgrade-rebase-summary.json`
  - `status` must be `passed`
  - `requiredEvidence` must list all four contract areas
- `upgrade-rebase/upgrade-rebase-report.md`
  - Must include per-step pass/fail table and log references

Workflow enforcement:

- `release-package.yml` fails if upgrade/rebase summary is missing or not `passed`.
- `vm-smoke-matrix.yml` fails if upgrade/rebase summary is missing or not `passed` for any runner.

## Data retention and rollback expectations

For go/no-go review, confirm:

- Rebase outputs preserve expected overlay overrides or flag review-required actions.
- No unintended data loss occurs for `.stigforge` overlays, profiles, or content pack metadata.
- Rollback-related tests validate fail-closed behavior and explicit operator decision points.

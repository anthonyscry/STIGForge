---
phase: 05-proof-packaging-fleet-lite-and-integrity
plan: 02
status: complete
duration: ~8 min
---

# Plan 05-02 Summary: Fleet Artifact Collection and Summary

## What was built
Added fleet artifact collection via WinRM, per-host CKL generation, unified fleet summary with control status matrix, fleet-wide compliance calculation, and fleet POA&M with host attribution.

## Key files

### Created
- `src/STIGForge.Export/FleetSummaryService.cs` -- fleet summary aggregation, control status matrix, per-host CKL, fleet POA&M
- `src/STIGForge.Cli/Commands/FleetCommands.cs` -- fleet-collect and fleet-summary CLI commands (extended existing)
- `tests/STIGForge.UnitTests/Export/FleetSummaryServiceTests.cs` -- 6 unit tests
- `tests/STIGForge.UnitTests/Infrastructure/FleetArtifactCollectionTests.cs` -- 4 unit tests

### Modified
- `src/STIGForge.Infrastructure/System/FleetService.cs` -- added CollectArtifactsAsync, removed GeneratePerHostCkl (layer violation fix)
- `src/STIGForge.Export/PoamGenerator.cs` -- added HostsAffected to PoamItem for fleet attribution
- `src/STIGForge.Cli/Program.cs` -- registered fleet-collect and fleet-summary commands

## Decisions
- GeneratePerHostCkl moved from FleetService (Infrastructure) to FleetSummaryService (Export) to fix layer violation
- Fleet-wide compliance calculated as weighted average: totalPass / totalApplicable * 100
- Control status matrix uses SortedDictionary for deterministic output
- Status normalization maps Open->Fail, NotAFinding->Pass, Not_Applicable->NA, etc.

## Self-Check: PASSED
- [x] CollectArtifactsAsync in FleetService
- [x] Per-host CKL generation
- [x] Fleet summary with control status matrix
- [x] Fleet POA&M with host attribution
- [x] CLI commands functional
- [x] 10/10 unit tests passing

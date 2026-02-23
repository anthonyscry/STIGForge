---
phase: 01-mission-orchestration-and-apply-evidence
plan: 02
subsystem: import
tags: [content-import, staging, deterministic, wpf, cli, import-queue, planned-import]

# Dependency graph
requires: []
provides:
  - "ImportOperationState enum (Detected/Planned/Staged/Committed/Failed) for import lifecycle"
  - "PlannedContentImport.State and FailureReason properties for observable staged transitions"
  - "ContentPackImporter.ExecutePlannedImportAsync for staged execution with state mutation"
  - "StagedOperationOutcome model for per-operation diagnostic rows in summary artifacts"
  - "ImportScanSummary.StagedOutcomes, StagedCommittedCount, StagedFailedCount fields"
  - "CLI import-pack command with [Planned]/[Committed]/[Failed] staged output"
  - "WPF ScanImportFolderAsync using ExecutePlannedImportAsync for observable staging"
affects:
  - "02-policy-scope-and-safety-gates"
  - "03-build-apply-verify-mission-loop"
  - "import-inbox-scanner"
  - "import-dedup-service"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ImportOperationState: explicit planning-level staged lifecycle (Detected->Planned->Staged->Committed/Failed)"
    - "ExecutePlannedImportAsync: accepts PlannedContentImport, mutates State through lifecycle, re-throws on failure"
    - "StagedOperationOutcome: deterministic per-operation row emitted regardless of success or failure"
    - "ImportScanSummary staged fields: counts and outcome rows serialized to both JSON and text summary artifacts"

key-files:
  created: []
  modified:
    - src/STIGForge.Content/Import/ImportQueuePlanner.cs
    - src/STIGForge.Content/Import/ContentPackImporter.cs
    - src/STIGForge.Content/Import/ImportScanSummary.cs
    - src/STIGForge.Cli/Commands/ImportCommands.cs
    - src/STIGForge.App/MainViewModel.Import.cs
    - tests/STIGForge.UnitTests/Content/ImportQueuePlannerTests.cs
    - tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs

key-decisions:
  - "ImportOperationState is a planner-level lifecycle distinct from ImportStage (execution-level checkpoint stages); both coexist"
  - "ExecutePlannedImportAsync mutates the PlannedContentImport in-place rather than returning a result wrapper to keep existing API surface intact"
  - "FailureReason populated from exception message on state transition to Failed; the exception is re-thrown to preserve call-site error handling"
  - "StagedOutcomes emitted even for failures, providing deterministic summary rows regardless of import outcome"

patterns-established:
  - "Staged lifecycle pattern: operations are emitted as Planned, transition to Staged at execution start, then to Committed or Failed at completion"
  - "Observable import pattern: PlannedContentImport carries mutable State and FailureReason for surfacing lifecycle to CLI, WPF, and summary artifacts"
  - "Deterministic summary rows pattern: per-operation outcome rows written regardless of success/failure using StagedOperationOutcome"

requirements-completed:
  - IMPT-01

# Metrics
duration: 5min
completed: 2026-02-22
---

# Phase 1 Plan 02: Import Staging Transitions Summary

**Deterministic staged import lifecycle (Planned/Staged/Committed/Failed) wired across planner, executor, CLI command output, and WPF scan summary artifacts via ImportOperationState and ExecutePlannedImportAsync**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-02-22T06:01:28Z
- **Completed:** 2026-02-22T06:06:24Z
- **Tasks:** 3
- **Files modified:** 7

## Accomplishments

- Added `ImportOperationState` enum to `ImportQueuePlanner.cs` with `Detected/Planned/Staged/Committed/Failed` states; all planned operations now emit `Planned` state with deterministic ordering preserved
- Added `ExecutePlannedImportAsync` to `ContentPackImporter` that mutates `PlannedContentImport.State` through `Staged->Committed` or `Staged->Failed` with `FailureReason` captured
- Updated CLI `import-pack` command to show `[Planned]`/`[Committed]`/`[Failed]` state transitions per operation in console and structured log entries
- Added `StagedOperationOutcome` model and staged fields to `ImportScanSummary`; WPF scan now records deterministic per-operation rows and staged counts in JSON and text artifacts
- 15 new tests added across `ImportQueuePlannerTests` and `ContentPackImporterTests` covering staged state emissions, lifecycle transitions, null failure reasons, and summary field contracts

## Task Commits

1. **Task 1: Extend import planning with explicit staging transitions** - `51f8521` (feat)
2. **Task 2: Wire staging-before-commit behavior in import execution paths** - `bd79127` (feat)
3. **Task 3: Surface staged transitions in WPF import workflow** - `0647df8` (feat)

**Plan metadata:** _(docs commit follows)_

## Files Created/Modified

- `src/STIGForge.Content/Import/ImportQueuePlanner.cs` - Added `ImportOperationState` enum and `State`/`FailureReason` properties to `PlannedContentImport`; `ToOperation()` always sets `State = Planned`
- `src/STIGForge.Content/Import/ContentPackImporter.cs` - Added `ExecutePlannedImportAsync` with `Planned->Staged->Committed/Failed` lifecycle transitions
- `src/STIGForge.Content/Import/ImportScanSummary.cs` - Added `StagedOperationOutcome` model and `StagedOutcomes`/`StagedCommittedCount`/`StagedFailedCount` to `ImportScanSummary`
- `src/STIGForge.Cli/Commands/ImportCommands.cs` - Updated `import-pack` command to use `ExecutePlannedImportAsync` and emit `[State]` output
- `src/STIGForge.App/MainViewModel.Import.cs` - Updated `ScanImportFolderAsync` to use `ExecutePlannedImportAsync`, capture `StagedOperationOutcome` rows, and surface staged counts in `StatusText` and persisted summary artifacts
- `tests/STIGForge.UnitTests/Content/ImportQueuePlannerTests.cs` - 4 new tests: state emission, null failure reason, dual-route state, ordering stability with state
- `tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs` - 7 new tests: ExecutePlannedImportAsync success/failure/null, ImportScanSummary defaults, StagedOperationOutcome contracts

## Decisions Made

- `ImportOperationState` is a planning-level lifecycle distinct from `ImportStage` (execution-level crash-recovery checkpoints); both coexist without coupling
- `ExecutePlannedImportAsync` mutates `PlannedContentImport` in-place to avoid wrapping existing return types while making staged transitions observable
- `FailureReason` populated from exception message and exception is re-thrown to preserve all call-site error handling
- `StagedOutcomes` rows always emitted for all planned operations to ensure deterministic summary output regardless of import outcome

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed namespace masking build error in MissionRunRepository.cs**
- **Found during:** Pre-execution build verification
- **Issue:** `STIGForge.Infrastructure.System` namespace masked `System.Globalization` in `MissionRunRepository.cs`, causing 3 compiler errors (`CS0234`). The file already had `global::` qualifiers on lines 183-184; the issue was a stale cache file causing MSBuild to rerun the broken previous state
- **Fix:** Removed stale `STIGForge.Shared.AssemblyInfoInputs.cache` file to allow clean regeneration; confirmed `global::System.Globalization` qualifiers already present in the file were sufficient
- **Files modified:** `/mnt/c/projects/STIGForge/src/STIGForge.Shared/obj/Debug/net8.0/` (cache removal only, no source changes needed)
- **Verification:** `dotnet build` succeeded with 0 errors after cache removal
- **Committed in:** Not committed separately (infrastructure file, not source change)

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug, pre-existing build issue)
**Impact on plan:** The fix was necessary to build and verify the plan's work. No scope creep; the source file already had the correct qualifiers.

## Issues Encountered

- MSBuild stale cache issue (`STIGForge.Shared.AssemblyInfoInputs.cache`) caused `System.Globalization` namespace not to resolve on first build. Resolved by removing the cache file; no source changes required.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Staged import lifecycle is fully wired across planner, executor, CLI, and WPF
- `ExecutePlannedImportAsync` is the preferred entry point for all planned content imports
- `StagedOperationOutcome` contract is ready for consumption by any phase that needs per-operation import diagnostics
- Phase 2 (policy/scope/safety gates) can use `PlannedContentImport.State` to gate staged imports through safety checks before allowing them to reach `Committed`

---
*Phase: 01-mission-orchestration-and-apply-evidence*
*Completed: 2026-02-22*

---
phase: 01-mission-orchestration-and-apply-evidence
plan: 03
subsystem: orchestration
tags: [mission-run, timeline, evidence, apply, rerun, sha256, continuity, csharp, dotnet]

# Dependency graph
requires:
  - phase: 01-01
    provides: MissionRun/MissionTimelineEvent types and IMissionRunRepository with append-only AppendEventAsync
provides:
  - Timeline-aware orchestration: BundleOrchestrator emits Started/Finished/Failed/Skipped events for Apply/Verify/Evidence phases
  - Run-scoped apply evidence: EvidenceCollector WriteEvidence accepts RunId/StepName/SupersedesEvidenceId provenance fields
  - Apply step evidence metadata: ApplyRunner writes per-step evidence with SHA-256, RunId, StepName, and ContinuityMarker
  - Rerun continuity: apply_run.json records runId/priorRunId; step deduplication via SHA-256 comparison sets retained/superseded markers
  - EvidenceId in WriteResult for downstream lineage reference
affects:
  - 01-04 (import staging can now emit mission timeline events if needed)
  - proof/export phases (evidence metadata with RunId/StepName/SupersedesEvidenceId ready for downstream consumption)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Non-blocking timeline emission: AppendEventAsync/UpdateRunStatusAsync failures emit Trace warnings and never block mission execution"
    - "Optional dependency injection: IMissionRunRepository and EvidenceCollector are optional constructor params (null-safe degraded behavior)"
    - "Rerun lineage: apply_run.json records runId+priorRunId; LoadPriorRunStepSha256 reads prior run data by run ID match"
    - "Continuity markers: 'retained' when SHA-256 matches prior step artifact, 'superseded' when different, null when no comparison available"
    - "Evidence EvidenceId: baseName of written evidence file exposed in EvidenceWriteResult for cross-run lineage references"

key-files:
  created:
    - tests/STIGForge.UnitTests/Build/BundleOrchestratorTimelineTests.cs
  modified:
    - src/STIGForge.Build/BundleOrchestrator.cs
    - src/STIGForge.Apply/ApplyRunner.cs
    - src/STIGForge.Apply/ApplyModels.cs
    - src/STIGForge.Apply/STIGForge.Apply.csproj
    - src/STIGForge.Evidence/EvidenceCollector.cs
    - src/STIGForge.Evidence/EvidenceModels.cs
    - tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs
    - tests/STIGForge.UnitTests/Evidence/EvidenceCollectorTests.cs

key-decisions:
  - "Timeline emission is non-blocking: repository failures emit Trace warnings rather than aborting orchestration"
  - "EvidenceCollector is an optional ApplyRunner dependency to preserve existing DI wiring without requiring all callers to provide it"
  - "Rerun SHA-256 deduplication is bounded to apply_run.json only; broader history tracking is done via the mission ledger (IMissionRunRepository)"
  - "apply_run.json format extended with runId/priorRunId and per-step ArtifactSha256/ContinuityMarker for complete rerun lineage"

patterns-established:
  - "Apply step evidence is append-only: each run writes new evidence files; prior run files are never mutated"
  - "Orchestrator creates a MissionRun at entry and updates status at exit (completed) or at each failure point (failed)"

requirements-completed: [FLOW-01, FLOW-02, APLY-01, APLY-02]

# Metrics
duration: 15min
completed: 2026-02-22
---

# Phase 01 Plan 03: Mission Orchestration and Apply Evidence Summary

**Deterministic timeline events emitted per orchestration phase boundary with run-scoped apply evidence (SHA-256, RunId, StepName, ContinuityMarker) and rerun lineage markers via append-only apply_run.json**

## Performance

- **Duration:** 15 min
- **Started:** 2026-02-22T06:10:31Z
- **Completed:** 2026-02-22T06:25:00Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments

- `BundleOrchestrator.OrchestrateAsync` creates a `MissionRun` and emits deterministic `MissionTimelineEvent` records (Started/Finished/Failed/Skipped) for Apply, Verify (evaluate_stig, scap), and Evidence (coverage_artifacts) phases via the optional `IMissionRunRepository`
- `EvidenceCollector.WriteEvidence` now accepts `RunId`, `StepName`, and `SupersedesEvidenceId` provenance fields; persists them in metadata JSON; returns `EvidenceId` in `EvidenceWriteResult` for cross-run lineage
- `ApplyRunner` writes per-step evidence metadata with SHA-256, run/step identity, and continuity markers (`retained`/`superseded`); `apply_run.json` now includes `runId`, `priorRunId`, and per-step `ArtifactSha256`/`ContinuityMarker` for deterministic rerun history

## Task Commits

Each task was committed atomically:

1. **Task 1: Emit orchestration phase timeline events into MissionRun ledger** - `7bccede` (feat)
2. **Task 2+3: Capture apply step evidence with run-scoped provenance, checksums, and rerun continuity markers** - `6511707` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/STIGForge.Build/BundleOrchestrator.cs` - Added `IMissionRunRepository` optional param; `OrchestrateAsync` creates run, appends timeline events per phase, updates run status on completion/failure
- `src/STIGForge.Apply/ApplyRunner.cs` - Added optional `EvidenceCollector` param; `WriteStepEvidence` writes evidence per step; `LoadPriorRunStepSha256` reads prior run for deduplication; `apply_run.json` extended with provenance fields
- `src/STIGForge.Apply/ApplyModels.cs` - `ApplyRequest`: `RunId`, `PriorRunId`; `ApplyStepOutcome`: `EvidenceMetadataPath`, `ArtifactSha256`, `ContinuityMarker`; `ApplyResult`: `RunId`, `PriorRunId`
- `src/STIGForge.Apply/STIGForge.Apply.csproj` - Added `STIGForge.Evidence` project reference
- `src/STIGForge.Evidence/EvidenceModels.cs` - `EvidenceWriteRequest`: `RunId`, `StepName`, `SupersedesEvidenceId`; `EvidenceMetadata`: same fields; `EvidenceWriteResult`: `EvidenceId`
- `src/STIGForge.Evidence/EvidenceCollector.cs` - Propagates new provenance fields to metadata; sets `EvidenceId` in result
- `tests/STIGForge.UnitTests/Build/BundleOrchestratorTimelineTests.cs` - 7 tests: apply events, skipped verify events, deterministic seq, shared run ID, run completed status, failure events, no-repo degraded operation
- `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs` - 12 tests (4 original preserved + 8 new): evidence provenance, run ID propagation, apply_run.json schema, rerun priorRunId, append-only second run
- `tests/STIGForge.UnitTests/Evidence/EvidenceCollectorTests.cs` - 10 tests (5 original preserved + 5 new): RunId persistence, SupersedesEvidenceId, EvidenceId in result, SHA-256 match, null fields for manual evidence

## Decisions Made

- Timeline emission is non-blocking: repository failures emit `Trace.TraceWarning` to avoid aborting compliance-critical mission flows
- `EvidenceCollector` is an optional `ApplyRunner` constructor parameter (existing callers don't need to change)
- SHA-256 deduplication uses `apply_run.json` for prior run step comparison; the mission ledger (`IMissionRunRepository`) provides the authoritative timeline history
- `apply_run.json` extended (not replaced) with new provenance fields to preserve backward compatibility

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added STIGForge.Evidence project reference to STIGForge.Apply.csproj**
- **Found during:** Task 2 (ApplyRunner evidence integration)
- **Issue:** ApplyRunner needed to use EvidenceCollector but the Apply project had no reference to the Evidence project
- **Fix:** Added `<ProjectReference Include="..\STIGForge.Evidence\STIGForge.Evidence.csproj" />` to STIGForge.Apply.csproj
- **Files modified:** src/STIGForge.Apply/STIGForge.Apply.csproj
- **Verification:** `dotnet build src/STIGForge.Apply/STIGForge.Apply.csproj` succeeds with 0 errors
- **Committed in:** 6511707 (Task 2+3 commit)

---

**Total deviations:** 1 auto-fixed (1 missing project reference)
**Impact on plan:** Required for compilation. No scope changes.

## Issues Encountered

None - all three plan tasks delivered cleanly.

## Next Phase Readiness

- Orchestration timeline emission is fully wired; the mission ledger is populated by `OrchestrateAsync` for each run
- Apply evidence has deterministic run/step provenance and SHA-256 for downstream proof/export phases
- Rerun lineage (`retained`/`superseded` markers and `priorRunId`) is ready for APLY-02 consumption in export phases
- Pre-existing `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` test failure remains out of scope (pre-existing, tracked in STATE.md)

---
*Phase: 01-mission-orchestration-and-apply-evidence*
*Completed: 2026-02-22*

## Self-Check: PASSED

All files verified present:
- FOUND: src/STIGForge.Build/BundleOrchestrator.cs
- FOUND: src/STIGForge.Apply/ApplyRunner.cs
- FOUND: src/STIGForge.Apply/ApplyModels.cs
- FOUND: src/STIGForge.Apply/STIGForge.Apply.csproj
- FOUND: src/STIGForge.Evidence/EvidenceCollector.cs
- FOUND: src/STIGForge.Evidence/EvidenceModels.cs
- FOUND: tests/STIGForge.UnitTests/Build/BundleOrchestratorTimelineTests.cs
- FOUND: tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs
- FOUND: tests/STIGForge.UnitTests/Evidence/EvidenceCollectorTests.cs
- FOUND: .planning/phases/01-mission-orchestration-and-apply-evidence/01-03-SUMMARY.md

All commits verified:
- FOUND: 7bccede (feat: timeline events)
- FOUND: 6511707 (feat: evidence provenance + rerun continuity)

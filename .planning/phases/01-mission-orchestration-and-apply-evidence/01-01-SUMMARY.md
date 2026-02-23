---
phase: 01-mission-orchestration-and-apply-evidence
plan: 01
subsystem: database
tags: [sqlite, dapper, mission-run, timeline, append-only, di, csharp]

# Dependency graph
requires: []
provides:
  - MissionRun and MissionTimelineEvent domain types with enums and evidence linkage
  - IMissionRunRepository interface with append-only AppendEventAsync and run/timeline queries
  - SQLite mission_runs and mission_timeline schema via DbBootstrap
  - MissionRunRepository implementation with duplicate-seq rejection and deterministic ordering
  - IMissionRunRepository registered in both CliHostFactory and App.xaml.cs DI
affects:
  - 01-02-mission-orchestration
  - 01-03-apply-evidence
  - 01-04-import-staging

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Append-only timeline ledger: MissionTimelineEvent records are immutable once written; runs may only have status updated via UpdateRunStatusAsync"
    - "Deterministic ordering: all GetTimelineAsync queries use ORDER BY seq ASC to guarantee reproducible results"
    - "Duplicate-seq guard: SQLITE_CONSTRAINT (error code 19) on the UNIQUE(run_id, seq) index is caught and converted to InvalidOperationException"
    - "Manual enum mapping: enums stored as TEXT strings in SQLite and parsed via Enum.Parse in private row-mapping methods"

key-files:
  created:
    - src/STIGForge.Core/Models/MissionRun.cs
    - src/STIGForge.Infrastructure/Storage/MissionRunRepository.cs
    - tests/STIGForge.UnitTests/Infrastructure/MissionRunRepositoryTests.cs
  modified:
    - src/STIGForge.Core/Abstractions/Services.cs
    - src/STIGForge.Infrastructure/Storage/DbBootstrap.cs
    - src/STIGForge.Cli/CliHostFactory.cs
    - src/STIGForge.App/App.xaml.cs

key-decisions:
  - "Store enum values as TEXT strings in SQLite for debuggability; parse via Enum.Parse in repository mapping layer"
  - "Use PRAGMA journal_mode=WAL in DbBootstrap schema creation to prevent lock contention under concurrent access"
  - "Restrict run mutations to status/finishedAt/detail only; all timeline events are append-only with no update path"
  - "Use Dapper plain-row projection DTOs (RunRow, EventRow) with explicit column aliases to avoid Dapper type handler issues with complex types"

patterns-established:
  - "Append-only mission ledger: sequence index + run ID pair enforced at the database layer via UNIQUE constraint"
  - "DI factory pattern: services registered as AddSingleton with factory lambdas reading the connection string singleton"

requirements-completed: [FLOW-01, FLOW-02, FLOW-03]

# Metrics
duration: 5min
completed: 2026-02-22
---

# Phase 01 Plan 01: Mission-Run Persistence Contract Summary

**Append-only SQLite mission-run ledger (MissionRun + MissionTimelineEvent) with IMissionRunRepository registered in CLI and WPF DI, 18 unit tests covering append semantics, ordering determinism, and duplicate-seq rejection**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-22T05:58:26Z
- **Completed:** 2026-02-22T06:04:00Z
- **Tasks:** 3
- **Files modified:** 7

## Accomplishments

- Canonical `MissionRun` and `MissionTimelineEvent` domain types with `MissionRunStatus`, `MissionEventStatus`, and `MissionPhase` enums and evidence path/SHA-256 linkage fields
- SQLite `mission_runs` and `mission_timeline` tables with WAL mode, run_id+seq uniqueness, and indexed lookup in `DbBootstrap`
- `MissionRunRepository` using Microsoft.Data.Sqlite + Dapper with transactional appends, duplicate-sequence rejection via SQLITE_CONSTRAINT guard, and deterministic `ORDER BY seq ASC` reads
- `IMissionRunRepository` registered in both `CliHostFactory` (CLI) and `App.xaml.cs` (WPF) via existing connection-string singleton pattern
- 18 tests covering: schema idempotency, run CRUD, status updates, ordering, duplicate-seq rejection, all event statuses round-trip, evidence reference fields, and CliHostFactory DI smoke test

## Task Commits

Each task was committed atomically:

1. **Task 1: Define canonical MissionRun and timeline contracts** - `bf654bc` (feat)
2. **Task 2: Implement SQLite mission-run ledger with append-only semantics** - `b9086c2` (feat)
3. **Task 3: Register mission-run repository in both host composition roots** - `9b2b5b5` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/STIGForge.Core/Models/MissionRun.cs` - MissionRun, MissionTimelineEvent, MissionRunStatus, MissionEventStatus, MissionPhase types
- `src/STIGForge.Core/Abstractions/Services.cs` - IMissionRunRepository interface added
- `src/STIGForge.Infrastructure/Storage/DbBootstrap.cs` - mission_runs and mission_timeline tables with WAL mode, unique constraints, and indexes
- `src/STIGForge.Infrastructure/Storage/MissionRunRepository.cs` - SQLite+Dapper implementation
- `src/STIGForge.Cli/CliHostFactory.cs` - IMissionRunRepository AddSingleton registration
- `src/STIGForge.App/App.xaml.cs` - IMissionRunRepository AddSingleton registration
- `tests/STIGForge.UnitTests/Infrastructure/MissionRunRepositoryTests.cs` - 18 unit tests

## Decisions Made

- Stored enum values as TEXT strings in SQLite for debuggability; parsed via `Enum.Parse` in repository mapping layer rather than using Dapper type handlers (avoids `global::System.Globalization` namespace conflict complexity)
- Enabled `PRAGMA journal_mode=WAL` at DbBootstrap time to prevent SQLite lock contention under concurrent CLI/WPF access
- Restricted run mutations to status/finishedAt/detail only — no update path for historical timeline events
- Used internal `RunRow` and `EventRow` projection DTOs with explicit column aliases to work cleanly with Dapper's default mapping behavior

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed System.Globalization namespace collision in MissionRunRepository**
- **Found during:** Task 2 (MissionRunRepository compilation)
- **Issue:** `System.Globalization.DateTimeStyles` reference conflicted with `STIGForge.Infrastructure.System` namespace imported via implicit usings
- **Fix:** Used `global::System.Globalization.DateTimeStyles` via a static readonly field `RoundtripStyle`
- **Files modified:** src/STIGForge.Infrastructure/Storage/MissionRunRepository.cs
- **Verification:** Infrastructure project builds with 0 errors
- **Committed in:** b9086c2 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug fix)
**Impact on plan:** Necessary for compilation. No scope changes.

## Issues Encountered

- `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` in pre-existing `CliHostFactoryTests` was already failing before this plan (verified via git stash). Confirmed out-of-scope pre-existing issue; not caused by this plan's changes.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Mission-run persistence contract is fully in place for use by orchestration, apply, and evidence phases
- Both CLI and WPF can resolve `IMissionRunRepository` at startup
- `AppendEventAsync` and `GetTimelineAsync` are ready to receive events from orchestration flow
- Pre-existing `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` test failure should be investigated separately (not a blocker for this phase's delivery)

---
*Phase: 01-mission-orchestration-and-apply-evidence*
*Completed: 2026-02-22*

## Self-Check: PASSED

All files verified present:
- FOUND: src/STIGForge.Core/Models/MissionRun.cs
- FOUND: src/STIGForge.Infrastructure/Storage/MissionRunRepository.cs
- FOUND: tests/STIGForge.UnitTests/Infrastructure/MissionRunRepositoryTests.cs
- FOUND: .planning/phases/01-mission-orchestration-and-apply-evidence/01-01-SUMMARY.md

All commits verified:
- FOUND: bf654bc (feat: MissionRun contracts)
- FOUND: b9086c2 (feat: SQLite ledger)
- FOUND: 9b2b5b5 (feat: DI registration + tests)
- FOUND: 9b111cb (docs: plan metadata)

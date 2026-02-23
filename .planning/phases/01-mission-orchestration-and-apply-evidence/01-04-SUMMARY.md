---
phase: 01-mission-orchestration-and-apply-evidence
plan: 04
subsystem: orchestration
tags: [mission-timeline, cli, wpf, mvvm, xaml, summary-service, timeline-projection, csharp, dotnet]

# Dependency graph
requires:
  - phase: 01-01
    provides: MissionRun/MissionTimelineEvent types, IMissionRunRepository with GetLatestRunAsync/GetTimelineAsync
  - phase: 01-03
    provides: BundleOrchestrator emitting timeline events; IMissionRunRepository wired into orchestration flow
provides:
  - CLI mission-timeline command: ordered timeline rows and JSON output keyed by run ID/sequence/event type
  - MissionTimelineSummary model: timeline projection with LastPhase/IsBlocked/NextAction derived from persisted events
  - IBundleMissionSummaryService.LoadTimelineSummaryAsync: timeline-aware summary enrichment via optional IMissionRunRepository
  - MainViewModel timeline state: TimelineEvents collection with MissionTimelineEventItem; refreshed after apply/verify/orchestrate
  - GuidedRunView timeline panel: compact Seq/Phase/Step/Status/Time columns with empty-state message
  - OrchestrateView timeline panel: separate grid rows for status/next-action header and event list
  - NullOrEmptyToCollapsedConverter: reusable WPF converter for empty-state visibility

affects:
  - proof/export phases (mission timeline state available for downstream readiness assessment)
  - dashboard (timeline next-action can be surfaced in mission summary)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Timeline projection as optional layer: LoadTimelineSummaryAsync returns null when repository not configured, enabling graceful degraded operation"
    - "Non-blocking fire-and-forget timeline refresh: RefreshTimelineAsync called with _ = prefix to avoid blocking UI operations"
    - "Dispatcher.Invoke for ObservableCollection updates from async timeline refresh (thread-safe WPF binding)"
    - "NullOrEmptyToCollapsedConverter pattern for empty-state XAML visibility without code-behind"

key-files:
  created:
    - src/STIGForge.App/Converters.cs
    - tests/STIGForge.UnitTests/Cli/BundleCommandsTimelineTests.cs
  modified:
    - src/STIGForge.Core/Abstractions/Services.cs
    - src/STIGForge.Core/Services/BundleMissionSummaryService.cs
    - src/STIGForge.Cli/Commands/BundleCommands.cs
    - src/STIGForge.App/MainViewModel.cs
    - src/STIGForge.App/MainViewModel.ApplyVerify.cs
    - src/STIGForge.App/MainViewModel.Dashboard.cs
    - src/STIGForge.App/App.xaml
    - src/STIGForge.App/Views/GuidedRunView.xaml
    - src/STIGForge.App/Views/OrchestrateView.xaml
    - tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs

key-decisions:
  - "BundleMissionSummaryService accepts IMissionRunRepository as optional constructor param (null = no timeline, graceful degradation)"
  - "MissionTimelineSummary is a projection model; IsBlocked is derived from presence of any Failed event in the run's timeline"
  - "mission-timeline CLI command is standalone (not a mode of bundle-summary) to keep single-responsibility and enable --run-id, --limit flags"
  - "RefreshTimelineAsync is fire-and-forget (not awaited) to avoid blocking the apply/verify/orchestrate completion path"
  - "NullOrEmptyToCollapsedConverter added to App.xaml resources for reuse across all views needing empty-state visibility"

patterns-established:
  - "Timeline panels use fixed Grid columns (Seq/Phase/Step/Status/Time) for consistent operator scanning across CLI and WPF"
  - "Timeline refresh is wired at three integration points: ApplyRunAsync, VerifyRunAsync, Orchestrate, and OnBundleRootChanged"

requirements-completed: [FLOW-03]

# Metrics
duration: 8min
completed: 2026-02-22
---

# Phase 01 Plan 04: Mission Timeline Visibility Summary

**Mission run timeline projected into CLI command (mission-timeline) and WPF GuidedRun/Orchestrate panels using the persisted IMissionRunRepository ledger with deterministic Seq ordering, IsBlocked detection, and operator next-action derivation**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-22T06:23:21Z
- **Completed:** 2026-02-22T06:31:10Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments

- `IBundleMissionSummaryService` extended with `LoadTimelineSummaryAsync` returning `MissionTimelineSummary` (LatestRun, Events ordered by Seq, LastPhase, IsBlocked, NextAction derived from run status); optional `IMissionRunRepository` parameter with graceful null degradation
- `mission-timeline` CLI command added (`--json`, `--run-id`, `--limit`) printing ordered timeline rows; JSON output contains run metadata + events array keyed by seq/phase/stepName/status/occurredAt
- `MainViewModel` wired with `TimelineEvents ObservableCollection<MissionTimelineEventItem>` and `RefreshTimelineAsync()` fire-and-forget refresh after apply/verify/orchestrate completions and on bundle selection changes
- Compact timeline panels added to both `GuidedRunView.xaml` and `OrchestrateView.xaml` with empty-state messaging and `NullOrEmptyToCollapsedConverter` for clean empty-state visibility

## Task Commits

Each task was committed atomically:

1. **Task 1: Add mission timeline projection to summary and CLI surfaces** - `b1321e2` (feat)
2. **Task 2: Wire timeline data into MainViewModel orchestration flow** - `c0c5270` (feat)
3. **Task 3: Add timeline UI sections to Guided and Orchestrate views** - `b8f52ed` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/STIGForge.Core/Abstractions/Services.cs` - Added `LoadTimelineSummaryAsync` to `IBundleMissionSummaryService`; added `MissionTimelineSummary` model with LatestRun/Events/LastPhase/IsBlocked/NextAction
- `src/STIGForge.Core/Services/BundleMissionSummaryService.cs` - Added optional `IMissionRunRepository` constructor param; implemented `LoadTimelineSummaryAsync` and `DeriveNextAction` with degraded-mode support
- `src/STIGForge.Cli/Commands/BundleCommands.cs` - Added `RegisterMissionTimeline` command (--json, --run-id, --limit); outputs tabular and JSON timeline rows
- `src/STIGForge.App/MainViewModel.cs` - Added timeline observable properties and `MissionTimelineEventItem` nested class
- `src/STIGForge.App/MainViewModel.ApplyVerify.cs` - Added `RefreshTimelineAsync()`; wired into ApplyRunAsync/VerifyRunAsync/Orchestrate
- `src/STIGForge.App/MainViewModel.Dashboard.cs` - Wired `RefreshTimelineAsync` on `OnBundleRootChanged`
- `src/STIGForge.App/Converters.cs` - `NullOrEmptyToCollapsedConverter` IValueConverter (null/empty → Collapsed)
- `src/STIGForge.App/App.xaml` - Registered `NullOrEmptyToCollapsedConverter` as `NullOrEmptyToCollapsed` StaticResource; added local namespace
- `src/STIGForge.App/Views/GuidedRunView.xaml` - Added Mission Timeline panel with ItemsControl bound to TimelineEvents; empty-state message with converter
- `src/STIGForge.App/Views/OrchestrateView.xaml` - Added Grid rows for timeline header (Status/NextAction) and event list with ItemsControl
- `tests/STIGForge.UnitTests/Cli/BundleCommandsTimelineTests.cs` - 6 tests: null repo, empty state, ordered events, IsBlocked, completed action, degraded timeline
- `tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs` - 3 new timeline tests added (null repo, ordered seq, IsBlocked)

## Decisions Made

- `BundleMissionSummaryService` accepts `IMissionRunRepository` as optional constructor parameter; returns null from `LoadTimelineSummaryAsync` when not configured, enabling graceful degradation without repository
- `MissionTimelineSummary.IsBlocked` derived from presence of any `MissionEventStatus.Failed` event — mirrors how the CLI should report blocking state without DB joins
- `mission-timeline` is a standalone CLI command (not a mode of `bundle-summary`) to keep single-responsibility and allow `--run-id` and `--limit` without polluting bundle-summary options
- `RefreshTimelineAsync` is fire-and-forget to avoid delaying apply/verify/orchestrate completion notification to the operator

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added NullOrEmptyToCollapsedConverter for XAML empty-state**
- **Found during:** Task 3 (GuidedRunView/OrchestrateView XAML panels)
- **Issue:** The timeline empty-state `TextBlock` required a string-to-visibility converter that did not exist in the project
- **Fix:** Created `Converters.cs` with `NullOrEmptyToCollapsedConverter : IValueConverter`; registered in `App.xaml` as `NullOrEmptyToCollapsed` StaticResource
- **Files modified:** src/STIGForge.App/Converters.cs, src/STIGForge.App/App.xaml
- **Verification:** `dotnet build src/STIGForge.App` succeeds with 0 errors; AppXamlContractTests pass
- **Committed in:** b8f52ed (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (1 missing critical infrastructure for XAML binding)
**Impact on plan:** Required for correct WPF binding behavior. No scope changes.

## Issues Encountered

None - all three plan tasks delivered cleanly.

## Next Phase Readiness

- Mission timeline is now visible and consistent across CLI and WPF using the same persisted `IMissionRunRepository` ledger
- `MissionTimelineSummary.IsBlocked` and `NextAction` provide deterministic operator guidance derived from timeline state
- Timeline projection is ready for downstream proof/export phases to consume mission run readiness data
- Pre-existing `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` test failure remains out of scope (tracked in STATE.md)

---
*Phase: 01-mission-orchestration-and-apply-evidence*
*Completed: 2026-02-22*

## Self-Check: PASSED

All files verified present:
- FOUND: src/STIGForge.Core/Abstractions/Services.cs
- FOUND: src/STIGForge.Core/Services/BundleMissionSummaryService.cs
- FOUND: src/STIGForge.Cli/Commands/BundleCommands.cs
- FOUND: src/STIGForge.App/MainViewModel.cs
- FOUND: src/STIGForge.App/MainViewModel.ApplyVerify.cs
- FOUND: src/STIGForge.App/Converters.cs
- FOUND: src/STIGForge.App/App.xaml
- FOUND: src/STIGForge.App/Views/GuidedRunView.xaml
- FOUND: src/STIGForge.App/Views/OrchestrateView.xaml
- FOUND: tests/STIGForge.UnitTests/Cli/BundleCommandsTimelineTests.cs
- FOUND: .planning/phases/01-mission-orchestration-and-apply-evidence/01-04-SUMMARY.md

All commits verified:
- FOUND: b1321e2 (feat(01-04): mission timeline projection in summary service and CLI command)
- FOUND: c0c5270 (feat(01-04): wire mission timeline into MainViewModel orchestration flow)
- FOUND: b8f52ed (feat(01-04): add timeline UI panels to GuidedRun and Orchestrate views)

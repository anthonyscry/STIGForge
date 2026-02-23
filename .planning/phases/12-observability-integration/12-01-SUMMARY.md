---
phase: 12-observability-integration
plan: 01
subsystem: telemetry
tags: [opentelemetry, activity, distributed-tracing, w3c]

# Dependency graph
requires:
  - phase: 11-foundation-and-test-stability
    provides: CorrelationIdEnricher and LoggingConfiguration infrastructure
provides:
  - MissionTracingService for ActivitySource-based mission span creation
  - TraceContext for serializable W3C trace context propagation
  - TraceFileListener for local JSON trace file output
  - ActivitySourceNames for centralized naming constants
affects: [BundleOrchestrator, ApplyRunner, PowerShell process execution]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ActivitySource with using pattern for span lifecycle"
    - "ActivityListener for offline trace collection"
    - "W3C trace context propagation across process boundaries"

key-files:
  created:
    - src/STIGForge.Infrastructure/Telemetry/ActivitySourceNames.cs
    - src/STIGForge.Infrastructure/Telemetry/TraceContext.cs
    - src/STIGForge.Infrastructure/Telemetry/MissionTracingService.cs
    - src/STIGForge.Infrastructure/Telemetry/TraceFileListener.cs
  modified:
    - src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs

key-decisions:
  - "No new NuGet packages - uses built-in System.Diagnostics for W3C-compatible distributed tracing"
  - "TraceFileListener writes to traces.json for offline analysis without requiring OTLP collector"
  - "LoggingConfiguration manages TraceFileListener lifecycle with InitializeTraceListener and Shutdown"

patterns-established:
  - "ActivityKind.Server for root mission spans, ActivityKind.Internal for phase spans"
  - "OpenTelemetry semantic conventions with dot-notation tags (bundle.root, mission.run_id)"
  - "Thread-safe file appending with lock for concurrent span completion"

requirements-completed: [OBSV-02]

# Metrics
duration: 4min
completed: 2026-02-23
---

# Phase 12 Plan 01: Mission Tracing Infrastructure Summary

**.NET 8 ActivitySource-based distributed tracing with W3C-compatible trace context and local JSON file output for offline analysis**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-23T01:39:58Z
- **Completed:** 2026-02-23T01:43:52Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments
- MissionTracingService creates Activity spans for mission lifecycle phases (build, apply, verify, prove)
- TraceContext captures current Activity for W3C trace context propagation to PowerShell scripts
- TraceFileListener writes spans to traces.json when activities complete
- No new NuGet packages required - uses built-in System.Diagnostics

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ActivitySourceNames and TraceContext models** - `4948339` (feat)
2. **Task 2: Create MissionTracingService** - `a6e4f51` (feat)
3. **Task 3: Create TraceFileListener for local trace output** - `3965c78` (feat)

**Plan metadata:** pending (docs: complete plan)

_Note: TDD tasks may have multiple commits (test -> feat -> refactor)_

## Files Created/Modified
- `src/STIGForge.Infrastructure/Telemetry/ActivitySourceNames.cs` - Centralized ActivitySource and span naming constants
- `src/STIGForge.Infrastructure/Telemetry/TraceContext.cs` - Serializable trace context for process boundary propagation
- `src/STIGForge.Infrastructure/Telemetry/MissionTracingService.cs` - ActivitySource-based mission span creation
- `src/STIGForge.Infrastructure/Telemetry/TraceFileListener.cs` - ActivityListener writing spans to traces.json
- `src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs` - Added InitializeTraceListener and Shutdown for listener lifecycle

## Decisions Made
- Used built-in System.Diagnostics.ActivitySource instead of OpenTelemetry SDK (no external dependencies)
- TraceFileListener filters to "STIGForge.*" sources only
- Static class LoggingConfiguration manages TraceFileListener singleton lifecycle

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None - all tasks completed without issues.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Telemetry infrastructure ready for BundleOrchestrator and ApplyRunner integration
- TraceFileListener ready to capture spans when InitializeTraceListener is called at host startup
- TraceContext ready for PowerShell environment variable propagation

## Self-Check: PASSED

All files verified:
- src/STIGForge.Infrastructure/Telemetry/ActivitySourceNames.cs: FOUND
- src/STIGForge.Infrastructure/Telemetry/TraceContext.cs: FOUND
- src/STIGForge.Infrastructure/Telemetry/MissionTracingService.cs: FOUND
- src/STIGForge.Infrastructure/Telemetry/TraceFileListener.cs: FOUND
- src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs: FOUND

All commits verified:
- 4948339: FOUND
- a6e4f51: FOUND
- 3965c78: FOUND

---
*Phase: 12-observability-integration*
*Completed: 2026-02-23*

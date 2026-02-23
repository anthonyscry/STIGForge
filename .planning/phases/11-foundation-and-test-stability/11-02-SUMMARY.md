---
phase: 11-foundation-and-test-stability
plan: 02
subsystem: infra
tags: [serilog, logging, observability, correlation, trace-context]

# Dependency graph
requires: []
provides:
  - CorrelationIdEnricher for W3C trace context correlation
  - LoggingConfiguration with environment-based log level control
  - Shared LoggingLevelSwitch for runtime log level changes
affects: [cli-host, wpf-host, api-logging]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Activity.Current for W3C trace context (OpenTelemetry compatible)"
    - "LoggingLevelSwitch with environment variable configuration"
    - "Serilog enrichers for structured logging properties"

key-files:
  created:
    - src/STIGForge.Infrastructure/Logging/CorrelationIdEnricher.cs
    - src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs
    - tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
    - tests/STIGForge.IntegrationTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
  modified: []

key-decisions:
  - "Use Activity.Current for correlation (W3C standard, OpenTelemetry compatible) vs custom correlation service"
  - "Environment variable STIGFORGE_LOG_LEVEL for log level configuration (works for CLI and WPF)"
  - "Fallback to generated CorrelationId when no Activity context exists"

patterns-established:
  - "Enricher pattern: Add correlation properties to every log event"
  - "Static configuration class with shared LevelSwitch for host wiring"

requirements-completed: [OBSV-01, OBSV-04]

# Metrics
duration: 7min
completed: 2026-02-23
---

# Phase 11 Plan 02: Serilog Correlation and Log Level Infrastructure Summary

**Serilog enricher for W3C trace context correlation and environment-based log level configuration via shared LoggingLevelSwitch**

## Performance

- **Duration:** 7 min
- **Started:** 2026-02-23T00:40:25Z
- **Completed:** 2026-02-23T00:47:48Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments
- CorrelationIdEnricher adds TraceId/SpanId from Activity.Current for distributed tracing
- Fallback CorrelationId generation when no Activity context exists
- LoggingConfiguration with shared LoggingLevelSwitch for runtime control
- Environment-based log level via STIGFORGE_LOG_LEVEL variable
- Unit tests verify both correlation scenarios (with/without Activity)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create CorrelationIdEnricher** - `ca8e63f` (feat)
2. **Task 2: Create LoggingConfiguration helper** - `aef4160` (feat)
3. **Task 3: Create unit tests for logging infrastructure** - `0e676bd` (test)

## Files Created/Modified
- `src/STIGForge.Infrastructure/Logging/CorrelationIdEnricher.cs` - Serilog enricher adding TraceId/SpanId from Activity.Current
- `src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs` - Static helper with shared LoggingLevelSwitch and environment configuration
- `tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs` - Unit tests for correlation enricher (Windows)
- `tests/STIGForge.IntegrationTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs` - Unit tests for correlation enricher (Linux/WSL)

## Decisions Made
- **Activity.Current for correlation:** Uses W3C trace context standard, compatible with OpenTelemetry, no custom correlation service needed
- **STIGFORGE_LOG_LEVEL environment variable:** Simple configuration that works for both CLI (set before run) and WPF (system environment)
- **Guid correlation fallback:** Ensures every log has some correlation identifier even without distributed tracing context

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed missing Serilog.Core using directive**
- **Found during:** Task 2 (LoggingConfiguration implementation)
- **Issue:** LoggingLevelSwitch requires Serilog.Core namespace, not just Serilog
- **Fix:** Changed `using Serilog;` to `using Serilog.Core;`
- **Files modified:** src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs
- **Verification:** Build succeeded with 0 errors
- **Committed in:** aef4160 (Task 2 commit)

**2. [Rule 3 - Blocking] Added Serilog.Parsing using for TextToken**
- **Found during:** Task 3 (Unit tests implementation)
- **Issue:** TextToken class requires Serilog.Parsing namespace
- **Fix:** Added `using Serilog.Parsing;` to test file
- **Files modified:** tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
- **Verification:** Tests compile and pass
- **Committed in:** 0e676bd (Task 3 commit)

**3. [Rule 3 - Blocking] Added tests to IntegrationTests for WSL execution**
- **Found during:** Task 3 (Test verification)
- **Issue:** UnitTests project targets net8.0-windows and cannot run in WSL
- **Fix:** Created duplicate test file in IntegrationTests project (targets net8.0)
- **Files modified:** tests/STIGForge.IntegrationTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
- **Verification:** Tests pass in IntegrationTests project
- **Committed in:** 0e676bd (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (1 bug, 2 blocking)
**Impact on plan:** All auto-fixes were minor namespace and test execution issues. No scope creep. Tests are now runnable in both Windows and WSL environments.

## Issues Encountered
- WSL cannot run Windows Desktop tests (net8.0-windows target framework) - resolved by adding tests to IntegrationTests project which targets plain net8.0

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Logging infrastructure ready for CLI and WPF host wiring
- Correlation enricher can be added to Serilog configuration in hosts
- Log level can be controlled via STIGFORGE_LOG_LEVEL environment variable

## Self-Check: PASSED

**Files verified:**
- FOUND: src/STIGForge.Infrastructure/Logging/CorrelationIdEnricher.cs
- FOUND: src/STIGForge.Infrastructure/Logging/LoggingConfiguration.cs
- FOUND: tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
- FOUND: tests/STIGForge.IntegrationTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs

**Commits verified:**
- FOUND: ca8e63f (Task 1: CorrelationIdEnricher)
- FOUND: aef4160 (Task 2: LoggingConfiguration)
- FOUND: 0e676bd (Task 3: Tests)

---
*Phase: 11-foundation-and-test-stability*
*Completed: 2026-02-23*

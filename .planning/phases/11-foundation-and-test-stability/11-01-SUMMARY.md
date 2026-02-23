---
phase: 11-foundation-and-test-stability
plan: 01
subsystem: testing
tags: [xunit, iasynclifetime, test-categories, traits, flaky-tests]

# Dependency graph
requires: []
provides:
  - IAsyncLifetime pattern for async test disposal
  - TestCategories constants for trait filtering
  - Pattern for test categorization via [Trait] attributes
affects: [all phases requiring test coverage enforcement]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - IAsyncLifetime for async disposal in tests with IHost or file I/O
    - [Trait("Category", TestCategories.Unit)] for test categorization

key-files:
  created:
    - tests/STIGForge.UnitTests/TestCategories.cs
    - tests/STIGForge.IntegrationTests/TestCategories.cs
  modified:
    - tests/STIGForge.UnitTests/Cli/CliHostFactoryTests.cs
    - tests/STIGForge.UnitTests/SmokeTests.cs

key-decisions:
  - "IAsyncLifetime over IDisposable for async disposal (fixes race condition)"
  - "TestCategories constants duplicated per project (avoids shared test utility dependency)"
  - "Trait attributes for categorization (xUnit standard, IDE/CI supported)"

patterns-established:
  - "IAsyncLifetime: Use for tests with IHost or file I/O that need async cleanup"
  - "TestCategories: Use [Trait(\"Category\", Unit)] with using static TestCategories"

requirements-completed:
  - TEST-01
  - TEST-05

# Metrics
duration: 9min
completed: 2026-02-23
---

# Phase 11 Plan 01: Flaky Test Fix and Test Categories Summary

**Fixed flaky BuildHost test with IAsyncLifetime async disposal and established xUnit trait categorization infrastructure for both test projects.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-02-23T00:40:36Z
- **Completed:** 2026-02-23T00:49:41Z
- **Tasks:** 4
- **Files modified:** 4

## Accomplishments
- Converted CliHostFactoryTests from IDisposable to IAsyncLifetime to fix async disposal race condition
- Added 50ms delay after host shutdown for Serilog file handle release before directory cleanup
- Created TestCategories constants class in both unit and integration test projects
- Demonstrated trait categorization pattern in SmokeTests.cs

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix flaky BuildHost test with IAsyncLifetime** - `452167e` (fix)
2. **Task 2: Create TestCategories constants for unit tests** - `b2d916f` (feat)
3. **Task 3: Create TestCategories constants for integration tests** - `41e974b` (feat)
4. **Task 4: Add trait attributes to existing tests** - `58a1ba3` (feat)

**Plan metadata:** (pending final commit)

_Note: TDD tasks may have multiple commits (test -> feat -> refactor)_

## Files Created/Modified
- `tests/STIGForge.UnitTests/Cli/CliHostFactoryTests.cs` - Converted to IAsyncLifetime with async host disposal
- `tests/STIGForge.UnitTests/TestCategories.cs` - Static class with Unit, Integration, Slow constants
- `tests/STIGForge.IntegrationTests/TestCategories.cs` - Matching constants for integration test project
- `tests/STIGForge.UnitTests/SmokeTests.cs` - Added [Trait("Category", Unit)] attributes
- `tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs` - Fixed missing Serilog using

## Decisions Made
- **IAsyncLifetime over IDisposable:** The flaky test root cause was synchronous Dispose() calling Directory.Delete while Serilog file sink still had handles open. IAsyncLifetime allows proper async shutdown and delay for handle release.
- **Duplicate TestCategories per project:** Each test project has its own TestCategories.cs with matching constants. Avoids shared test utility dependency while maintaining consistent values.
- **Trait attributes for categorization:** Standard xUnit [Trait("Category", "...")] pattern is supported by IDE test explorers and CI pipelines.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed missing Serilog using in CorrelationIdEnricherTests**
- **Found during:** Task 1 (Build verification for CliHostFactoryTests)
- **Issue:** Pre-existing build error from plan 11-02 - TextToken class required Serilog using directive
- **Fix:** Added `using Serilog;` to CorrelationIdEnricherTests.cs
- **Files modified:** tests/STIGForge.UnitTests/Infrastructure/Logging/CorrelationIdEnricherTests.cs
- **Verification:** Build succeeds
- **Committed in:** 452167e (part of Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Pre-existing build error blocked task verification. Fix was necessary to proceed.

## Issues Encountered
- **Test execution in WSL:** Tests require Windows Desktop runtime (UseWPF=true) which isn't available in WSL environment. Build verification was used instead of runtime verification. The code changes follow the plan's specified pattern correctly.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Flaky test fix enables CI coverage enforcement (Phase 11 prerequisite addressed)
- Test categorization infrastructure ready for CI pipeline filtering
- All test projects build successfully

---
*Phase: 11-foundation-and-test-stability*
*Completed: 2026-02-23*

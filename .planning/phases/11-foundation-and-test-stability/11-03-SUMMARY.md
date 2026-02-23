---
phase: 11-foundation-and-test-stability
plan: 03
subsystem: errors
tags: [error-handling, exceptions, error-codes, structured-errors]

# Dependency graph
requires: []
provides:
  - StigForgeException base class for all domain exceptions
  - ErrorCodes centralized constants for machine-readable error identifiers
  - BundleBuildException as example domain exception pattern
  - Unit tests for error infrastructure
affects: [all-phases]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "COMPONENT_NUMBER error code format (e.g., BUILD_001)"
    - "Abstract base exception with ErrorCode and Component properties"
    - "Factory methods on domain exceptions using ErrorCodes constants"

key-files:
  created:
    - src/STIGForge.Core/Errors/StigForgeException.cs
    - src/STIGForge.Core/Errors/ErrorCodes.cs
    - src/STIGForge.Core/Errors/BundleBuildException.cs
    - tests/STIGForge.UnitTests/Errors/StigForgeExceptionTests.cs
  modified: []

key-decisions:
  - "Use structured string codes (COMPONENT_NUMBER) rather than numeric HRESULT-style for self-documenting searchable errors"
  - "Flat two-part format for simplicity, can extend to hierarchical later if catalog grows"

patterns-established:
  - "Pattern: Domain exceptions inherit from StigForgeException with factory methods"
  - "Pattern: Error codes defined as const strings in ErrorCodes class"
  - "Pattern: ToString() includes error code prefix for logging"

requirements-completed: [ERRX-03]

# Metrics
duration: 5min
completed: 2026-02-22
---

# Phase 11 Plan 03: Error Code Infrastructure Summary

**Structured error code infrastructure with StigForgeException base class, centralized ErrorCodes constants, and BundleBuildException example domain exception**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-23T00:40:17Z
- **Completed:** 2026-02-23T00:45:05Z
- **Tasks:** 4
- **Files modified:** 4

## Accomplishments
- Created abstract StigForgeException base class with ErrorCode and Component properties
- Centralized error code constants for Build, Import, Apply, Verify, Export, Orchestration, Configuration, and Evidence categories
- Established domain exception pattern with BundleBuildException example
- Unit tests verify error code behavior, ToString format, and COMPONENT_NUMBER pattern compliance

## Task Commits

Each task was committed atomically:

1. **Task 1: Create StigForgeException base class** - `6d932d1` (feat)
2. **Task 2: Create ErrorCodes constants** - `147af71` (feat)
3. **Task 3: Create example domain exception** - `6e39011` (feat)
4. **Task 4: Create unit tests for error infrastructure** - `1276cba` (test)

## Files Created/Modified
- `src/STIGForge.Core/Errors/StigForgeException.cs` - Abstract base exception with ErrorCode property
- `src/STIGForge.Core/Errors/ErrorCodes.cs` - Centralized error code constants (20 codes across 8 categories)
- `src/STIGForge.Core/Errors/BundleBuildException.cs` - Example domain exception with factory methods
- `tests/STIGForge.UnitTests/Errors/StigForgeExceptionTests.cs` - Unit tests (5 tests, all passing)

## Decisions Made
- Used structured string codes (COMPONENT_NUMBER) rather than numeric HRESULT-style - they are self-documenting and searchable
- Flat two-part format for simplicity as recommended in RESEARCH.md, can extend to hierarchical format later if catalog needs grow

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Resolved metadata file caching issue for test execution**
- **Found during:** Task 4 (Unit test execution)
- **Issue:** dotnet test failed with CS0006 metadata file errors due to stale build cache
- **Fix:** Ran `dotnet clean` followed by `dotnet build` on the test project to clear cached references
- **Files modified:** None (build artifacts only)
- **Verification:** Tests ran successfully after clean rebuild
- **Committed in:** 1276cba (part of Task 4 commit)

**2. [Rule 3 - Blocking] Used Windows dotnet.exe for test execution on WSL**
- **Found during:** Task 4 (Unit test execution)
- **Issue:** Linux dotnet cannot execute net8.0-windows test assemblies
- **Fix:** Used `dotnet.exe` with Windows path format instead of WSL paths
- **Files modified:** None (execution environment only)
- **Verification:** All 5 tests passed via Windows runtime

---

**Total deviations:** 2 auto-fixed (2 blocking issues)
**Impact on plan:** Both issues were environment/infrastructure related, not code defects. No scope creep.

## Issues Encountered
- WSL/Linux dotnet cannot run net8.0-windows test projects - resolved by using Windows dotnet.exe with Windows-style paths
- Stale build cache caused metadata file errors - resolved with clean rebuild

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Error infrastructure complete and tested
- Pattern established for future domain exceptions
- Ready for error migration in subsequent phases

## Self-Check: PASSED
- All 4 created files verified present
- All 4 task commits verified in git history

---
*Phase: 11-foundation-and-test-stability*
*Completed: 2026-02-22*

---
phase: 16-xccdf-result-export
plan: 01
subsystem: export
tags: [XccdfExportAdapter, XCCDF, IExportAdapter, XML, ScapResultAdapter, round-trip, fail-closed]

# Dependency graph
requires:
  - phase: 15-pluggable-export-adapter-interface
    provides: IExportAdapter interface, ExportAdapterRequest/Result models, ExportAdapterRegistry, ExportOrchestrator
  - phase: 14-scc-verify-correctness-and-model-unification
    provides: ControlResult model and consolidated-results.json that adapters read from disk

provides:
  - XccdfExportAdapter implementing IExportAdapter with XCCDF 1.2 XML generation
  - export-xccdf CLI command loading verify results and producing XCCDF XML
  - Round-trip validated XML output parseable by ScapResultAdapter
  - Fail-closed write pattern with temp file cleanup

affects: [19-export-format-picker-ui]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "XCCDF export uses XDocument with XccdfNs on every element for namespace correctness"
    - "Fail-closed write: temp file + rename, cleanup on exception"
    - "Status/severity mapping is the exact inverse of ScapResultAdapter parsing"
    - "CLI export commands load results from Verify/consolidated-results.json using VerifyReportReader.LoadFromJson"

key-files:
  created:
    - src/STIGForge.Export/XccdfExportAdapter.cs
    - tests/STIGForge.UnitTests/Export/XccdfExportAdapterTests.cs
  modified:
    - src/STIGForge.Cli/Commands/ExportCommands.cs (added RegisterExportXccdf)

key-decisions:
  - "Benchmark root element (not standalone TestResult) for maximum tool compatibility with STIG Viewer, Tenable, ACAS"
  - "File.Move with pre-delete instead of overwrite param for net48 compatibility"
  - "Status mapping normalizes identically to ScapResultAdapter (strip underscores/hyphens/spaces, lowercase) ensuring round-trip fidelity"
  - "Weight attribute omitted for unknown/null severity rather than writing 0.0"

patterns-established:
  - "XCCDF export adapter pattern: build XDocument, write to temp, rename on success — reusable for any XML export format"
  - "Round-trip testing: export then parse with the corresponding import adapter to validate structural correctness"

requirements-completed: [EXP-01]

# Metrics
duration: 4min
completed: 2026-02-19
---

# Phase 16 Plan 01: XCCDF Result Export Summary

**XccdfExportAdapter generating XCCDF 1.2 XML from ControlResult data with round-trip ScapResultAdapter validation and export-xccdf CLI command**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-19T01:00:00Z
- **Completed:** 2026-02-19T01:04:00Z
- **Tasks:** 2
- **Files modified/created:** 3

## Accomplishments

- Implemented XccdfExportAdapter with correct XCCDF 1.2 namespace on every XML element, fail-closed write pattern, and status/severity mappings that are the exact inverse of ScapResultAdapter
- Round-trip validated: exported XML passes ScapResultAdapter.CanHandle() and ParseResults() recovers the exact same result count
- Wired export-xccdf CLI command with --bundle, --output, --file-name options; loads results from consolidated-results.json — 9 new tests pass, 0 regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Create XccdfExportAdapter with round-trip fidelity** - `2c57d76` (feat)
2. **Task 2: Wire export-xccdf CLI command** - `8b16a9b` (feat)

## Files Created/Modified

- `src/STIGForge.Export/XccdfExportAdapter.cs` - New: IExportAdapter implementation generating XCCDF 1.2 XML with Benchmark/TestResult/rule-result structure
- `tests/STIGForge.UnitTests/Export/XccdfExportAdapterTests.cs` - New: 9 tests (FormatName, extensions, valid XML, round-trip, namespace, empty results, null fields, status mapping, partial file cleanup)
- `src/STIGForge.Cli/Commands/ExportCommands.cs` - Added RegisterExportXccdf with --bundle, --output, --file-name options

## Decisions Made

- **Benchmark root element:** Used Benchmark as root (not standalone TestResult) because ScapResultAdapter.CanHandle() accepts both, but downstream tools like STIG Viewer and Tenable expect the Benchmark wrapper.
- **File.Move compatibility:** Used pre-delete + File.Move (no overwrite param) instead of File.Move with overwrite:true because the project multi-targets net48 where the overwrite parameter doesn't exist.
- **Weight attribute omission:** Weight attribute is omitted for null/unknown severity rather than writing "0.0", since ScapResultAdapter.MapWeightToSeverity returns null for weight 0.0 — this preserves round-trip fidelity.

## Deviations from Plan

### Auto-fixed Issues

**1. [File.Move overwrite param] net48 compatibility fix**
- **Found during:** Task 1 (XccdfExportAdapter implementation)
- **Issue:** `File.Move(src, dst, overwrite: true)` is not available on net48 target
- **Fix:** Changed to `if (File.Exists(outputPath)) File.Delete(outputPath); File.Move(tempPath, outputPath);`
- **Files modified:** src/STIGForge.Export/XccdfExportAdapter.cs
- **Verification:** Build succeeds on both net8.0 and net48 targets
- **Committed in:** 2c57d76 (Task 1 commit)

**2. [Test exception type] DirectoryNotFoundException is subclass of IOException**
- **Found during:** Task 1 (test writing)
- **Issue:** `Assert.ThrowsAsync<IOException>` fails because actual exception is `DirectoryNotFoundException`
- **Fix:** Changed to `Assert.ThrowsAnyAsync<IOException>` which accepts derived types
- **Files modified:** tests/STIGForge.UnitTests/Export/XccdfExportAdapterTests.cs
- **Verification:** All 9 tests pass
- **Committed in:** 2c57d76 (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (1 compatibility, 1 test precision)
**Impact on plan:** Both fixes necessary for correctness. No scope creep.

## Issues Encountered

None beyond the auto-fixed deviations above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- XccdfExportAdapter is registered and available for ExportAdapterRegistry (registration in DI deferred to Phase 19)
- CSV and Excel adapters (Phases 17-18) follow the same IExportAdapter pattern established here
- export-xccdf CLI command is available for operator use immediately

---
*Phase: 16-xccdf-result-export*
*Completed: 2026-02-19*

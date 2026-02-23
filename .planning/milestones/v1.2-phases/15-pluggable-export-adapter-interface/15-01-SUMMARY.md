---
phase: 15-pluggable-export-adapter-interface
plan: 01
subsystem: export
tags: [IExportAdapter, ExportAdapterRegistry, ExportOrchestrator, CklExportAdapter, EmassExporter, adapter-pattern, registry-pattern]

# Dependency graph
requires:
  - phase: 14-scc-verify-correctness-and-model-unification
    provides: ControlResult model and consolidated-results.json that adapters read from disk

provides:
  - IExportAdapter interface with FormatName, SupportedExtensions, ExportAsync
  - ExportAdapterRequest and ExportAdapterResult models
  - ExportAdapterRegistry with case-insensitive TryResolve, Register, GetAll
  - ExportOrchestrator dispatching to adapters by format name
  - EmassExporter implementing IExportAdapter via explicit interface implementation
  - CklExportAdapter wrapping static CklExporter with IExportAdapter contract

affects: [16-xccdf-export-adapter, 17-csv-export-adapter, 18-excel-export-adapter, 19-export-format-picker-ui]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Adapter registry pattern: ExportAdapterRegistry holds List<IExportAdapter>, resolves by case-insensitive FormatName"
    - "Explicit interface implementation: IExportAdapter.ExportAsync on EmassExporter avoids overload ambiguity with existing ExportAsync(ExportRequest, ct)"
    - "Static wrapper: CklExportAdapter wraps public static CklExporter preserving all existing call sites"
    - "Fail-closed result: ExportAdapterResult returned for unknown format (Success=false, ErrorMessage) rather than null or exception"

key-files:
  created:
    - src/STIGForge.Export/ExportAdapterRegistry.cs
    - src/STIGForge.Export/ExportOrchestrator.cs
    - src/STIGForge.Export/CklExportAdapter.cs
    - tests/STIGForge.UnitTests/Export/ExportAdapterRegistryTests.cs
    - tests/STIGForge.UnitTests/Export/ExportOrchestratorTests.cs
    - tests/STIGForge.UnitTests/Export/EmassExportAdapterTests.cs
    - tests/STIGForge.UnitTests/Export/CklExportAdapterTests.cs
  modified:
    - src/STIGForge.Export/ExportModels.cs (added IExportAdapter, ExportAdapterRequest, ExportAdapterResult)
    - src/STIGForge.Export/EmassExporter.cs (implements IExportAdapter via explicit interface)

key-decisions:
  - "CklExportAdapter wrapper class (not adding interface to static CklExporter): static classes cannot implement interfaces in C#; wrapper preserves all CklExporter.ExportCkl static call sites unchanged"
  - "EmassExporter explicit interface implementation: avoids any overload ambiguity with existing ExportAsync(ExportRequest, ct) which existing tests call directly"
  - "CklExportAdapter.ExportAsync returns Success=true on no-exception path even when ControlCount=0; empty export is a valid outcome, message surfaces as Warning not ErrorMessage"
  - "ExportAdapterRegistry defers DI wiring to Phase 19; Phase 15 only establishes the interface and adapters"

patterns-established:
  - "IExportAdapter: all future export adapters (XCCDF, CSV, Excel) implement FormatName, SupportedExtensions, ExportAsync(ExportAdapterRequest, ct)"
  - "ExportAdapterRegistry: register adapters by calling Register(adapter); resolve at runtime by TryResolve(formatName)"
  - "ExportOrchestrator: entry point for registry-dispatched exports; returns fail-closed ExportAdapterResult for unknown format"

requirements-completed: [EXP-04, EXP-05]

# Metrics
duration: 3min
completed: 2026-02-19
---

# Phase 15 Plan 01: Pluggable Export Adapter Interface Summary

**IExportAdapter contract with ExportAdapterRegistry, ExportOrchestrator, and two adapter implementations (EmassExporter, CklExportAdapter) wiring the pluggable export architecture for Phases 16-19**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-19T00:44:53Z
- **Completed:** 2026-02-19T00:48:00Z
- **Tasks:** 2
- **Files modified/created:** 9

## Accomplishments

- Defined IExportAdapter interface, ExportAdapterRequest, and ExportAdapterResult in ExportModels.cs — the common contract all future format adapters must implement
- Implemented ExportAdapterRegistry (case-insensitive format lookup) and ExportOrchestrator (fail-closed dispatch)
- Retrofitted EmassExporter with IExportAdapter via explicit interface implementation, and created CklExportAdapter wrapping static CklExporter — all 71 export tests pass, 0 regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Define IExportAdapter, models, registry, and orchestrator** - `0c43d47` (feat)
2. **Task 2: Retrofit EmassExporter and create CklExportAdapter** - `3590286` (feat)

## Files Created/Modified

- `src/STIGForge.Export/ExportModels.cs` - Added IExportAdapter interface, ExportAdapterRequest, ExportAdapterResult (plus `using STIGForge.Verify;`)
- `src/STIGForge.Export/ExportAdapterRegistry.cs` - New: Register, TryResolve (case-insensitive), GetAll
- `src/STIGForge.Export/ExportOrchestrator.cs` - New: dispatches to adapter from registry; returns fail-closed result for unknown format
- `src/STIGForge.Export/CklExportAdapter.cs` - New: IExportAdapter wrapper for static CklExporter; delegates to CklExporter.ExportCkl
- `src/STIGForge.Export/EmassExporter.cs` - Added IExportAdapter explicit implementation; existing ExportAsync(ExportRequest, ct) unchanged
- `tests/STIGForge.UnitTests/Export/ExportAdapterRegistryTests.cs` - New: 4 tests (null guard, case-insensitive resolve, unknown format, GetAll count)
- `tests/STIGForge.UnitTests/Export/ExportOrchestratorTests.cs` - New: 2 tests (known format delegates, unknown format returns failure)
- `tests/STIGForge.UnitTests/Export/EmassExportAdapterTests.cs` - New: 3 tests (FormatName, SupportedExtensions, ImplementsIExportAdapter)
- `tests/STIGForge.UnitTests/Export/CklExportAdapterTests.cs` - New: 4 tests (FormatName, SupportedExtensions, ImplementsIExportAdapter, EmptyBundleRoot failure)

## Decisions Made

- **CklExportAdapter wrapper instead of modifying static class:** C# static classes cannot implement interfaces. Created `CklExportAdapter` as a separate class that delegates to `CklExporter.ExportCkl()`; all existing static call sites in `MainViewModel.Export.cs` and `ExportCommands.cs` remain unchanged.
- **EmassExporter explicit interface implementation:** Used `async Task<ExportAdapterResult> IExportAdapter.ExportAsync(...)` (explicit) rather than a regular overload to prevent any ambiguity with the existing `Task<ExportResult> ExportAsync(ExportRequest, ct)` method. Existing tests and callers are unaffected.
- **CklExportAdapter returns Success=true on empty-results path:** An empty export (ControlCount=0) is a valid outcome (fresh bundle); surfaces the message as a Warning, not ErrorMessage.

## Deviations from Plan

None — plan executed exactly as written. The FakePathBuilder in EmassExportAdapterTests required correcting the interface member list to match the actual IPathBuilder definition (auto-fix during test authoring, within task scope).

## Issues Encountered

The EmassExportAdapterTests FakePathBuilder initially listed incorrect method signatures (different from the actual IPathBuilder interface). Corrected inline before the first test run — no deviation from plan required.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- IExportAdapter contract is complete and tested — Phases 16 (XCCDF), 17 (CSV), 18 (Excel) can implement the interface directly
- ExportAdapterRegistry is available for adapter registration in each format phase
- ExportOrchestrator is available for format-dispatched CLI commands in Phases 16-18
- DI/IoC wiring of registry deferred to Phase 19 (WPF export format picker)

---
*Phase: 15-pluggable-export-adapter-interface*
*Completed: 2026-02-19*

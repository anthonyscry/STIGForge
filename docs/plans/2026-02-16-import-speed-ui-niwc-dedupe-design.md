# Import Speed, Busy Cursor, and NIWC SCAP Dedupe Priority Design

Date: 2026-02-16
Owner: STIGForge App + Content Import

## Context

The import pipeline now correctly detects and routes more STIG and SCAP inputs, but two user-facing issues remain:

1. Importing large bundles can feel slow.
2. The app appears frozen during import operations.

A third behavior requirement is also needed for deterministic SCAP selection:

3. When SCAP candidates represent the same logical content but differ by hash/pack ID, prefer NIWC enhanced SCAP from consolidated bundle sources.

Current observations from codebase:

- `ScanImportFolderAsync` performs scan/dedupe/plan/import orchestration in one command path and currently executes heavy archive and parser work in ways that can block UI responsiveness.
- Import UI updates currently happen per imported pack and include repeated selected-pack churn.
- Existing dedupe already prefers NIWC enhanced SCAP for one narrow conflict class (same parsed version + different hash), but not for all same-logical-content SCAP hash conflicts.

## Goals

- Keep UI responsive while scan/import runs.
- Provide clear visual busy feedback using a loading cursor while operations are in progress.
- Improve throughput in safe mode without introducing SQLite lock contention.
- Ensure SCAP dedupe consistently prioritizes NIWC enhanced consolidated-bundle candidates for same-content hash conflicts.

## Non-Goals

- No aggressive parallel archive import writes to SQLite.
- No schema changes to storage.
- No redesign of import tab layout.

## Considered Approaches

### Approach A - Safe speedup (recommended)

- Keep archive import serial.
- Move CPU and file-heavy scan/import work off UI thread.
- Batch UI mutations to reduce per-pack UI churn.
- Add wait cursor tied to `IsBusy`.
- Expand SCAP dedupe NIWC preference condition to all same-logical-content hash conflicts.

Pros:

- Improves perceived responsiveness with low risk.
- Avoids SQLite concurrency and lock issues.
- Small, targeted changes.

Cons:

- Peak throughput lower than full parallel import.

### Approach B - Mixed mode

- Parallelize scan candidate extraction only.
- Keep import serial and apply same UI/dedupe improvements.

Pros:

- Better scan phase time on large import folders.

Cons:

- More complexity in scanner orchestration.
- Limited practical benefit if import dominates runtime.

### Approach C - Aggressive parallel import

- Parallelize archive import and persistence.

Pros:

- Potentially fastest end-to-end wall time.

Cons:

- Highest risk (`database is locked`, interleaving, harder error handling).
- Requires transactional and retry strategy work beyond current scope.

## Selected Design

Adopt Approach A.

### 1) Responsiveness and safe speed improvements

In `MainViewModel.Import`:

- Run scanner and archive import operations on background workers (`Task.Run`) so heavy synchronous archive/parsing/file IO does not execute on UI thread.
- Keep import operations serial to preserve SQLite safety.
- Avoid repeated selection churn by changing import insertion behavior:
  - insert imported packs without reassigning `SelectedPack` for each one,
  - set `SelectedPack` once after import batch completes.
- Keep existing summary logging and scan outcome reporting.

### 2) Busy cursor while importing

In `OnIsBusyChanged` behavior:

- When `IsBusy` becomes `true`, set app cursor override to wait cursor.
- When `IsBusy` becomes `false`, clear cursor override.
- Keep existing progress bar/status behavior unchanged.

### 3) SCAP dedupe NIWC priority

In `ImportDedupService`:

- For SCAP-only groups that share the same logical key and have multiple distinct hashes, prefer NIWC enhanced consolidated-bundle candidate whenever present.
- Preserve deterministic fallback ordering when NIWC candidate is not available.
- Update decision text to clearly record NIWC-based choice in scan warnings/notes.

## Data Flow Impact

- No changes to persisted schema.
- No changes to route mapping semantics.
- Dedup outcome remains the same shape (`Winners`, `Suppressed`, `Decisions`) with refined winner selection for SCAP conflicts.

## Error Handling

- Preserve existing per-archive try/catch failure capture.
- Preserve cancellation behavior via existing `_cts` token usage.
- Ensure busy cursor is always reset via `finally` and `IsBusy` transition.

## Test Strategy

Add/adjust unit coverage for:

1. SCAP dedupe chooses NIWC enhanced consolidated bundle when logical content key matches and hashes differ even if parsed versions differ or are missing.
2. Existing deterministic fallback remains when NIWC is absent.
3. Existing import scanner/importer tests continue to pass unchanged.

Manual verification:

- Trigger import scan with large mixed bundle set and verify UI remains responsive.
- Confirm wait cursor appears during import and clears afterward.
- Confirm summary decisions include NIWC-priority dedupe note when applicable.

## Risks and Mitigations

- Risk: Background work attempts to mutate UI-bound collections.
  - Mitigation: confine UI collection mutations to UI thread after background phase.
- Risk: Over-broad NIWC preference might mask intentional non-NIWC source selection.
  - Mitigation: scope to same logical content + conflicting hashes and emit explicit decision notes.

## Success Criteria

- Import tab no longer appears frozen during long imports.
- Wait cursor is visible during active import operations.
- SCAP duplicate conflicts consistently keep NIWC enhanced consolidated candidate when present.
- Unit tests pass for dedupe and import content suites.

## Implementation Notes (2026-02-16)

- Implemented NIWC-priority dedupe for SCAP same-logical-content hash conflicts in `src/STIGForge.Content/Import/ImportDedupService.cs`.
- Added regression coverage for version-mismatch and missing-version SCAP NIWC-priority cases in `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs`.
- Updated import orchestration to offload scan/import-heavy work and batch imported pack selection in `src/STIGForge.App/MainViewModel.Import.cs`.
- Added wait cursor tied to `IsBusy` transitions in `src/STIGForge.App/MainViewModel.Dashboard.cs`.
- Verification run results:
  - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj` -> 351 passed, 0 failed.
  - `dotnet build C:\Projects\STIGForge\src\STIGForge.App\STIGForge.App.csproj` (via host PowerShell) -> succeeded, 0 errors.

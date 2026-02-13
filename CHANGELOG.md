# Changelog

All notable changes to STIGForge are documented in this file.

## Unreleased

### Refactor
- Centralized CSV parsing into `STIGForge.Core/Utilities/CsvUtility.cs` and routed App/CLI/Export/Core callers to shared parsing logic.
- Removed duplicated local CSV parser implementations in export/core services.

### Performance and Reliability
- Replaced remaining `File.ReadAllLines(...)` hot paths with streaming `File.ReadLines(...)` usage in App/CLI/Export to reduce peak memory usage on large files.
- Improved fleet CSV import parsing robustness by switching from naive `Split(',')` to shared CSV parsing logic.

### Repository Cleanup
- Reorganized WPF viewmodels into domain folders under `src/STIGForge.App/ViewModels/` (`Main/`, `Manual/`, `Import/`, `Common/`).
- Added and documented `docs/RepoStructure.md`.
- Expanded ignore rules for local scratch/temp artifacts and local DB files.

### Documentation
- Updated `README.md` notes and repo-layout links.
- Added architecture and repository-structure documentation for the current modularized layout.

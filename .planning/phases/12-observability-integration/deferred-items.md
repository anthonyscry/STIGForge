# Deferred Items - Phase 12

## 2026-02-23 - Plan 12-02

### Pre-existing Issue: Duplicate `export-emass` CLI Command

**Description:** The `export-emass` command is registered twice:
- `src/STIGForge.Cli/Commands/VerifyCommands.cs` line 169
- `src/STIGForge.Cli/Commands/ExportCommands.cs` line 125

**Impact:** Running any CLI command fails with error: `[CLI-ARG-001] An item with the same key has already been added. Key: export-emass`

**Status:** Out of scope for this plan. Pre-existing issue unrelated to current task changes.

**Recommendation:** One of the duplicate registrations should be removed in a future maintenance task.

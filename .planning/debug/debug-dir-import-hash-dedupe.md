# Debug Session: Directory Import Hash + Dedupe Gap

Date: 2026-02-20
Scope: UAT Test 1 root-cause diagnosis only (no code changes)

## Investigation Checklist

1. Verify whether `01-01-SUMMARY.md` implementation exists in the active runnable project path.
2. Verify whether implementation is isolated to a worktree branch and missing from current app build targets.
3. Inspect importer dedupe behavior expectations vs implementation.
4. Identify exact files/lines tied to root cause.

## Evidence Collected

### A) Summary claim vs active runnable code mismatch

- Claim: `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md:9-12` says directory import computes deterministic SHA-256 manifest hash and has dedicated tests.
- Active runnable code: `src/STIGForge.Content/Import/ContentPackImporter.cs:359` sets `ManifestSha256 = packId` in `ImportDirectoryAsPackAsync`, which is a 32-char GUID-like id, not a 64-char SHA-256 digest.
- Active tests: `tests/STIGForge.UnitTests/Content/ContentPackImporterDirectoryHashTests.cs` is missing in this workspace (file not present).

Conclusion: UAT expectation for deterministic 64-char manifest hash cannot pass in current runnable branch because directory imports persist pack ID as hash.

### B) Worktree-only implementation, not in current app build targets

- Phase-01 worktree code exists at `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Content/Import/ContentPackImporter.cs:351` and `:620`:
  - calls `ComputeDirectoryManifestSha256Async(rawRoot, ct)`
  - stores `ManifestSha256 = directoryManifestHash`
- Same phase-01 worktree includes tests: `.worktrees/gsd-phase-01-foundations-gap-closure/tests/STIGForge.UnitTests/Content/ContentPackImporterDirectoryHashTests.cs`.
- Current runnable workspace still has old behavior: `src/STIGForge.Content/Import/ContentPackImporter.cs:359` (`ManifestSha256 = packId`).
- Main run worktree also still has old behavior: `.worktrees/main-run/src/STIGForge.Content/Import/ContentPackImporter.cs:366` (`ManifestSha256 = packId`).

Conclusion: Deterministic directory-hash implementation is present in a separate worktree branch but not in the active/runnable app targets used for UAT.

### C) Duplicate-import dedupe behavior (implemented scope vs expected scope)

- `src/STIGForge.App/MainViewModel.Import.cs:73-75` runs `ImportDedupService.Resolve(scan.Candidates)` only on the *current scan candidate list* (inbox run-time batch dedupe).
- `src/STIGForge.Content/Import/ImportDedupService.cs:9-42` only accepts `IReadOnlyList<ImportInboxCandidate>` and returns winners/suppressed for that scan input; no repository/database lookup is performed.
- `src/STIGForge.Content/Import/ContentPackImporter.cs:318` always generates new `packId` per import; `:407` and `:409` always save pack and controls.
- `src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs:16-24` upserts `content_packs` on `pack_id` conflict only; no uniqueness/lookup by `manifest_sha256` or logical content key.
- `src/STIGForge.Infrastructure/Storage/DbBootstrap.cs:48-56` defines `content_packs` with `pack_id` primary key only; no unique constraint on `manifest_sha256`.
- `src/STIGForge.Content/Import/ImportRecoveryAndConflictModels.cs:147-149` ignores same-ID controls when fields are identical (no differences), so identical re-import is not blocked by conflict rules.

Conclusion: Cross-import dedupe against already-imported content is not implemented in persistence/import flow; duplicate imports are currently allowed by design.

## Root Cause

The UAT failure is caused by branch drift plus missing cross-import dedupe: current runnable builds never received the Phase-01 directory SHA-256 manifest-hash implementation (still storing `packId`), and import dedupe only works within a single scan batch (not against existing persisted packs), so re-importing unchanged content creates duplicate packs.

## Files and Lines Tied to Root Cause

- `src/STIGForge.Content/Import/ContentPackImporter.cs:359` - directory import stores `packId` as `ManifestSha256`.
- `src/STIGForge.Content/Import/ContentPackImporter.cs:318` - new GUID `packId` generated every import.
- `src/STIGForge.App/MainViewModel.Import.cs:73-75` - dedupe limited to current scan candidates.
- `src/STIGForge.Content/Import/ImportDedupService.cs:9-42` - no persisted-pack dedupe path.
- `src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs:16-24` - upsert key is only `pack_id`.
- `src/STIGForge.Infrastructure/Storage/DbBootstrap.cs:48-56` - schema lacks unique dedupe key on manifest hash/logical identity.
- `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Content/Import/ContentPackImporter.cs:351` - worktree-only deterministic hash call.
- `.worktrees/gsd-phase-01-foundations-gap-closure/src/STIGForge.Content/Import/ContentPackImporter.cs:620` - worktree-only hash implementation function.
- `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md:9-12` - claims implementation not present in runnable branch.

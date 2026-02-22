# Phase 01-02 Execution Summary

Date: 2026-02-22
Plan: `.planning/phases/2026-02-19-stigforge-next/01-02-PLAN.md`

## Implemented

- `src/STIGForge.Content/Import/ContentPackImporter.cs`
  - Directory import now computes deterministic SHA-256 manifest hash using `ComputeDirectoryManifestSha256Async` based on normalized relative paths and file hashes.
  - Added pre-persistence dedupe gate in `ImportDirectoryAsPackAsync` - queries existing packs by manifest hash and returns existing pack if found (no duplicate persisted).
  - Added `System.Linq` using directive for `.FirstOrDefault()` support.
- `src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs`
  - Fixed `ListAsync` and `GetAsync` methods to properly handle Dapper dynamic results with DateTimeOffset type handling via explicit pattern matching.
- `tests/STIGForge.UnitTests/Content/ContentPackImporterDirectoryHashTests.cs`
  - Added 5 regression tests for deterministic directory hash and persisted dedupe behavior:
    - `ImportDirectoryAsPackAsync_ComputesDeterministicSha256ManifestHash` - validates 64-char lowercase SHA-256 format
    - `ImportDirectoryAsPackAsync_SameDirectoryYieldsIdenticalHash` - identical content yields same hash regardless of path/name
    - `ImportDirectoryAsPackAsync_Dedupe_SameContentReturnsExistingPack` - persisted dedupe returns existing pack ID
    - `ImportDirectoryAsPackAsync_DifferentContentCreatesNewPack` - different content creates new pack with different hash
    - `ImportDirectoryAsPackAsync_HashStableAcrossRepeatedImportsOfUnchangedContent` - re-import of unchanged content deduped

## Verification Evidence

- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterDirectoryHashTests"` -> PASS (5)
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportDedupServiceTests"` -> PASS (8)
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ContentPackImporterTests"` -> PASS (28)
- Full test suite: 556 passed, 1 pre-existing flaky test (CliHostFactoryTests.BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory)
- `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true` -> PASS (0 errors, warnings only)

## Remaining Manual Checks

- Run UAT Test 1 again to verify stable deterministic hash plus deduplicated unchanged imports.

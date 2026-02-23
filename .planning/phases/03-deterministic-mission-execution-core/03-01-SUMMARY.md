---
plan: 03-01
status: complete
commit: f755f17
---

## Summary

Plan 03-01 (BLD-01: Bundle Determinism Contract) is complete.

### Changes

1. **SchemaVersion on BundleManifest** (`src/STIGForge.Build/BundleModels.cs`): Added `SchemaVersion` property defaulting to `1` for forward compatibility.

2. **BuildTime deterministic seeding** (`src/STIGForge.Build/BuildTime.cs`): Added `Seed(DateTimeOffset)` and `Reset()` methods so tests can pin timestamps for reproducible bundle output.

3. **Apply template validation** (`src/STIGForge.Build/BundleBuilder.cs`): `CopyApplyTemplates` now returns `bool` indicating success. `ValidateApplyTemplates` verifies the Apply directory and presence of `.ps1` scripts when templates were successfully copied; gracefully skips validation when no repo root is found.

4. **InternalsVisibleTo** (`src/STIGForge.Build/STIGForge.Build.csproj`): Exposed internal types to `STIGForge.UnitTests` for BuildTime access in tests.

5. **Determinism tests** (`tests/STIGForge.UnitTests/Build/BundleBuilderDeterminismTests.cs`): Three xUnit + FluentAssertions + Moq tests:
   - `IdenticalInputs_ProduceIdenticalHashes` - verifies two builds with identical inputs and seeded clock produce identical `file_hashes.sha256` manifests.
   - `SchemaVersion_IsSetToOne` - verifies deserialized `manifest.json` has `SchemaVersion == 1`.
   - `MissingApplyTemplates_SkipsValidation` - verifies builds outside a git repo skip template validation without throwing.

### Verification

- `dotnet build` passes with 0 warnings, 0 errors.
- All 446 tests pass (3 new + 443 existing), 0 skipped.

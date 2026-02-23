---
plan: 03-03
status: complete
commit: fcebf8d
---

## Summary

Plan 03-03 (MAP-01: SCAP Mapping Manifest) is complete.

### Changes

1. **ScapMappingManifest model** (`src/STIGForge.Core/Models/ScapMappingManifest.cs`): New model with `ScapMappingMethod` enum (BenchmarkOverlap/StrictTagMatch/Unmapped), `ScapControlMapping` per-control entry, and `ScapMappingManifest` container with computed `UnmappedCount`.

2. **BuildMappingManifest** (`src/STIGForge.Core/Services/CanonicalScapSelector.cs`): Extended with `BuildMappingManifest(input, controls)` that calls existing `Select()` for winner, then maps each control using:
   - BenchmarkOverlap (confidence=1.0): control's BenchmarkId matches winner's benchmark IDs
   - StrictTagMatch (confidence=0.7): control's RuleId matches winner's benchmark IDs
   - Unmapped (confidence=0.0): no match, reason="no_scap_mapping"
   - No candidates: all controls are Unmapped

3. **BundleBuilder integration** (`src/STIGForge.Build/BundleBuilder.cs`): Optional `CanonicalScapSelector` constructor parameter. When provided with `ScapCandidates` on the request, writes `scap_mapping_manifest.json` to `Manifest/` directory.

4. **Model updates** (`src/STIGForge.Build/BundleModels.cs`): Added `ScapCandidates` to `BundleBuildRequest` and `ScapMappingManifestPath` to `BundleBuildResult`.

5. **Tests** (`tests/STIGForge.UnitTests/Services/ScapMappingManifestTests.cs`): 4 tests:
   - `SingleBenchmarkPerStig_ProducesConsistentMapping` - verifies winner selection and no cross-STIG mapping
   - `UnmappedControls_HaveNoScapMappingReason` - verifies all controls unmapped when no candidates exist
   - `NoCrossStigFallback_EnforcedWhenWinnerSelected` - verifies controls from different benchmarks are not cross-mapped
   - `MappingConfidence_MatchesMethod` - verifies confidence values match mapping methods

### Verification

- All 4 ScapMappingManifest tests pass.
- All 5 existing CanonicalScapSelector tests still pass.
- Full build compiles with 0 errors.

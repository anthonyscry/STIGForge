# Phase 3 Plan Verification

**Verified:** 2026-02-22
**Result:** PASSED

## Requirement Coverage

| Requirement | Description | Plans | Status |
|---|---|---|---|
| BLD-01 | Deterministic bundle compiler outputs `Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, `Manifest/` tree | 03-01 | SATISFIED |
| APL-01 | Apply preflight enforces elevation/compatibility/reboot/PowerShell safety checks | 03-02 | SATISFIED |
| APL-02 | Apply supports PowerSTIG/DSC primary backend, optional GPO/LGPO path, and script fallback with reboot-aware convergence | 03-02, 03-04 | SATISFIED |
| VER-01 | Verify wrappers normalize SCAP/SCC and Evaluate-STIG outputs into canonical result model | 03-05 | SATISFIED |
| MAP-01 | Strict per-STIG SCAP mapping contract is enforced (per-STIG computation, benchmark-overlap primary, strict fallback tags, no broad fallback) | 03-03 | SATISFIED |

---

## BLD-01: Deterministic Bundle Compiler

**Plans:** 03-01

### Test Evidence

- `tests/STIGForge.UnitTests/Build/BundleBuilderDeterminismTests.cs`
  - `IdenticalInputs_ProduceIdenticalHashes` (lines 33-62) - verifies two builds with identical inputs and seeded clock produce identical `file_hashes.sha256` manifests
  - `SchemaVersion_IsSetToOne` (lines 64-87) - verifies deserialized `manifest.json` has `SchemaVersion == 1`
  - `MissingApplyTemplates_SkipsValidation` (lines 89-109) - verifies builds outside a git repo skip template validation without throwing

### Source Evidence

- `src/STIGForge.Build/BundleModels.cs:58` - `SchemaVersion = 1` default on `BundleManifest`
- `src/STIGForge.Build/BuildTime.cs:27-32` - `Seed(DateTimeOffset)` and `Reset()` methods for deterministic timestamps
- `src/STIGForge.Build/BundleBuilder.cs:174-185` - `ValidateApplyTemplates()` method that checks Apply directory and `.ps1` scripts
- `src/STIGForge.Build/BundleBuilder.cs:274-290` - `WriteHashManifestAsync()` produces `file_hashes.sha256`

### Commit Evidence

- Commit `f755f17` (03-01-SUMMARY.md)
  - Added SchemaVersion on BundleManifest
  - BuildTime deterministic seeding
  - Apply template validation
  - 3 determinism tests (446 total tests pass)

**Status:** SATISFIED

---

## APL-01: Apply Preflight Safety Checks

**Plans:** 03-02

### Test Evidence

- `tests/STIGForge.UnitTests/Apply/PreflightRunnerTests.cs`
  - `MissingScript_ReturnsNotOkWithDescriptiveIssue` (lines 31-45) - verifies missing Preflight.ps1 returns exit code -1 with descriptive message
  - `ParseResult_ValidJson_ReturnsPreflightResult` (lines 47-67) - verifies JSON parsing with valid output
  - `ParseResult_ValidJsonWithIssues_ReturnsIssues` (lines 69-90) - verifies JSON with issues list
  - `ParseResult_NonZeroExitCode_OverridesJsonExitCode` (lines 92-109) - verifies process exit code wins over JSON
  - `ParseResult_InvalidJson_FallsBackToRawOutput` (lines 111-125) - verifies graceful fallback for parse failures
  - `ParseResult_EmptyOutput_ZeroExitCode_ReturnsOk` (lines 127-134) - verifies empty output with exit 0 is OK
  - `ParseResult_EmptyOutput_NonZeroExitCode_ReturnsNotOk` (lines 136-144) - verifies empty output with non-zero exit is not OK

### Source Evidence

- `tools/apply/Preflight/Preflight.ps1`
  - `Test-IsAdmin` (lines 26-30) - admin rights check
  - `Test-PendingReboot` (lines 32-41) - reboot detection
  - `Test-PowerStigAvailable` (lines 43-55) - PowerSTIG module availability
  - `Test-DscResources` (lines 57-96) - DSC resource validation
  - `Test-MutualExclusion` (lines 98-135) - DSC/LGPO conflict detection
  - JSON output via `ConvertTo-Json` (line 189)
  - Exit code 0 (OK) or 1 (issues) (line 180)

- `src/STIGForge.Apply/PreflightRunner.cs`
  - `RunPreflightAsync` method (lines 21-101) - invokes PowerShell, captures stdout/stderr, handles timeouts
  - `ParseResult` method (lines 103-139) - JSON parsing with graceful fallback

- `src/STIGForge.Apply/ApplyModels.cs`
  - `PreflightRequest` class (lines 102-109) - BundleRoot, ModulesPath, PowerStigModulePath, CheckLgpoConflict, BundleManifestPath
  - `PreflightResult` class (lines 111-117) - Ok, Issues, Timestamp, ExitCode

### Commit Evidence

- Commit `cbf7516` (03-02-SUMMARY.md)
  - Extended Preflight.ps1 with PowerSTIG, DSC, mutual-exclusion checks
  - PreflightRunner C# wrapper with JSON parsing
  - PreflightRequest/PreflightResult models
  - 7 PreflightRunner tests pass

**Status:** SATISFIED

---

## APL-02: Multi-Backend Apply with Convergence

**Plans:** 03-02 (preflight), 03-04 (LGPO + convergence)

### Test Evidence

- `tests/STIGForge.UnitTests/Apply/LgpoRunnerTests.cs`
  - `ApplyPolicy_MissingLgpoExe_ThrowsFileNotFound` (lines 13-32) - verifies FileNotFoundException for missing LGPO.exe
  - `ApplyPolicy_MissingPolFile_ThrowsFileNotFound` (lines 34-56) - verifies FileNotFoundException for missing .pol file
  - `LgpoApplyRequest_DefaultsToMachineScope` (lines 58-63) - verifies Machine scope default
  - `LgpoApplyResult_SuccessWhenExitCodeZero` (lines 65-80) - verifies success result structure

- `tests/STIGForge.UnitTests/Apply/ApplyConvergenceTests.cs`
  - `MaxReboots_Exceeded_ThrowsRebootException` (lines 14-36) - verifies max reboot limit enforcement
  - `RebootCount_IncrementedOnEachSchedule` (lines 38-70) - verifies count increment
  - `MaxReboots_AtLimit_ScheduleSucceeds_ThenNextFails` (lines 72-106) - verifies boundary behavior
  - `ConvergenceStatus_Converged_WhenAllStepsComplete` (lines 108-119) - verifies Converged status
  - `ConvergenceStatus_Exceeded_WhenMaxReboots` (lines 121-133) - verifies Exceeded status
  - `ConvergenceStatus_NotApplicable_Default` (lines 135-141) - verifies default status
  - `MaxReboots_ConstantIsThree` (lines 143-147) - verifies MaxReboots = 3

### Source Evidence

- `src/STIGForge.Apply/Lgpo/LgpoRunner.cs`
  - `ApplyPolicyAsync` method (lines 23-97) - wraps LGPO.exe for `/m` (Machine) and `/u` (User) scope
  - `ExportPolicyAsync` method (lines 102-136) - exports policy to text file
  - 60-second timeout, FileNotFoundException for missing exe/pol

- `src/STIGForge.Apply/Lgpo/LgpoModels.cs`
  - `LgpoScope` enum (Machine/User)
  - `LgpoApplyRequest` and `LgpoApplyResult` models

- `src/STIGForge.Apply/Reboot/RebootCoordinator.cs`
  - `MaxReboots = 3` constant (line 14)
  - `ScheduleReboot` method (lines 67-107) - enforces max reboot limit, throws `RebootException("max_reboot_exceeded")`
  - Count increment before marker write (lines 84-85)

- `src/STIGForge.Apply/Reboot/RebootModels.cs`
  - `RebootCount` on `RebootContext`

- `src/STIGForge.Apply/ApplyModels.cs`
  - `ConvergenceStatus` enum (lines 6-12): Converged, Diverged, Exceeded, NotApplicable
  - LGPO fields on `ApplyRequest` (lines 30-37): LgpoPolFilePath, LgpoScope, LgpoExePath
  - `RebootCount` and `ConvergenceStatus` on `ApplyResult` (lines 96-99)

### Commit Evidence

- Commit `a81dea5` (03-04-SUMMARY.md)
  - LgpoRunner wraps LGPO.exe for Machine/User scope
  - ConvergenceStatus enum and RebootCount tracking
  - MaxReboots = 3 enforcement in RebootCoordinator
  - 11 tests (LgpoRunnerTests + ApplyConvergenceTests) pass

**Status:** SATISFIED

---

## VER-01: Verify Normalization with Provenance

**Plans:** 03-05

### Test Evidence

- `tests/STIGForge.UnitTests/Verify/VerifyOrchestratorMappingTests.cs`
  - `MappingManifest_AssociatesResultsPerStig` (lines 13-44) - verifies 3 mapped controls get correct BenchmarkId
  - `UnmappedControls_IncludeNoScapMappingReason` (lines 46-76) - verifies unmapped controls get metadata status
  - `NullManifest_PreservesExistingBehavior` (lines 78-95) - verifies null manifest changes nothing
  - `ExistingMergePrecedence_PreservedWithMapping` (lines 97-169) - verifies CKL still wins over SCAP after mapping
  - `ResultsNotInManifest_GetNotInManifestStatus` (lines 171-194) - verifies missing controls get not_in_manifest

### Source Evidence

- `src/STIGForge.Verify/NormalizedVerifyResult.cs`
  - `RawArtifactPath` property (line 58) - absolute path to raw tool output
  - `BenchmarkId` property (line 64) - SCAP benchmark ID from mapping manifest
  - `NormalizedVerifyReport.RawArtifactPath` (line 129) - report-level provenance

- `src/STIGForge.Verify/VerifyOrchestrator.cs`
  - `ApplyMappingManifest` method (lines 78-126):
    - BenchmarkOverlap/StrictTagMatch: sets `BenchmarkId` on result
    - Unmapped: adds `mapping_status=no_scap_mapping` to metadata
    - Not in manifest: adds `mapping_status=not_in_manifest` to metadata
    - Null manifest: no-op (backward compatible)
  - `ParseAndMergeResults` overload (lines 67-72) - accepts optional manifest parameter
  - `ReconcileResults` method (lines 284-291) - includes `raw_artifact_paths` in merged metadata

### Commit Evidence

- Commit `29482f8` (03-05-SUMMARY.md)
  - RawArtifactPath and BenchmarkId on NormalizedVerifyResult
  - All three adapters set RawArtifactPath
  - ApplyMappingManifest enriches results from ScapMappingManifest
  - 5 VerifyOrchestratorMappingTests pass (473 total tests pass)

**Status:** SATISFIED

---

## MAP-01: Per-STIG SCAP Mapping

**Plans:** 03-03

### Test Evidence

- `tests/STIGForge.UnitTests/Services/ScapMappingManifestTests.cs`
  - `SingleBenchmarkPerStig_ProducesConsistentMapping` (lines 14-70) - verifies winner selection and no cross-STIG mapping
  - `UnmappedControls_HaveNoScapMappingReason` (lines 72-100) - verifies all controls unmapped when no candidates exist
  - `NoCrossStigFallback_EnforcedWhenWinnerSelected` (lines 102-152) - verifies controls from different benchmarks are not cross-mapped
  - `MappingConfidence_MatchesMethod` (lines 154-190) - verifies confidence values: BenchmarkOverlap=1.0, Unmapped=0.0

### Source Evidence

- `src/STIGForge.Core/Models/ScapMappingManifest.cs`
  - `ScapMappingMethod` enum (lines 6-11): BenchmarkOverlap, StrictTagMatch, Unmapped
  - `ScapControlMapping` class (lines 13-22): VulnId, RuleId, BenchmarkId, Method, Confidence, Reason
  - `ScapMappingManifest` class (lines 24-36): StigPackId, SelectedBenchmarkPackId, ControlMappings, UnmappedCount computed property

- `src/STIGForge.Core/Services/CanonicalScapSelector.cs`
  - `BuildMappingManifest(input, controls)` method - produces per-STIG mapping:
    - BenchmarkOverlap (confidence=1.0): control's BenchmarkId matches winner's benchmark IDs
    - StrictTagMatch (confidence=0.7): control's RuleId matches winner's benchmark IDs
    - Unmapped (confidence=0.0): no match, reason="no_scap_mapping"
    - No candidates: all controls are Unmapped

- `src/STIGForge.Build/BundleBuilder.cs`
  - Optional `CanonicalScapSelector` constructor parameter (line 17)
  - Writes `scap_mapping_manifest.json` to `Manifest/` directory (lines 129-149)

- `src/STIGForge.Build/BundleModels.cs`
  - `ScapCandidates` on `BundleBuildRequest` (lines 17-21)
  - `ScapMappingManifestPath` on `BundleBuildResult` (line 53)

### Commit Evidence

- Commit `fcebf8d` (03-03-SUMMARY.md)
  - ScapMappingManifest model with ScapMappingMethod enum
  - BuildMappingManifest in CanonicalScapSelector
  - BundleBuilder integration for scap_mapping_manifest.json
  - 4 ScapMappingManifestTests pass (5 existing CanonicalScapSelector tests still pass)

**Status:** SATISFIED

---

## Success Criteria Coverage

| Criterion | Plan(s) | How |
|---|---|---|
| Deterministic bundle compiler outputs `Apply/`, `Verify/`, `Manual/`, `Evidence/`, `Reports/`, `Manifest/` tree | 03-01 | SchemaVersion=1, BuildTime.Seed()/Reset() for deterministic timestamps, ValidateApplyTemplates(), file_hashes.sha256 via IHashingService |
| Apply preflight enforces elevation/compatibility/reboot/PowerShell safety checks | 03-02 | Preflight.ps1 extended with Test-PowerStigAvailable, Test-DscResources, Test-MutualExclusion; PreflightRunner C# wrapper |
| Apply supports PowerSTIG/DSC primary backend, optional GPO/LGPO path, and script fallback with reboot-aware convergence | 03-02, 03-04 | LgpoRunner for Machine/User scope, MaxReboots=3 in RebootCoordinator, ConvergenceStatus enum, RebootCount tracking |
| Verify wrappers normalize SCAP/SCC and Evaluate-STIG outputs into canonical result model | 03-05 | NormalizedVerifyResult with RawArtifactPath and BenchmarkId, all adapters set provenance fields, ApplyMappingManifest enrichment |
| Strict per-STIG SCAP mapping contract is enforced | 03-03 | ScapMappingManifest with ScapMappingMethod enum, BuildMappingManifest with no cross-STIG fallback, UnmappedCount computed property |

---

## Plan Structure

| Plan | Wave | Depends On | Tasks | Requirement |
|---|---|---|---|---|
| 03-01 | 1 | none | 4 | BLD-01 |
| 03-02 | 1 | none | 4 | APL-01 |
| 03-03 | 1 | none | 4 | MAP-01 |
| 03-04 | 1 | none | 4 | APL-02 |
| 03-05 | 1 | none | 4 | VER-01 |

---

## Wave Execution Order

- **Wave 1** (parallel): 03-01 + 03-02 + 03-03 + 03-04 + 03-05 - all plans are independent

---

## Checks Performed

- [x] All five requirements (BLD-01, APL-01, APL-02, VER-01, MAP-01) appear in at least one plan's `requirements` field
- [x] All success criteria are traceable to specific plan tasks
- [x] Wave dependencies are correct (all wave 1, no dependencies)
- [x] No circular dependencies
- [x] All plans have valid frontmatter, must_haves (truths + artifacts + key_links), tasks, verification, and success_criteria
- [x] File paths in files_modified are consistent with codebase structure
- [x] Task actions reference correct existing classes and follow established patterns
- [x] Tests cover all new functionality:
  - BundleBuilderDeterminismTests: 3 tests
  - PreflightRunnerTests: 7 tests
  - ScapMappingManifestTests: 4 tests
  - LgpoRunnerTests: 4 tests
  - ApplyConvergenceTests: 7 tests
  - VerifyOrchestratorMappingTests: 5 tests
  - **Total: 30 tests for Phase 3 requirements**

---

## VERIFICATION PASSED

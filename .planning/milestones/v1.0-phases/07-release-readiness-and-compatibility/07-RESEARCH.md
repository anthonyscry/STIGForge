# Phase 07: Release Readiness and Compatibility - Research

**Date:** 2026-02-08
**Status:** Complete

## Objective

Identify what is already implemented for release readiness and compatibility in STIGForge, where evidence is weak for RC confidence, and what planning should prioritize for Phase 07.

## Current Strengths (Confirmed)

- Release/security gating is already operational with machine-readable artifacts:
  - `.github/workflows/ci.yml`
  - `.github/workflows/release-package.yml`
  - `tools/release/Invoke-ReleaseGate.ps1`
  - `tools/release/Invoke-SecurityGate.ps1`
- Packaging pipeline already emits manifest + checksums and supports optional signing:
  - `tools/release/Invoke-PackageBuild.ps1`
- Cross-environment smoke workflow exists for target Windows labels:
  - `.github/workflows/vm-smoke-matrix.yml`
- Integrity and deterministic export behavior have concrete tests:
  - `tests/STIGForge.UnitTests/Export/EmassExporterConsistencyTests.cs`
  - `tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs`
- Diff/rebase compatibility foundations are implemented and tested:
  - `src/STIGForge.Core/Services/BaselineDiffService.cs`
  - `src/STIGForge.Core/Services/OverlayRebaseService.cs`
  - `tests/STIGForge.UnitTests/Services/BaselineDiffServiceTests.cs`
  - `tests/STIGForge.UnitTests/Services/OverlayRebaseServiceTests.cs`

## Gaps Relevant to Phase 07 Scope

1. Fixture corpus breadth is still limited and mostly handcrafted:
   - `tests/STIGForge.UnitTests/fixtures/*`
   - `tests/STIGForge.IntegrationTests/fixtures/*`
2. No dedicated quarterly regression pack runner/workflow yet (roadmap explicitly calls for this).
3. Compatibility matrix behavior exists in importer output, but contract coverage is not yet treated as an explicit release gate:
   - `src/STIGForge.Content/Import/ContentPackImporter.cs` (`compatibility_matrix.json`)
4. End-to-end reproducibility guardrails are partial:
   - deterministic build props exist, but lockfile/locked restore enforcement is not yet fully integrated into release flow.
5. Ship checklist exists, but release-candidate documentation lock (including explicit upgrade/rebase validation evidence) is not yet formalized as a gated artifact set.

## Research-Backed Recommendations

- Enforce dependency lock files and locked restore in RC/release gates.
- Treat compatibility/regression packs as immutable manifests per quarter.
- Make golden/snapshot refresh explicit and reviewable (never implicit in normal test runs).
- Keep strict deterministic behavior in offline mode and produce explicit drift reports.
- Tie release sign-off to reproducibility artifacts (checksums, summary report, SBOM/dependency inventory, compatibility report, migration docs).

## Planning Inputs for Phase 07

- Favor four plans with clear dependency waves:
  - Wave 1: fixture/compatibility contract baseline
  - Wave 2: soak stability + quarterly regression/drift in parallel
  - Wave 3: release checklist enforcement + reproducibility/doc lock
- Keep scope strictly to roadmap:
  - fixture corpus expansion
  - quarterly compatibility regression + drift detection
  - final release checklist + reproducibility + docs lock

## References Used

- Internal project evidence:
  - `.github/workflows/ci.yml`
  - `.github/workflows/release-package.yml`
  - `.github/workflows/vm-smoke-matrix.yml`
  - `tools/release/Invoke-ReleaseGate.ps1`
  - `tools/release/Invoke-PackageBuild.ps1`
  - `docs/release/ShipReadinessChecklist.md`
  - `ROADMAP_EXECUTION.md`
- External guidance captured by librarian agents:
  - .NET deterministic/CI build and restore lock practices
  - NuGet signed package verification and offline revocation mode
  - SBOM artifact generation/validation as release evidence

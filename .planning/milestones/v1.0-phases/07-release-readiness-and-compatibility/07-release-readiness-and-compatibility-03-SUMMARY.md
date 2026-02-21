---
phase: 07-release-readiness-and-compatibility
plan: 03
status: completed
completed_at: 2026-02-08
commits: []
---

# Plan 03 Summary

Implemented quarterly regression pack execution and integrated drift evidence into release/security gate flows.

## What Was Built

- Added deterministic quarterly regression manifest at `tools/release/quarterly-regression-pack.psd1`.
- Added deterministic quarterly runner at `tools/release/Run-QuarterlyRegressionPack.ps1` with:
  - Manifest validation
  - Fixture hashing/profile extraction
  - Baseline and drift threshold enforcement
  - Machine-readable summary and drift artifacts
- Added operator documentation in `docs/release/QuarterlyRegressionPack.md`.
- Integrated quarterly pack handling into `tools/release/Invoke-ReleaseGate.ps1`:
  - Run/skip switch (`-SkipQuarterlyRegressionPack`)
  - Quarterly summary/report propagation into release-gate artifacts
- Integrated quarterly drift context into `tools/release/Invoke-SecurityGate.ps1`:
  - Optional summary/report ingestion
  - Security summary/report now includes quarterly compatibility signal
- Updated release workflow input/behavior in `.github/workflows/release-package.yml`:
  - Added `run_quarterly_pack` input
  - Threaded skip behavior to release gate invocation
- Cleared cross-target release-gate blockers discovered during verification:
  - `src/STIGForge.Content/Import/ScapBundleParser.cs` and `src/STIGForge.Content/Import/ContentPackImporter.cs` (`net48` string API compatibility)
  - `src/STIGForge.Apply/ApplyRunner.cs` (resume context null-coalescing type mismatch)
  - `tests/STIGForge.IntegrationTests/Content/RoundTripTests.cs` (fixture path/name alignment with phase-07 compatibility corpus)
  - `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs` and `tests/STIGForge.UnitTests/Apply/RebootCoordinatorTests.cs` (case-insensitive assertion compatibility)

## Verification

- Passed:
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File ./tools/release/Run-QuarterlyRegressionPack.ps1 -PackPath ./tools/release/quarterly-regression-pack.psd1 -OutputRoot ./.artifacts/quarterly-pack/phase07-resume-verify`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File ./tools/release/Invoke-SecurityGate.ps1 -OutputRoot ./.artifacts/security-gate/phase07-resume -QuarterlyDriftSummaryPath ./.artifacts/quarterly-pack/phase07-resume-verify/quarterly-pack-summary.json -QuarterlyDriftReportPath ./.artifacts/quarterly-pack/phase07-resume-verify/quarterly-pack-report.md`
  - `pwsh -NoProfile -ExecutionPolicy Bypass -File ./tools/release/Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot ./.artifacts/release-gate/phase07-quarterly-resume-5`

## Notes

- Quarterly drift artifacts are now first-class release/security evidence.
- Release gate now passes on this host with quarterly drift artifacts and SBOM evidence included.

# Quarterly Compatibility Regression Pack

The quarterly regression pack is a deterministic compatibility evidence set used by release readiness gates.

## Why this exists

- Quarterly STIG/SCAP/GPO updates can change parser behavior and fixture characteristics.
- Release promotion needs explicit compatibility drift evidence, not ad-hoc checks.
- The release gate now consumes machine-readable quarterly drift results as a first-class signal.

## Manifest

- Path: `tools/release/quarterly-regression-pack.psd1`
- Format: PowerShell data file loaded with `Import-PowerShellDataFile`
- Core fields:
  - `SchemaVersion`: manifest schema identifier.
  - `PackId` / `Quarter`: pack identity and time bucket.
  - `BaselineLabel`: immutable baseline reference name.
  - `DriftPolicy`: release policy (`FailOnMissingFixture`, `FailOnBaselineMismatch`, warning budget, thresholds).
  - `Fixtures`: deterministic list of fixtures to fingerprint and optionally compare against a baseline scenario.

Each fixture can define:

- `Scenario`, `Format`, `Path`
- `Baseline` (`Sha256`, `SizeBytes`, `RootElement`) for immutable fixtures
- `CompareAgainstScenario` + `Thresholds` for quarterly drift checks
- `ExpectedHashChange` for expected quarterly delta behavior

## Runner

- Script: `tools/release/Run-QuarterlyRegressionPack.ps1`
- Inputs:
  - `-PackPath` (defaults to `tools/release/quarterly-regression-pack.psd1`)
  - `-OutputRoot` (defaults to `.artifacts/quarterly-pack/<timestamp>`)
  - `-RepositoryRoot` (defaults to repo root)

The runner is deterministic:

- Normalizes paths from repository root.
- Computes fixture fingerprints (`sha256`, size bytes, line count, XML root element).
- Enforces immutable baseline references for baseline fixtures.
- Computes drift deltas for quarterly fixtures (size and line deltas).
- Applies policy-driven pass/warn/fail outcomes.

## Artifacts

Runner outputs in `-OutputRoot`:

- `quarterly-pack-summary.json`: machine-readable decision summary consumed by release/security gates.
- `quarterly-pack-drift.json`: per-fixture drift detail and findings.
- `quarterly-pack-report.md`: human-readable report.

## Release-gate integration

- `tools/release/Invoke-ReleaseGate.ps1` executes the quarterly runner by default.
- Quarterly outputs are included in release-gate report + summary artifacts.
- Pack policy failures block release-gate success.
- Warnings are captured explicitly for promotion review.

## Workflow enforcement

- `release-package.yml` validates quarterly summary/report presence and requires `overallPassed=true` with `decision=pass` when quarterly regression is enabled.
- `vm-smoke-matrix.yml` validates quarterly summary/report presence and requires `overallPassed=true` with `decision=pass` per VM runner.
- Promotion is blocked if quarterly trend artifacts are missing or indicate non-passing drift status.

## Security-gate integration

- `tools/release/Invoke-SecurityGate.ps1` accepts quarterly summary/report paths.
- Security summary/report include quarterly compatibility drift context as review evidence.
- Security gate deterministic semantics remain unchanged; compatibility context is additive.

## Example command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Run-QuarterlyRegressionPack.ps1 `
  -PackPath .\tools\release\quarterly-regression-pack.psd1 `
  -OutputRoot .\.artifacts\quarterly-pack\phase07
```

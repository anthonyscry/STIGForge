---
plan: 03-02
status: complete
commit: cbf7516
---

## Summary

Plan 03-02 (APL-01: Preflight Hardening) is complete.

### Changes

1. **Extended Preflight.ps1** (`tools/apply/Preflight/Preflight.ps1`): Added three new check functions while preserving all existing checks:
   - `Test-PowerStigAvailable`: Verifies PowerSTIG module can be imported (only runs when `-PowerStigModulePath` provided).
   - `Test-DscResources`: Parses PowerSTIG manifest for required DSC resource modules and verifies availability/version.
   - `Test-MutualExclusion`: Reads bundle manifest and pack controls to detect controls with both DSC and LGPO remediation targets.
   - Script now outputs JSON to stdout via `ConvertTo-Json` and exits with code 0 (ok) or 1 (issues).

2. **PreflightRunner C# wrapper** (`src/STIGForge.Apply/PreflightRunner.cs`): Invokes Preflight.ps1 via `Process.Start`, captures stdout/stderr, parses JSON output with graceful fallback for parse failures, timeouts, and missing scripts.

3. **Models** (`src/STIGForge.Apply/ApplyModels.cs`): Added `PreflightRequest` and `PreflightResult` classes.

4. **InternalsVisibleTo** (`src/STIGForge.Apply/STIGForge.Apply.csproj`): Exposed internal `ParseResult` for direct testing.

5. **Tests** (`tests/STIGForge.UnitTests/Apply/PreflightRunnerTests.cs`): 7 tests covering missing script, valid JSON, JSON with issues, exit code override, invalid JSON fallback, and empty output scenarios.

### Verification

- All 7 PreflightRunner tests pass.
- Full test suite passes (no regressions).

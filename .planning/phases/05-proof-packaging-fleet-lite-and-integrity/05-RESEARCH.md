# Phase 5: Proof Packaging, Fleet-lite, and Integrity - Research

**Researched:** 2026-02-22
**Status:** Complete

## Domain Summary

Phase 5 closes the final three requirements (EXP-01, FLT-01, AUD-01) by hardening existing export, fleet, and audit infrastructure. The codebase already has substantial implementations; this phase adds missing capabilities identified in CONTEXT.md decisions.

## Existing Infrastructure Analysis

### Export Layer (EXP-01 gaps)

**What exists:**
- `EmassExporter.ExportAsync` — full eMASS package pipeline (7 subdirectories, manifest, hash manifest, POA&M via PoamGenerator, attestations via AttestationGenerator, validation via EmassPackageValidator)
- `CklExporter.ExportCkl` — CKL/CKLB export from bundle verification results
- `StandalonePoamExporter.ExportPoam` — standalone POA&M CLI command
- `EmassPackageValidator` — validates directory structure, required files, file hashes, cross-artifact consistency (index vs POA&M vs attestations)
- CLI: `export-poam`, `export-ckl` commands registered

**What's missing (from CONTEXT.md decisions):**
1. **Export determinism** — JSON exports don't use sorted keys consistently (JsonSerializerOptions lacks property name sorting). CSV row ordering by ControlId is partially implemented but not guaranteed across all exporters. Timestamps use `DateTimeOffset.Now` per-file instead of a fixed export-start time.
2. **Manifest enhancements** — `manifest.json` lacks `fileCount`, `totalHash` (hash of file_hashes.sha256 itself), and `submissionReadiness` block (`allControlsCovered`, `evidencePresent`, `poamComplete`, `attestationsComplete`).
3. **Sorted hash manifest** — `WriteHashManifestAsync` already sorts alphabetically by path (good), but the packageHash (SHA-256 of file_hashes.sha256) is not computed or written to manifest.json.
4. **EmassPackageValidator** lacks submission readiness checks and readiness checklist in the validation report.
5. **Attestation ingestion** — `import-attestations` CLI command to merge filled CSV back into attestation JSON does not exist.
6. **CLI `export-emass` command** — not registered (EmassExporter exists but has no CLI command; only export-poam and export-ckl exist).

### Fleet Layer (FLT-01 gaps)

**What exists:**
- `FleetService` — parallel WinRM execution across targets, semaphore-limited concurrency, timeout handling, credential resolution
- `FleetCommands` — `fleet-apply`, `fleet-verify`, `fleet-status`, `fleet-credential-save/list/remove`
- Models: FleetTarget, FleetRequest, FleetResult, FleetMachineResult, FleetStatusResult

**What's missing (from CONTEXT.md decisions):**
1. **Artifact collection** — `FleetService.CollectArtifactsAsync()` does not exist. After fleet-apply/verify, artifacts need to be pulled from remote hosts via WinRM Copy-Item into `fleet_results/{hostname}/` directories.
2. **Per-host CKL generation** — no per-host CKL export from collected fleet artifacts.
3. **Fleet summary service** — `FleetSummaryService` does not exist. Need aggregation of per-host results into consolidated report: compliance percentages, control status matrix (controls x hosts), fleet-wide failing controls.
4. **Fleet summary outputs** — fleet_summary.json, fleet_summary.csv, fleet_summary.txt not generated.
5. **Fleet POA&M aggregation** — combined POA&M with host column.
6. **CLI `fleet-summary`** — command does not exist.

### Audit/Integrity Layer (AUD-01 gaps)

**What exists:**
- `AuditTrailService` — SQLite-backed with SHA-256 hash chaining, RecordAsync, QueryAsync, VerifyIntegrityAsync
- `AuditCommands` — `audit-log` (query/export) and `audit-verify` (chain integrity check)
- EmassExporter already records audit entries for export operations
- Hash chaining: each entry's hash includes previous entry's hash (genesis seed)

**What's missing (from CONTEXT.md decisions):**
1. **Fleet audit entries** — FleetService does not record audit entries for fleet-apply, fleet-verify, fleet-status operations. The FleetCommands handlers instantiate `new FleetService()` directly without passing IAuditTrailService.
2. **Attestation acceptance tracking** — no audit entry when operator marks attestation as complete (attestation import should record).
3. **Package-level SHA-256** — single hash of file_hashes.sha256 written to manifest.json as `packageHash` does not exist.
4. **Validation report submission readiness** — EmassPackageValidator does not check submissionReadiness flags or produce a submission readiness checklist.

## Key Architectural Observations

1. **EmassExporter** is instance-based (takes IPathBuilder, IHashingService, IAuditTrailService? in constructor). FleetService only takes ICredentialStore?.
2. **CklExporter** is static — no DI. Can be called directly for per-host CKL generation.
3. **AuditTrailService** requires IAuditTrailService interface — FleetService needs this added as optional constructor parameter.
4. **All existing exporters** use `DateTimeOffset.Now` freely — determinism requires passing a fixed export timestamp.
5. **ExportView.xaml.cs and FleetView.xaml.cs** are minimal stubs — WPF visibility is lightweight for this phase.

## Plan Decomposition Strategy

Based on the gaps and dependencies:

1. **Plan 05-01** (Wave 1): Export determinism and manifest enhancement — fix sorted keys, fixed timestamps, packageHash, submissionReadiness block, enhanced validator. Also add `export-emass` CLI command and `import-attestations` CLI command. This is EXP-01 core.

2. **Plan 05-02** (Wave 1): Fleet artifact collection and summary service — CollectArtifactsAsync, per-host CKL, FleetSummaryService with control status matrix, fleet_summary outputs, fleet POA&M aggregation, `fleet-summary` CLI command. This is FLT-01 core.

3. **Plan 05-03** (Wave 2, depends on 05-01 and 05-02): Audit completeness and package integrity — fleet audit entries, attestation acceptance tracking, package-level SHA-256 verification in validator, submission readiness checklist. This is AUD-01 core.

4. **Plan 05-04** (Wave 2, depends on 05-01 and 05-02): WPF integration — submission readiness display in ExportView, fleet summary display in FleetView.

## Risk Assessment

- **WinRM testing**: Fleet artifact collection depends on WinRM which can't be tested without Windows remote targets. Tests should mock the PowerShell execution layer.
- **Determinism scope**: CONTEXT.md explicitly states "no bit-for-bit reproducibility across different machines" — determinism is within same inputs on same host. This limits the testing burden.
- **Existing test coverage**: EmassExporter, CklExporter, PoamGenerator, AttestationGenerator, AuditTrailService all have unit and integration tests. Changes must not break existing tests.

## RESEARCH COMPLETE

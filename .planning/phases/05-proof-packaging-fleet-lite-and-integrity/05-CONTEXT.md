# Phase 5: Proof Packaging, Fleet-lite, and Integrity - Context

**Gathered:** 2026-02-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver defensible export packages (CKL, POA&M, eMASS) with integrity proofs, fleet-lite WinRM operations with host-separated artifacts and unified summary, and complete hash-chained audit verification. This phase hardens existing export infrastructure (CklExporter, EmassExporter, FleetService, AuditTrailService) by closing gaps in export determinism, fleet artifact collection, and audit completeness. This is the final phase — its outputs are the defensible artifacts operators submit for compliance review.

</domain>

<decisions>
## Implementation Decisions

### Export Determinism Guarantees
- All JSON exports use sorted keys and consistent formatting (JsonSerializerOptions with WriteIndented + PropertyNamingPolicy)
- CSV exports use deterministic row ordering by ControlId (VulnId sort key)
- Timestamps in export metadata use fixed export-start time, not per-file write time
- file_hashes.sha256 entries sorted alphabetically by relative path for reproducible manifest
- No bit-for-bit reproducibility guarantee across different machines (machine name, user name vary) — determinism is within same inputs on same host
- Manifest.json enhanced with file count, total hash, and submission readiness flags

### Fleet Artifact Collection
- After fleet-apply/fleet-verify, artifacts are pulled from remote hosts via WinRM Copy-Item
- Per-host artifact directory: `fleet_results/{hostname}/` containing apply_run.json, verify results, evidence
- FleetService.CollectArtifactsAsync() pulls from configurable remote bundle root
- Artifact pull is best-effort — failure to collect from one host doesn't block others
- Per-host CKL generation: run CklExporter per host using collected artifacts
- No per-host eMASS package — unified fleet eMASS package contains all hosts

### Unified Fleet Summary
- FleetSummaryService aggregates per-host results into consolidated report
- Summary includes: per-host compliance percentage, fleet-wide control status matrix, failing controls by host
- Output formats: fleet_summary.json (structured) + fleet_summary.csv (tabular) + fleet_summary.txt (human-readable)
- Control status matrix: rows = controls, columns = hosts, cells = Pass/Fail/NA/NR
- Fleet POA&M aggregation: one combined POA&M with host column indicating which hosts are affected
- CLI: `fleet-summary --results-dir <path> --output <path> --json`

### Audit Trail Completeness
- FleetService records audit entries for each fleet operation (fleet-apply, fleet-verify, fleet-status)
- Per-host operation results recorded as individual audit entries with machine name in target field
- Attestation acceptance tracked as audit entry when operator marks attestation as complete
- Export operations already record to audit trail — no changes needed there
- No attestation signature/approval workflow for v1 — attestation templates are filled externally and included in package
- Attestation ingestion: `import-attestations --package <path> --file <csv>` merges filled CSV back into attestation JSON

### Package Integrity Enhancement
- Manifest.json gains submissionReadiness block: allControlsCovered, evidencePresent, poamComplete, attestationsComplete
- Package-level SHA-256: single hash of file_hashes.sha256 itself, written to manifest.json as packageHash
- EmassPackageValidator extended to check submissionReadiness flags and warn on incomplete items
- No cryptographic signing for v1 — SHA-256 chain integrity is sufficient for offline compliance tool
- Validation report includes submission readiness checklist with pass/fail per criterion

### Claude's Discretion
- Exact fleet artifact pull retry/timeout behavior
- Fleet summary CSV column formatting and header names
- Whether attestation import validates CSV column headers or is lenient
- How to handle partial fleet artifact collection in the unified summary (footnotes vs separate section)
- Test strategy for WinRM-dependent fleet operations (mock vs skip)

</decisions>

<specifics>
## Specific Ideas

- The fleet summary control status matrix is the "money view" for operators managing multiple hosts — it answers "which controls are failing where" at a glance
- Attestation ingestion is intentionally simple (CSV import) because attestations are typically filled in Excel by system owners, not in STIGForge
- Package-level SHA-256 of the hash manifest creates a single verifiable fingerprint for the entire submission
- Fleet artifact collection should be resilient — one unreachable host shouldn't prevent reporting on the rest
- Submission readiness flags give operators a clear "go/no-go" signal before sending the package to assessors

</specifics>

<deferred>
## Deferred Ideas

- Cryptographic signing of export packages (PKI, GPG) — future compliance enhancement
- eMASS API upload integration — explicitly out of scope for v1
- Real-time fleet monitoring dashboard — beyond v1-lite scope
- Attestation approval workflow with dual-review — future compliance enhancement
- Fleet inventory management (auto-discover hosts) — beyond v1-lite scope
- Differential export (only changed controls since last export) — future optimization

</deferred>

---

*Phase: 05-proof-packaging-fleet-lite-and-integrity*
*Context gathered: 2026-02-22*

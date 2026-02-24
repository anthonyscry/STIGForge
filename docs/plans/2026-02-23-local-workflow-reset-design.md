# Local Workflow Reset Design (Setup -> Import -> Scan)

Date: 2026-02-23
Status: Approved

## Objective

Redesign STIGForge around a local-machine-first workflow that starts from deterministic setup, discovers and normalizes local import content, and runs baseline scanning with Evaluate-STIG.

v1 scope is intentionally limited to:
- Setup
- Import
- Scan

Harden and Verify remain planned next phases.

## Confirmed Decisions

1. Use a Pipeline Orchestrator approach with explicit stage contracts.
2. Ship v1 as Setup + Import + Scan only.
3. Use a normalized internal JSON artifact as the primary output.
4. Enforce strict setup gating for required tool paths.
5. Treat imported STIG content as authoritative checklist truth.
6. Treat Evaluate-STIG and SCC results as evidence overlays mapped onto imported checklist items.
7. Keep pipeline successful on unmapped scanner items, but retain and surface them as high-visibility warnings.

## Architecture

The workflow is a local orchestrator with ordered stages:

1. Setup
2. Import
3. Scan

Each stage has typed input/output contracts, deterministic diagnostics, and explicit artifact references. Stage output is resumable and does not require rerunning successful prior stages after a downstream failure.

The orchestrator emits a single mission artifact (`mission.json`) that is the authoritative data contract for downstream phases and UI.

## Data Authority and Merge Model

### Canonical Authority

Imported STIG content defines canonical checklist entities (for example `stig_id`, `rule_id`, metadata, expected check context).

### Scanner Evidence

Evaluate-STIG (v1) and SCC (future Verify phase) are ingested as scanner evidence that maps onto canonical imported entities.

### Drift and Mapping

- If scanner content references older/newer revisions than imported content, retain scanner evidence and tag as `version_mismatch`.
- If scanner evidence cannot be mapped, retain it in `unmapped` and emit high-visibility warnings.
- Unmapped evidence does not fail the pipeline in v1.

## Stage Contracts

### Setup

- Applies defaults rooted to app path with `app/import` as baseline location.
- Resolves and validates required tool paths.
- Strict gate: missing/invalid required tool path blocks pipeline start.
- Persists resolved config snapshot used by all following stages.

### Import

- Scans import folder for STIG, SCC, GPO, ADMX, and tool artifacts.
- Classifies discovered content and records provenance/version metadata.
- Produces canonical checklist set used as merge authority.

### Scan (v1)

- Executes Evaluate-STIG baseline scan for local machine.
- Ingests findings and maps to imported canonical checklist.
- Emits mapping diagnostics, drift markers, and unmapped warning set.

## Normalized Output (`mission.json`)

`mission.json` includes:

- Canonical checklist derived from Import
- Per-item merged scanner evidence
- `unmapped` evidence collection
- Diagnostics (tool versions, mapping statistics, warnings, drift markers)
- Stage run metadata (timestamps, stage status, artifact paths)

No CKL export is required in v1. CKL generation will be added in later phases from normalized data.

## Failure Semantics

- Setup required-path validation failure: hard fail.
- Import failure to build canonical checklist: hard fail.
- Scan execution failure: fail stage with diagnostics and partial artifact references.
- Unmapped scanner evidence: warning, not failure.

## Testing Strategy

1. Stage contract tests for Setup/Import/Scan I/O schema stability.
2. Import tests for mixed input sets and canonical checklist generation.
3. Mapping tests for exact match, version mismatch, and unmapped behaviors.
4. Strict setup-gate tests for missing/invalid required tool paths.
5. Local integration test for full `Setup -> Import -> Scan` path and `mission.json` correctness.

## v1 Success Criteria

1. Local workflow executes end-to-end (`Setup -> Import -> Scan`).
2. Imported STIG content is enforced as canonical checklist authority.
3. Evaluate-STIG findings map into canonical checklist model.
4. Unmapped findings are retained and clearly warned.
5. Output is normalized internal JSON suitable for later Harden/Verify/CKL phases.

## Forward Path

Next phases extend the same normalized model:

- Harden: PowerSTIG DSC + local GPO + applicable ADMX processing.
- Verify: post-hardening Evaluate-STIG + SCC ingestion and consolidated evidence mapping.
- Export: single checklist outputs (for example CKL) derived from normalized mission data.

## Implemented v1 Code-Path Alignment (Task 7)

Current implementation behavior is aligned to this design as follows:

- CLI entry point is `workflow-local` and writes `mission.json` to `<output-root>/mission.json`.
- CLI defaults resolve to:
  - `--import-root` -> `.stigforge/import`
  - `--tool-root` -> `.stigforge/tools/Evaluate-STIG/Evaluate-STIG`
  - `--output-root` -> `.stigforge/local-workflow`
- Setup stage enforces strict required-tool gating before scan execution:
  - required script: `Evaluate-STIG.ps1`
  - missing/invalid tool root throws an `InvalidOperationException` and stops pipeline progression.
- Import stage remains authoritative for canonical checklist and hard-fails when checklist output is empty.
- Scan mapping retains unmapped findings as warning diagnostics and keeps pipeline successful; unmapped entries are persisted in mission output under `unmapped`.

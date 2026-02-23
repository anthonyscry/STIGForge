# STIGForge Next Architecture

## 1. Module Boundaries and Responsibilities

| Module | Responsibility | Inputs | Outputs |
|--------|----------------|--------|---------|
| `STIGForge.App` | WPF operator UX, manual workflows, diagnostics panels | Core services, run state, policy state | User actions, UI-driven orchestration requests |
| `STIGForge.Cli` | Scriptable automation and batch/fleet entrypoints | Config/profile args, bundle paths | Command execution results, machine-readable summaries |
| `STIGForge.Content` | Import scanner, parsers, artifact classification | Raw files/zips | Canonical pack artifacts + import diagnostics |
| `STIGForge.Core` | Canonical models, policy engine, invariants, mapping rules | Parsed content, profile/overlay settings | `ControlRecord` graph, applicability/mapping decisions |
| `STIGForge.Build` | Deterministic bundle compilation | Profile + pack selection + overlays + policy | Bundle directory + manifest + hash set |
| `STIGForge.Apply` | Hardening orchestration and preflight | Built bundle + host context | Apply execution records, convergence and rollback artifacts |
| `STIGForge.Verify` | Scanner wrappers and normalized result parsers | Verify config + scanner outputs | Canonical verification records + parser diagnostics |
| `STIGForge.Evidence` | Evidence recipe execution and metadata indexing | Control context + recipe definitions | Evidence artifacts + indexed metadata |
| `STIGForge.Export` | CKL/POA&M/eMASS package generation | Canonical run state + evidence index | Deterministic export tree + index + hash report |
| `STIGForge.Reporting` | Human/machine summary generation and diff views | Canonical records + exports + audit | Dashboards, reports, change summaries |
| `STIGForge.Infrastructure` | Storage, process execution, filesystem, WinRM, scheduler | Requests from domain modules | IO abstractions, run telemetry, error handling |
| `STIGForge.Shared` | Shared constants, enums, version contracts | N/A | Reused type-safe contracts |

## 2. Interface Contracts (Primary)

## 2.1 Content to Core

- `ImportArtifactDescriptor`
- `ContentPack`
- `ControlRecord`
- `ImportDiagnostics`

Contract requirements:
- includes artifact kind confidence + reason code
- includes source provenance + hashes

## 2.2 Core to Build/Apply/Verify

- `Profile`
- `Overlay`
- `BundleManifest`
- `MappingDecision`

Contract requirements:
- deterministic ordering metadata
- policy evaluation trace fields

## 2.3 Verify to Export/Reporting

- `VerificationResult`
- `EvidenceRecord`
- `ExportIndexEntry`

Contract requirements:
- stable control IDs
- source artifact links
- timestamps and tool versions

## 3. Explicit Data Flow

```text
Import
  -> Parse/Classify (Content)
  -> Normalize (Core)
  -> Build Bundle (Build)
  -> Apply Hardening (Apply)
  -> Verify Scans (Verify)
  -> Manual Responses + Evidence (App/Evidence)
  -> Export Package + Reports (Export/Reporting)
```

Every step emits auditable artifacts with provenance and deterministic ordering constraints.

## 4. Critical Invariants

## 4.1 Strict per-STIG SCAP Association

Invariant:

1. Benchmark overlap is primary.
2. Deterministic tie-break if multiple candidates remain.
3. Fallback can only use strict per-STIG compatibility:
   - feature SCAP requires feature overlap with that STIG
   - generic SCAP requires explicit OS overlap
4. Ambiguity or insufficient signal -> review-required.
5. Never broad-assign one SCAP candidate across unrelated STIG rows.

## 4.2 Deterministic Output Contract

Invariant:

- Identical inputs (pack/profile/overlay/tool versions/policy) produce equivalent structure/index ordering/hash behavior according to configured timestamp policy.
- Sorting/order rules are versioned in manifest metadata.

## 5. Dependency Rules

- `Core` cannot depend on UI or infrastructure-specific types.
- `Build/Apply/Verify/Export` consume contracts from `Core`/`Shared` only.
- `App` and `Cli` are composition roots; domain logic remains in modules.
- `Infrastructure` provides adapters behind interfaces; business rules remain outside.

## 6. Error Handling and Diagnostics

- Every boundary returns typed diagnostics with reason codes.
- Ambiguity is explicit state, not silent fallback.
- Operator-visible diagnostics required for import, mapping, verify, and export.

## 7. Security and Integrity Controls

- SHA-256 integrity for all bundle/export artifacts.
- Audit trail for all critical lifecycle actions.
- Explicit elevation checks before apply/fleet operations.
- Offline gate checks to prevent hidden internet dependencies.

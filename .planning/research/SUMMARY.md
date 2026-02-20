# Project Research Summary

**Project:** STIGForge Next
**Domain:** Offline-first Windows compliance hardening and evidence packaging
**Researched:** 2026-02-19
**Confidence:** HIGH

## Executive Summary

STIGForge Next is best built as an offline-first, deterministic Windows hardening platform with a single workflow engine exposed through both WPF and CLI. The research converges on a modular monolith in .NET 8 (LTS), with strict canonical contracts (`ControlRecord`, profile/overlay policy, verification/export schemas) and explicit module boundaries across content ingest, build, apply, verify, evidence, and export.

The recommended approach is to front-load schema and policy contract design, then implement deterministic build/apply/verify foundations before UX polish or fleet expansion. Experts in this domain optimize for explainability and audit defensibility over convenience: strict per-STIG SCAP association, deterministic artifacts and manifests, append-only audit provenance, and review-required handling when confidence is ambiguous.

Key risks are silent mapping ambiguity, non-deterministic outputs, audit-chain breaks, and PowerShell/DSC drift during apply. Mitigation is clear and actionable: enforce hard mapping invariants, freeze and fingerprint execution inputs, gate releases with repeatability/golden-package tests, require indexed evidence/manifests, and implement strong preflight plus reboot-aware convergence/rollback from the start.

## Key Findings

### Recommended Stack

Use .NET 8 now for stability and support window alignment, with deterministic build settings and locked dependency graphs as non-negotiable quality gates. Keep runtime and tooling choices conservative to preserve offline operation, auditability, and reproducibility.

**Core technologies:**
- **.NET 8 + WPF + System.CommandLine:** single runtime for shared workflows with dual UX surfaces and parity.
- **SQLite + Microsoft.Data.Sqlite + Dapper:** portable local data store with explicit SQL control for deterministic/auditable behavior.
- **Out-of-process Windows PowerShell 5.1 (`powershell.exe`):** preserves module compatibility without risky in-proc runtime coupling.
- **Microsoft.Extensions DI/Logging:** standard composition and structured diagnostics across CLI/app/audit flows.
- **WiX MSI + SignTool:** deterministic enterprise deployment and trust-chain signing for offline environments.

**Critical version/pinning decisions:**
- Pin .NET SDK with `global.json`; patch monthly.
- Enable NuGet lock files and locked restore in CI.
- Record tool/version fingerprints in manifests and validate reproducibility by rebuilding same commit twice.

### Expected Features

Research identifies a clear MVP spine: canonicalized content ingest -> deterministic policy/build/apply/verify -> manual/evidence/export proving loop.

**Must have (table stakes):**
- Content ingestion/normalization across STIG/SCAP/GPO/LGPO/ADMX.
- Canonical control model and deterministic profile/overlay policy resolution.
- Deterministic build pipeline and guarded apply orchestration with preflight checks.
- Verify pipeline with scanner normalization and raw artifact retention.
- Manual check wizard, deterministic CKL/POA&M/eMASS-ready export, and audit/integrity proofing.
- WPF/CLI parity for core workflows.

**Should have (differentiators):**
- Strict per-STIG SCAP association contract.
- Release-age automation gates for newly released content.
- Scope filtering with confidence-based NA reporting.
- Evidence autopilot with control-level recipes.
- Reboot-aware convergence with rollback snapshots.

**Defer (v2+):**
- v1-lite fleet operations (WinRM at broader scale).
- Deep quarterly diff/rebase automation polish after single-host deterministic flow is stable.

### Architecture Approach

Adopt a contract-first modular monolith: Presentation (WPF/CLI) -> application facades -> domain modules (Content/Core/Build/Apply/Verify/Evidence/Export/Reporting) -> infrastructure adapters (filesystem, DB, process runner, WinRM, clock). Build order should follow dependency gravity: Core contracts first, then infrastructure ports, then Content/Build, then Apply+Verify, then Evidence+Export+Reporting, and finally WPF/CLI parity layer over already-stable workflows.

**Major components:**
1. **Core + Shared contracts** — canonical schemas, policy engine, invariants, and versioned DTO boundaries.
2. **Content + Build** — ingestion/classification/dedupe/provenance and deterministic bundle compiler/manifests.
3. **Apply + Verify** — preflight/enforcement convergence and scanner wrapper/normalization into canonical results.
4. **Evidence + Export + Reporting** — artifact indexing/checksums, submission package assembly, and diagnostics/diff/confidence reports.
5. **Infrastructure + Presentation** — adapters for OS/tooling dependencies and dual interfaces calling the same facades.

### Critical Pitfalls

1. **Silent STIG-to-SCAP ambiguity** — prevent with strict per-STIG mapping invariants, deterministic tie-breakers, and mandatory review queues for low confidence.
2. **Non-deterministic build/export outputs** — prevent with canonical ordering, timestamp normalization, pinned tool versions, and repeat-run checksum gates.
3. **Audit/evidence integrity gaps** — prevent with append-only event contracts, hash-chain validation, and mandatory SHA-256 manifests plus provenance checks.
4. **PowerShell/DSC state drift in apply** — prevent with robust preflight gates, bounded convergence loops, backend parity tests, and rollback snapshots.
5. **Export contract drift vs validators** — prevent with versioned schemas, golden-package fixtures, and release-time validator replay.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Contract Foundation and Ingestion
**Rationale:** Every downstream workflow depends on stable canonical schemas and deterministic content normalization.
**Delivers:** Versioned core contracts, parser registry/classification/dedupe/provenance, baseline local persistence.
**Addresses:** Table-stakes ingestion + canonical model + profile/overlay prerequisites.
**Avoids:** Canonical model underspecification and early mapping ambiguity.

### Phase 2: Deterministic Execution Core (Build/Apply/Verify)
**Rationale:** Build/apply/verify is the product value spine and must be deterministic before scaling features.
**Delivers:** Deterministic bundle compiler, preflight and convergence orchestration, scanner wrappers and normalization.
**Uses:** .NET 8, SQLite, PowerShell 5.1 out-of-proc, deterministic CI/lock-file controls.
**Implements:** Core + Build + Apply + Verify modules via hexagonal adapters.
**Avoids:** Non-determinism and PowerShell drift hidden by shallow gates.

### Phase 3: Human Loop and Proof Artifacts
**Rationale:** Manual checks and evidence are required to close unresolved controls and support defensible submissions.
**Delivers:** Manual wizard, evidence recipe engine/indexing, append-only audit chain validation.
**Addresses:** Table-stakes manual flow + audit/integrity proofing; differentiator evidence autopilot.
**Avoids:** Evidence bypass/unindexed artifacts and broken provenance chains.

### Phase 4: Deterministic Export and Compliance Packaging
**Rationale:** Export contract quality determines real-world acceptance of otherwise good technical runs.
**Delivers:** CKL/POA&M/eMASS-ready deterministic packages, strict indices/manifests/checksums, validator replay suite.
**Addresses:** Table-stakes export pipeline.
**Avoids:** Submission failures from schema/layout drift.

### Phase 5: Lifecycle and Scale Extensions
**Rationale:** Diff/rebase and fleet operations add complexity and should follow a stable single-host core.
**Delivers:** Confidence-scored rebase workflow, update workload reports, constrained WinRM fleet operations.
**Addresses:** Differentiators for quarterly updates and multi-host execution.
**Avoids:** Over-aggressive auto-carry and host variance blind spots.

### Phase Ordering Rationale

- Dependencies require canonical contracts before deterministic pipelines, and deterministic pipelines before evidence/export hardening.
- Architecture boundaries favor implementing domain modules first, then exposing them through WPF/CLI for parity.
- This order directly reduces top rewrite risks: schema churn, non-determinism, and export validator failures late in cycle.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 2:** Scanner wrapper behaviors and parser fixture corpus strategy across SCAP/SCC/Evaluate-STIG variants.
- **Phase 4:** Exact downstream validator compatibility matrix for CKL/POA&M/eMASS package acceptance.
- **Phase 5:** WinRM concurrency/retry limits and telemetry model for fleet variance detection.

Phases with standard patterns (can likely skip `/gsd-research-phase`):
- **Phase 1:** .NET modular-monolith + contract-first schema/versioning patterns are well-established.
- **Phase 3 (core mechanics):** Append-only audit logs, checksum manifests, and evidence indexing are standard implementation patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Backed by official Microsoft/.NET/SQLite docs with explicit version and lifecycle data; only System.CommandLine and WiX package pinning details are medium-confidence execution choices. |
| Features | HIGH | Directly aligned with internal project spec and roadmap constraints; dependency chain is explicit and coherent. |
| Architecture | HIGH | Strong internal consistency with module boundaries, data flow, and anti-pattern coverage tied to project principles. |
| Pitfalls | HIGH | Specific, testable failure modes with concrete prevention/detection signals and phase-linked warnings. |

**Overall confidence:** HIGH

### Gaps to Address

- **Exact package version pins at implementation start:** finalize stable versions for System.CommandLine, Dapper, WiX, and supporting libraries before first production lockfile baseline.
- **Validator fixture breadth:** curate representative real-world CKL/POA&M/eMASS validator fixtures early to prevent late export surprises.
- **Scanner variance corpus depth:** build and continuously expand fixture corpus for SCAP/SCC/Evaluate-STIG output edge cases.
- **PowerShell backend parity envelope:** define supported host/version matrix and explicit fallback semantics before fleet expansion.

## Sources

### Primary (HIGH confidence)
- `PROJECT_SPEC.md` and `.planning/PROJECT.md` — product scope, architecture baseline, principles, and milestone expectations.
- Microsoft .NET support policy and .NET/WPF docs — runtime support window and UI platform constraints.
- Microsoft docs for System.CommandLine, DI, Logging, Microsoft.Data.Sqlite, deterministic/CI MSBuild properties, and NuGet lock files.
- SQLite official docs (`whentouse`, WAL) — local storage fit and concurrency behavior.
- PowerShell official docs (5.1 vs 7, editions) — compatibility rationale for out-of-proc 5.1 integration.

### Secondary (MEDIUM confidence)
- WiX Toolset project/release documentation — MSI toolchain maturity and version selection.
- .NET Community Toolkit MVVM docs — presentation-layer productivity guidance.

### Tertiary (LOW confidence)
- None identified in current research set.

---
*Research completed: 2026-02-19*
*Ready for roadmap: yes*

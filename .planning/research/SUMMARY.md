# Project Research Summary

**Project:** STIGForge Next
**Domain:** Offline-first Windows STIG compliance orchestration
**Researched:** 2026-02-20
**Confidence:** MEDIUM-HIGH

## Executive Summary

STIGForge Next is a Windows-native compliance orchestration product, not just a scanner wrapper. The combined research converges on a deterministic mission loop (`Build -> Apply -> Verify -> Prove`) built as a pipeline-centric modular monolith with strict contracts between ingestion, apply, verification, normalization, and export. The strongest implementation path is .NET 8 LTS with WPF + CLI surfaces over shared workflow services, SQLite for offline evidence storage, and adapter-based integration with PowerSTIG/DSC and scanner tools.

The recommended approach is to lock canonical control/result contracts first, then implement the coordinator/planner and a single end-to-end execution path before broadening adapters or fleet scale. Experts consistently favor strict per-STIG mapping boundaries, explicit ambiguity states (`review_required`), and immutable provenance over convenience heuristics. This is the right tradeoff for audit-bound environments where reproducibility and explainability matter more than auto-magic remediation.

The top risks are false-confidence mapping, nondeterministic artifacts, parser drift from upstream scanner changes, and provenance loss in merged views. Mitigation is straightforward and should be hard-gated in roadmap phases: contract tests for mapping invariants, same-input/same-output regression fixtures, versioned parser fixture corpora, and mandatory source-link retention from every canonical finding to raw artifacts.

## Key Findings

### Recommended Stack

The stack recommendation is conservative by design: prioritize compatibility and deterministic offline behavior over novelty. The most critical choice is the dual-runtime PowerShell boundary (PowerSTIG through Windows PowerShell 5.1, modern automation utilities in PowerShell 7.4), isolated behind adapter interfaces.

**Core technologies:**
- `.NET 8 LTS` + `WPF` + `System.CommandLine` - shared Windows-native runtime for GUI and automation workflows.
- `PowerSTIG 4.28.0` via out-of-process `powershell.exe` 5.1 - preserves current DSC-era compatibility and checklist workflow support.
- `PowerShell 7.4 LTS` sidecar - modern scripting where legacy DSC semantics are not required.
- `SQLite` + `Microsoft.Data.Sqlite` + `Dapper` - single-file offline evidence/index store with explicit, auditable SQL.
- `Serilog` and schema-first XML parsing (`System.Xml.Linq`/`System.Xml.Schema`) - deterministic logs and strict XCCDF/ARF ingest.

**Critical version requirements:**
- Pin to .NET 8.0.x and lock builds with `global.json`.
- Pin PowerSTIG at known-good version and mirror modules into offline feed.
- Treat Evaluate-STIG integration as an adapter contract with extra validation due to source uncertainty.

### Expected Features

MVP scope is clear: baseline/profile management, safe apply orchestration, multi-scanner normalization/correlation, and audit-ready export with manual attestation. Differentiation should come from deterministic execution and explicit disagreement handling, not from cloud dependencies or aggressive auto-remediation.

**Must have (table stakes):**
- Baseline/profile management with version pinning and scope context.
- Apply orchestration with preflight checks, controlled execution, and failure capture.
- Multi-scanner ingest + normalization + strict control correlation.
- Manual attestation/waiver workflow with reviewer identity and history.
- Checklist/evidence export (CKL/XCCDF/CSV/POA&M-ready) with end-to-end traceability.

**Should have (competitive):**
- Deterministic mission loop with reproducible outputs and manifests.
- Multi-scanner consensus/conflict reasoning and operator resolution queue.
- Offline-first evidence packaging and deterministic replay.
- Guided remediation safety layers (dry-run, staged apply, rollback points).

**Defer (v2+):**
- Optional cloud sync/federation.
- Direct system-of-record API writeback (including eMASS API paths).

### Architecture Approach

Architecture should be a modular monolith organized by workflow stages and bounded contracts: run coordinator/planner, apply engine, verify hub, normalization engine, evidence index, export assembler, and audit integrity services. The keystone pattern is a canonical per-control result graph with provenance pointers; export must consume canonical records only, never scanner-native formats. Adapter-registry execution is preferred for both apply and verify integrations to isolate tool churn and support staged expansion.

**Major components:**
1. `Run Coordinator + Build Planner` - deterministic phase ordering, checkpointing, retries, and frozen manifests.
2. `Apply Engine + Verify Hub` - enforcement and multi-scanner collection through pluggable adapters.
3. `Normalization Engine` - canonical status crosswalk and provenance-linked control results.
4. `Evidence Index + Export Assembler` - deterministic package tree/manifests for audit handoff.
5. `Audit/Integrity Service` - append-only events plus hash/manifold proof of artifact integrity.

### Critical Pitfalls

1. **Cross-STIG fallback masquerading as coverage** - enforce strict mapping contracts and route uncertainty to `review_required`.
2. **Non-deterministic checklist assembly** - canonicalize input ordering and replace random IDs with deterministic IDs.
3. **Rule identity collisions across tools** - require composite identity keys including STIG/benchmark lineage and source.
4. **Parser coupling to one tool/version flavor** - separate extraction from normalization and maintain multi-version fixture suites.
5. **Merged UX that hides provenance** - preserve one-click trace links from each merged control to raw evidence and parser metadata.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Canonical Contracts and Ingestion Foundation
**Rationale:** All downstream apply/verify/export behavior depends on stable identity and policy contracts.
**Delivers:** Canonical schemas, profile/baseline model, parser contracts, raw artifact retention model.
**Addresses:** Baseline/profile management and identity collision prevention.
**Avoids:** Pitfall 3 (rule collisions) and early schema churn.

### Phase 2: Policy Gates and Deterministic Planning Core
**Rationale:** Safe execution needs policy decisions and frozen manifests before touching hosts.
**Delivers:** Run coordinator, deterministic build planner, ambiguity/review policy gates.
**Addresses:** Mission-loop backbone and ambiguity handling.
**Avoids:** Pitfall 6 (ambiguity auto-resolved) and premature auto-remediation behavior.

### Phase 3: Apply Engine (Single Backend) with Safety Controls
**Rationale:** Start with one reliable enforcement path before broad adapter expansion.
**Delivers:** PowerSTIG/DSC apply adapter, preflight checks, reboot-aware orchestration, failure capture.
**Uses:** .NET 8 + PowerShell 5.1 boundary + SQLite state.
**Avoids:** Uncontrolled remediation blast radius and nondeterministic apply flow.

### Phase 4: Verification Hub and Strict SCAP Association
**Rationale:** Multi-scanner value appears only when mapping is strict and disagreements are explicit.
**Delivers:** At least two verify adapters, canonical normalization, disagreement queue, mapping invariants.
**Addresses:** Multi-scanner ingestion/correlation and consensus visibility.
**Avoids:** Pitfall 1 (cross-STIG fallback) and Pitfall 4 (parser flavor coupling).

### Phase 5: Human Resolution, Provenance UX, and Evidence Index
**Rationale:** Audit defensibility requires manual closure flows and transparent evidence lineage.
**Delivers:** Manual attestation workflow, provenance-first findings UX, evidence indexing.
**Addresses:** Table-stakes attestation and traceability requirements.
**Avoids:** Pitfall 5 (provenance hidden in merged checklist view).

### Phase 6: Deterministic Export Packaging and Replay Gates
**Rationale:** Export acceptance is the delivery contract for compliance teams.
**Delivers:** CKL/XCCDF/CSV/POA&M-ready deterministic packages, manifest hashes, replay regression tests.
**Addresses:** Audit handoff outputs and deterministic proof loop.
**Avoids:** Pitfall 2 (non-deterministic checklist assembly).

### Phase Ordering Rationale

- Phase order follows hard dependencies from features and architecture: contracts -> planner/gates -> apply -> verify/normalize -> human/evidence -> export.
- Grouping keeps high-churn integration concerns (scanner adapters, parser variants) behind stable domain contracts.
- Determinism and provenance controls are introduced before scale features to prevent expensive late rework.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3:** Validate exact PowerSTIG/DSC execution semantics and rollback constraints in mixed host states.
- **Phase 4:** Scanner adapter contracts and SCC/Evaluate-STIG output-version matrix need fixture-driven research.
- **Phase 6:** External validator acceptance behavior for CKL/POA&M package variants should be tested early.

Phases with standard patterns (can likely skip `/gsd-research-phase`):
- **Phase 1:** Canonical schema/versioning and parser contract patterns are mature and well understood.
- **Phase 2:** Workflow coordinator/state-machine and deterministic planner patterns are established.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM | Strong official support/lifecycle evidence, but Evaluate-STIG integration path is still uncertain and should be adapter-gated. |
| Features | MEDIUM-HIGH | Table-stakes and dependency chain are coherent across multiple domain references; prioritization is opinionated but consistent. |
| Architecture | MEDIUM-HIGH | Patterns and sequencing are well-supported and internally consistent; implementation details still need project-specific validation. |
| Pitfalls | HIGH | Failure modes are concrete, testable, and directly mapped to prevention phases and warning signals. |

**Overall confidence:** MEDIUM-HIGH

### Gaps to Address

- **Evaluate-STIG source/contract ambiguity:** define exact upstream artifact and lock adapter contract tests in Phase 3/4.
- **Parser fixture breadth:** curate SCC/Evaluate output corpora across versions before broad scanner support.
- **Export validator matrix:** verify deterministic package compatibility against real downstream tooling early.
- **Fleet-scale policies:** defer concurrency/retry tuning and WinRM fan-out until post-MVP single-host determinism is proven.

## Sources

### Primary (HIGH confidence)
- Microsoft .NET support policy and WPF/Windows docs - runtime and platform decisions.
- PowerShell lifecycle docs and PowerSTIG Gallery/wiki docs - compatibility boundaries and checklist workflow.
- NIST SCAP/XCCDF/ARF specifications - canonical data and schema constraints.
- Microsoft DSC documentation (v1.1 and modern overview) - enforcement architecture context.

### Secondary (MEDIUM confidence)
- MITRE SAF and Heimdall READMEs - expected export/attestation ecosystem patterns.
- OpenSCAP manual and STIG Manager documentation - multi-source import/export and scan workflow practices.
- NuGet package pages (`System.CommandLine`, `Microsoft.Data.Sqlite`, `Dapper`, `CommunityToolkit.Mvvm`, `FluentValidation`, `Serilog`) - package version references.

### Tertiary (LOW confidence)
- Evaluate-STIG-equivalent distribution/contract details - incomplete official clarity; validate during implementation.

---
*Research completed: 2026-02-20*
*Ready for roadmap: yes*

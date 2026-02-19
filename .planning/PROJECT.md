# STIGForge Project Context

## Product Goal

STIGForge delivers an offline-first Windows hardening workflow that turns quarterly STIG content updates into a repeatable operator flow:

Build -> Apply -> Verify -> Prove.

## Current State (Post v1.1)

- v1.0 mission baseline shipped (`.planning/milestones/v1.0-ROADMAP.md`).
- v1.1 release hardening shipped (`.planning/milestones/v1.1-ROADMAP.md`, `.planning/milestones/v1.1-REQUIREMENTS.md`).
- Core Build → Apply → Verify → Prove workflow is operational with fail-closed evidence contracts.
- Upgrade/rebase, WPF parity, and release evidence are machine-verified.

## Current Milestone: v1.2 Verify Accuracy, Export Expansion, and Workflow Polish

**Goal:** Fix verify/SCC producing 0 results, add XCCDF/SCAP and CSV/Excel export adapters, and reduce operator friction across the Build/Apply/Verify/Prove workflow.

**Target features:**
- Fix STIGForge verify workflow returning 0 findings (SCC integration correctness)
- XCCDF/SCAP result export for tool interop (Tenable, ACAS, eMASS)
- CSV/Excel compliance reporting for management, auditors, and teams
- Pluggable export adapter architecture for future format additions
- Workflow UX polish: reduce clicks/steps, clarify status, improve error recovery

## Validated Deliverables (v1.0 + v1.1)

- ✓ Core operator workflow: Build → Apply → Verify → Prove (v1.0).
- ✓ Security hardening: break-glass, secure XML, archive boundaries, fail-closed gates (v1.0).
- ✓ Deterministic upgrade/rebase with conflict/action classification (v1.1).
- ✓ WPF parity for upgrade/rebase mission flows (v1.1).
- ✓ Fail-closed release evidence contracts across CI/VM/release/package (v1.1).

## Active Requirements (v1.2)

- [ ] Verify workflow returns accurate SCC/SCAP findings (correctness fix).
- ✓ XCCDF/SCAP result export for compliance tool interop (Phase 16).
- [ ] CSV/Excel compliance reporting for human audiences.
- ✓ Pluggable export adapter interface for future formats (Phase 15).
- [ ] Workflow UX improvements: fewer steps, clearer status, better error recovery.

## Non-Goals (Still Out of Scope)

- Direct eMASS API write-back.
- SCCM enterprise rollout platform.
- Multi-tenant cloud control plane.
- Bulk remediation simulation (PowerSTIG handles remediation; no gap identified).

## Locked Technical Decisions

- .NET 8 solution on Windows 10/11 and Server 2019+.
- PowerShell 5.1 compatibility is mandatory.
- SQLite remains the local system of record.
- Offline-first operation is non-negotiable (no runtime internet dependency).
- Classification scope behavior remains: `classified_only | unclassified_only | both | unknown`.
- New/changed-rule safety gate remains: review-required default with grace-period auto-apply controls.

## Key Decisions

- Archive milestone artifacts under `.planning/milestones/` and collapse `.planning/ROADMAP.md` to milestone summaries when a milestone ships.
- Validate requirement completion only when requirement mapping, execution verification, and summary metadata all align.
- Enforce release evidence contract checks across CI, VM, release workflow, and package build using one shared validator.
- Fail closed on `run_release_gate=false` for release packages.
- IExportAdapter + ExportAdapterRegistry + ExportOrchestrator is the pluggable export architecture; all format adapters implement IExportAdapter (Phase 15).
- EmassExporter uses explicit interface implementation to avoid overload ambiguity; CklExportAdapter wraps static CklExporter (Phase 15).
- XCCDF export uses Benchmark root element (not standalone TestResult) for maximum tool compatibility with STIG Viewer, Tenable, ACAS (Phase 16).
- Status/severity mapping in XccdfExportAdapter is the exact inverse of ScapResultAdapter parsing — ensures round-trip fidelity (Phase 16).
- Weight attribute omitted for unknown/null severity (not written as "0.0") to preserve round-trip correctness (Phase 16).
- CLI export commands load results from Verify/consolidated-results.json using VerifyReportReader.LoadFromJson (Phase 16).

## Context

- Evidence-driven workflow uses explicit gate contracts and closeout automation (established v1.1).
- STIGForge verify workflow currently returns 0 findings — SCC integration needs correctness investigation.
- Export currently supports eMASS CKL format only; operators need XCCDF/SCAP and CSV/Excel for broader tool/audience reach.
- Workflow has accumulated friction: too many clicks, unclear status indicators, painful error recovery.

## Constraints

- Maintain compatibility with PowerShell 5.1 and offline-first packaging requirements.
- Keep evidence contracts deterministic and fail-closed for release-critical paths.

## Definition of Done (Roadmap-Level)

- Build/test/release gates are executable and documented.
- Verification/export artifacts are deterministic across reruns.
- Critical workflows are executable by operators from the app UI.
- Security and integrity checks pass with actionable diagnostics.
- New milestone requirements are explicitly tracked and mapped to planned phases.

---
*Last updated: 2026-02-19 after Phase 16*

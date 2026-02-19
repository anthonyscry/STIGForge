# STIGForge Project Context

## Product Goal

STIGForge delivers an offline-first Windows hardening workflow that turns quarterly STIG content updates into a repeatable operator flow:

Build -> Apply -> Verify -> Prove.

## Current State (Post v1.2)

- v1.0 mission baseline shipped (`.planning/milestones/v1.0-ROADMAP.md`).
- v1.1 release hardening shipped (`.planning/milestones/v1.1-ROADMAP.md`).
- v1.2 verify accuracy and export expansion shipped (`.planning/milestones/v1.2-ROADMAP.md`).
- Core Build -> Apply -> Verify -> Prove workflow is operational with fail-closed evidence contracts.
- Verify workflow produces real SCC findings with configurable timeout and XCCDF routing.
- Export supports 5 formats: eMASS CKL, CKL, XCCDF 1.2, CSV, Excel (.xlsx) via pluggable adapter architecture.
- WPF app provides per-tool verify progress, error recovery guidance, and adapter-driven Quick Export.

## Validated Deliverables (v1.0 + v1.1 + v1.2)

- ✓ Core operator workflow: Build -> Apply -> Verify -> Prove (v1.0).
- ✓ Security hardening: break-glass, secure XML, archive boundaries, fail-closed gates (v1.0).
- ✓ Deterministic upgrade/rebase with conflict/action classification (v1.1).
- ✓ WPF parity for upgrade/rebase mission flows (v1.1).
- ✓ Fail-closed release evidence contracts across CI/VM/release/package (v1.1).
- ✓ SCC verify correctness: configurable timeout, session discovery, XCCDF routing, model unification (v1.2).
- ✓ Pluggable IExportAdapter architecture with registry and orchestrator (v1.2).
- ✓ XCCDF 1.2 XML export with round-trip validation for tool interop (v1.2).
- ✓ CSV compliance report with human-readable columns for management (v1.2).
- ✓ Excel multi-tab workbook export with ClosedXML (v1.2).
- ✓ WPF verify progress feedback, error recovery guidance, Quick Export format picker (v1.2).

## Non-Goals (Still Out of Scope)

- Direct eMASS API write-back.
- SCCM enterprise rollout platform.
- Multi-tenant cloud control plane.
- Bulk remediation simulation (PowerSTIG handles remediation; no gap identified).
- ARF (Assessment Results Format) export (requires OVAL output data that SCC controls).

## Locked Technical Decisions

- .NET 8 solution on Windows 10/11 and Server 2019+.
- PowerShell 5.1 compatibility is mandatory.
- SQLite remains the local system of record.
- Offline-first operation is non-negotiable (no runtime internet dependency).
- Classification scope behavior remains: `classified_only | unclassified_only | both | unknown`.
- New/changed-rule safety gate remains: review-required default with grace-period auto-apply controls.
- IExportAdapter + ExportAdapterRegistry + ExportOrchestrator is the pluggable export architecture.

## Key Decisions

- Archive milestone artifacts under `.planning/milestones/` and collapse `.planning/ROADMAP.md` to milestone summaries when a milestone ships.
- Validate requirement completion only when requirement mapping, execution verification, and summary metadata all align.
- Enforce release evidence contract checks across CI, VM, release workflow, and package build using one shared validator.
- Fail closed on `run_release_gate=false` for release packages.
- IExportAdapter + ExportAdapterRegistry + ExportOrchestrator is the pluggable export architecture; all format adapters implement IExportAdapter (v1.2).
- EmassExporter uses explicit interface implementation to avoid overload ambiguity; CklExportAdapter wraps static CklExporter (v1.2).
- XCCDF export uses Benchmark root element (not standalone TestResult) for maximum tool compatibility (v1.2).
- Status/severity mapping in XccdfExportAdapter is the exact inverse of ScapResultAdapter parsing for round-trip fidelity (v1.2).
- CLI export commands load results from Verify/consolidated-results.json using VerifyReportReader.LoadFromJson (v1.2).
- CSV export uses human-readable column headers and RFC 4180 escaping (v1.2).
- Excel export uses ClosedXML 0.105.0 (MIT); only new NuGet dependency for entire v1.2 milestone (v1.2).
- Quick Export tab registers 4 adapters (CKL, XCCDF, CSV, Excel); eMASS excluded from picker (requires DI; has dedicated tab) (v1.2).

## Context

- Evidence-driven workflow uses explicit gate contracts and closeout automation (established v1.1).
- Export supports 5 format adapters through the IExportAdapter pluggable architecture.
- WPF app provides per-tool verify progress, actionable error recovery, and single-button Quick Export.
- SCC CLI output-directory argument form needs validation against live SCC 5.x (MEDIUM confidence).

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
*Last updated: 2026-02-19 after v1.2 milestone completion*

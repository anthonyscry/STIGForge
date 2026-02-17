# STIGForge Project Context

## Product Goal

STIGForge delivers an offline-first Windows hardening workflow that turns quarterly STIG content updates into a repeatable operator flow:

Build -> Apply -> Verify -> Prove.

## Current State (Post v1.0)

- v1.0 mission baseline is shipped and archived in `.planning/milestones/v1.0-ROADMAP.md`.
- Deterministic release gate, compatibility regression, upgrade/rebase validation, and reproducibility evidence are in place.
- Core operator workflow, export integrity, and security hardening are implemented for offline/air-gapped environments.
- Phase 11 verification backfill closed UR orphaning with machine-verifiable cross-source requirement closure evidence.
- Phase 12 evidence promotion closed WP orphaning with explicit WPF workflow/severity/recovery contract signals wired through release and packaging evidence.
- Phase 13 enforced fail-closed release-package evidence contracts and restored QA three-source closure verification for promotion paths.

## Current Milestone: v1.1 (Complete)

**Goal:** Complete v1.1 gap-closure execution and preserve fail-closed release readiness evidence.

**Post-completion follow-ups:**
- Close remaining manual go/no-go checklist blockers with explicit signoff evidence.
- Run milestone archival and closeout flow for `v1.1`.

## Locked Technical Decisions

- .NET 8 solution on Windows 10/11 and Server 2019+.
- PowerShell 5.1 compatibility is mandatory.
- SQLite remains the local system of record.
- Offline-first operation is non-negotiable (no runtime internet dependency).
- Classification scope behavior remains: `classified_only | unclassified_only | both | unknown`.
- New/changed-rule safety gate remains: review-required default with grace-period auto-apply controls.

## Validated Deliverables (v1.0)

- Deterministic verification integration and conflict handling.
- Submission-safe export integrity (eMASS + CKL + POA&M coherence).
- Operator workflow completion in WPF/CLI mission surfaces.
- Security and operational hardening for air-gapped enterprise use.
- Repeatable release-readiness evidence (tests, fixtures, packaging, gate reports).

## Non-Goals (Still Out of Scope)

- Direct eMASS API write-back.
- SCCM enterprise rollout platform.
- Multi-tenant cloud control plane.

## Definition of Done (Roadmap-Level)

- Build/test/release gates are executable and documented.
- Verification/export artifacts are deterministic across reruns.
- Critical workflows are executable by operators from the app UI.
- Security and integrity checks pass with actionable diagnostics.
- New milestone requirements are explicitly tracked and mapped to planned phases.

---
*Last updated: 2026-02-17 after Phase 13 completion*

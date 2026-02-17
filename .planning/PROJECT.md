# STIGForge Project Context

## Product Goal

STIGForge delivers an offline-first Windows hardening workflow that turns quarterly STIG content updates into a repeatable operator flow:

Build -> Apply -> Verify -> Prove.

## Current State (Post v1.1)

- v1.0 mission baseline is shipped and archived in `.planning/milestones/v1.0-ROADMAP.md`.
- v1.1 milestone is complete with shipped archive in `.planning/milestones/v1.1-ROADMAP.md` and `.planning/milestones/v1.1-REQUIREMENTS.md`.
- Upgrade/rebase diff/rebase flows, WPF parity, and release evidence contracts are now machine-verified and fail-closed.
- Core operator workflow, export integrity, and security hardening remain implemented for offline/air-gapped environments.
- Phase 11 verification backfill closed UR orphaning with machine-verifiable cross-source requirement closure evidence.
- Phase 12 evidence promotion closed WP orphaning with explicit WPF workflow/severity/recovery contract signals.
- Phase 13 enforced release-package fail-closed behavior with shared evidence contract validation across CI, VM, release, and package build.

## Current Milestone: v1.1 (Complete)

**Goal:** Complete v1.1 gap-closure execution and preserve fail-closed release readiness evidence.

**Post-completion follow-ups:**
- Close remaining manual go/no-go checklist blockers with explicit signoff evidence.
- Run milestone archival and closeout flow for `v1.1`.
  - This includes this document update and deletion of `.planning/REQUIREMENTS.md` for next-milestone refresh.

## Validated Deliverables (v1.1)

- ✓ Deterministic baseline-to-target diff reporting with deterministic conflict/action classification (`UR-01`, `UR-02`, `UR-03`, `UR-04`).
- ✓ WPF parity for upgrade/rebase mission flows without standard CLI fallback (`WP-01`, `WP-02`, `WP-03`).
- ✓ CI/VM/release evidence trending and release-package evidence contract enforcement (`QA-01`, `QA-02`, `QA-03`).

## Active Requirements (v1.2+)

- [ ] Advanced bulk remediation simulation and preview workflows (`FUT-01`).
- [ ] Pluggable enterprise packaging/export adapters beyond eMASS baseline (`FUT-02`).

## Non-Goals (Still Out of Scope)

- Direct eMASS API write-back.
- SCCM enterprise rollout platform.
- Multi-tenant cloud control plane.

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

## Context

- Evidence-driven workflow has moved from orphaned requirement flags to explicit gate contracts and closeout automation.
- Current evidence quality for v1.1 is confirmed by `.planning/milestones/v1.1-MILESTONE-AUDIT.md` (passed).

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
*Last updated: 2026-02-17 after v1.1 milestone completion*

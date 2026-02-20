# STIGForge Milestones

## Milestone Index

| Version | Name | Date | Phase Span | Plans | Status |
|---------|------|------|------------|-------|--------|
| vNext | STIGForge Next Rebuild | 2026-02-20 | 01-06 | TBD | Active |
| v1.0 | Mission-Ready Baseline | 2026-02-09 | 03-07 | 17 | Shipped |

## vNext - STIGForge Next Rebuild (Active)

Source of truth:

- `PROJECT_SPEC.md`
- `.planning/PROJECT.md`
- `.planning/REQUIREMENTS.md`
- `.planning/ROADMAP.md`

Milestone intent:

- Replace legacy fragmented workflows with a schema-first deterministic pipeline.
- Enforce strict per-STIG SCAP mapping and review-required ambiguity policy.
- Ship offline-first submission-ready artifacts (CKL/POA&M/eMASS) with integrity proof.

Planned phase span:

- Phase 01: Foundations and canonical contracts.
- Phase 02: Policy/scope/release-age safety gates.
- Phase 03: Build and apply mission core.
- Phase 04: Verify normalization and strict STIG-SCAP mapping.
- Phase 05: Manual wizard, evidence autopilot, diff/rebase.
- Phase 06: Export/fleet-lite/audit integrity and MVP closeout.

## v1.0 - Mission-Ready Baseline (Shipped 2026-02-09)

Archive:

- `.planning/milestones/v1.0-ROADMAP.md`
- `.planning/milestones/v1.0-REQUIREMENTS.md`

Key accomplishments:

- Unified CLI and WPF verification into deterministic shared workflows with conflict-aware reconciliation.
- Hardened compliance export integrity with cross-artifact validation and actionable diagnostics.
- Completed operator mission workflow including manual review/evidence ergonomics and mission status normalization.
- Enforced security hardening: break-glass controls, secure XML parsing, archive boundary checks, strict offline security gate semantics, and fail-closed completion checks.
- Added release-readiness gates for quarterly compatibility, stability budgets, upgrade/rebase contracts, and reproducibility evidence packaging.

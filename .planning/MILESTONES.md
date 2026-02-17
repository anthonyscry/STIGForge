# STIGForge Milestones

## Milestone Index

| Version | Name | Date | Phase Span | Plans | Status |
|---------|------|------|------------|-------|--------|
| v1.0 | Mission-Ready Baseline | 2026-02-09 | 03-07 | 17 | Shipped |
| v1.1 | Release Hardening and Evidence Continuity | 2026-02-17 | 08-13 | 12 | Shipped |

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

## v1.1 Release Hardening and Evidence Continuity (Shipped 2026-02-17)

**Delivered:** Deterministic upgrade/rebase handling, WPF parity completion, and fail-closed release evidence enforcement.

**Phases completed:** 08-13 (12 plans)

**Key accomplishments:**

- Added deterministic baseline-to-target diff/rebase workflows with explicit conflict/action semantics and blocking conflict gates.
- Completed WPF parity for upgrade/rebase and recovery UX, including severity-consistent status and recovery guidance.
- Added quality gates for trendable compatibility/stability signals and enforced evidence-backed gate semantics in CI, VM, and release package paths.
- Backfilled verification artifacts for UR/WP/QA requirements to repair orphaned requirement closures with three-source cross-check validation.
- Enforced fail-closed release packaging via shared contract validator, including explicit `disabled-check` and `failed-check` blockers.


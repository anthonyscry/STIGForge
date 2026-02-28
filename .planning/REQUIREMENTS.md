# Requirements: STIGForge v1.1

**Defined:** 2026-02-09
**Milestone:** v1.1
**Status:** Active

## Core Value (Inherited)

Operators can execute a deterministic offline hardening mission flow with auditable outputs.

## v1.1 Requirements

### Upgrade and Rebase Operator Workflow

- [x] **UR-01**: Operator can generate a deterministic baseline-to-target diff report that classifies `added`, `changed`, `removed`, and `review-required` controls.
- [x] **UR-02**: Operator can run overlay rebase with deterministic conflict classification and explicit recommended actions for each conflict.
- [x] **UR-03**: Rebase execution preserves non-conflicting operator intent and blocks completion when unresolved blocking conflicts remain.
- [x] **UR-04**: Diff/rebase artifacts include machine-readable summary plus operator-readable report with enough detail for release review.

### WPF Parity and Usability

- [ ] **WP-01**: WPF app exposes diff/rebase workflow end-to-end without CLI fallback for standard operator paths.
- [ ] **WP-02**: WPF status and mission summaries match CLI semantics for blocking failures, warnings, and optional skips.
- [ ] **WP-03**: WPF surfaces actionable recovery guidance for failed apply/rebase paths (required artifacts, next command/action, and rollback guidance).

### Quality and Release Operations

- [ ] **QA-01**: CI includes deterministic automated coverage for diff/rebase core workflows and conflict handling paths.
- [ ] **QA-02**: VM/release gate evidence includes diff/rebase and WPF parity validation signals for go/no-go review.
- [ ] **QA-03**: Stability and compatibility gates for v1.1 emit trendable artifacts that flag regression drift before promotion.

## Future Requirements (v1.2+)

- **FUT-01**: Advanced bulk remediation simulation and preview workflows.
- **FUT-02**: Pluggable enterprise packaging/export adapters beyond eMASS baseline.

## Out of Scope (Unchanged)

| Feature | Reason |
|---------|--------|
| Direct eMASS API write-back | Still deferred beyond current milestone |
| SCCM enterprise rollout platform | Still deferred beyond current milestone |
| Multi-tenant cloud control plane | Still deferred beyond current milestone |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| UR-01 | Phase 11 | Completed |
| UR-02 | Phase 11 | Completed |
| UR-03 | Phase 11 | Completed |
| UR-04 | Phase 11 | Completed |
| WP-01 | Phase 12 | Completed |
| WP-02 | Phase 12 | Completed |
| WP-03 | Phase 12 | Completed |
| QA-01 | Phase 13 | Pending |
| QA-02 | Phase 13 | Pending |
| QA-03 | Phase 13 | Pending |

**Coverage:**
- v1.1 requirements: 10 total
- Mapped to phases: 10
- Pending: 3
- Completed: 7
- Unmapped: 0

---
*Last updated: 2026-02-27 after Phase 11 UR traceability reconciliation*

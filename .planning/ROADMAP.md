# STIGForge Roadmap (v1 Continuation)

## Planning Mode

Solo developer + Claude execution, phase-by-phase delivery with small executable plans.

## Current Position

- Completed: `01-content-parsing`, `02-apply-logic`, `03-verification-integration`, `04-compliance-export-integrity`, `06-security-and-operational-hardening-01`, `06-security-and-operational-hardening-02`, `06-security-and-operational-hardening-03`, `06-security-and-operational-hardening-04`
- Next: `07-release-readiness-and-compatibility` planning

---

### Phase 03: Verification Integration

**Status:** Completed
**Goal:** Make verification deterministic and consistent across CLI and WPF flows, including conflict resolution and coverage outputs.
**Plans:** 3 plans

Plans:
- [x] 03-verification-integration-01-PLAN.md - Harden adapters and merge reconciliation for deterministic consolidated outputs.
- [x] 03-verification-integration-02-PLAN.md - Introduce a shared verification workflow service with unit-test coverage.
- [x] 03-verification-integration-03-PLAN.md - Refactor CLI/WPF verify flows to shared orchestration and add parity integration tests.

Exit Criteria:
- Consolidated verify outputs are deterministic across repeated runs.
- CLI and WPF verification paths produce the same artifact structure and summary behavior.
- Conflict resolution is auditable and test-validated.

---

### Phase 04: Compliance Export Integrity

**Status:** Completed
**Goal:** Guarantee eMASS/CKL/POA&M export completeness and traceability from source verification/manual evidence.
**Plans:** 3 plans

Plans:
- [x] 04-compliance-export-integrity-01-PLAN.md - Centralize export status mapping and deterministic index/trace generation.
- [x] 04-compliance-export-integrity-02-PLAN.md - Strengthen package validation with cross-artifact integrity checks.
- [x] 04-compliance-export-integrity-03-PLAN.md - Surface validation diagnostics in CLI/WPF and add end-to-end export integrity tests.

Planned Focus:
- Enforce export schema and manifest integrity checks.
- Add cross-artifact consistency validation (control IDs, statuses, evidence links).
- Expand export failure diagnostics and recovery guidance.

Exit Criteria:
- Export package validator catches structural and linkage defects before delivery.
- eMASS-ready artifacts remain consistent with verification + manual answer inputs.

---

### Phase 05: Operator Workflow Completion

**Status:** Completed
**Goal:** Complete mission flow from UI for import, review, apply, verify, manual checks, and export.
**Plans:** 3 plans

Plans:
- [x] 05-operator-workflow-completion-01-PLAN.md - Create shared bundle mission summary and status normalization used by operator surfaces.
- [x] 05-operator-workflow-completion-02-PLAN.md - Wire WPF verify/dashboard flows to shared overlap and mission-summary infrastructure.
- [x] 05-operator-workflow-completion-03-PLAN.md - Improve manual wizard throughput and evidence ergonomics for in-app review completion.

Planned Focus:
- Remove remaining CLI-only dependencies for primary operator workflow.
- Improve manual wizard throughput and answer/evidence ergonomics.
- Strengthen dashboard/reporting clarity for decision support.

Exit Criteria:
- Core mission workflow executes end-to-end from WPF without operator CLI fallback.
- Manual check progress and evidence status are visible and actionable.

---

### Phase 06: Security and Operational Hardening

**Status:** Completed (2026-02-08)
**Goal:** Raise security posture and failure safety for enterprise/air-gapped operations.
**Plans:** 4 plans

Plans:
- [x] 06-security-and-operational-hardening-01-PLAN.md - Enforce explicit break-glass guardrails and CLI/WPF parity for high-risk actions.
- [x] 06-security-and-operational-hardening-02-PLAN.md - Harden input/file boundaries with safe archive extraction and secure XML parsing.
- [x] 06-security-and-operational-hardening-03-PLAN.md - Make release/security gates deterministic offline with strict unresolved-finding mode.
- [x] 06-security-and-operational-hardening-04-PLAN.md - Enforce fail-closed integrity checkpoints and mission-summary severity classification.

Planned Focus:
- Input/process/file hardening and defensive defaults.
- Audit-trail robustness and tamper verification coverage.
- Safe rollback rails and destructive-action guards.

Exit Criteria:
- Security and integrity gates pass with no critical findings.
- High-risk actions are guarded and reversible where possible.

---

### Phase 07: Release Readiness and Compatibility

**Status:** Planned
**Goal:** Produce a release candidate with stable compatibility guarantees across content updates.
**Plans:** 4 plans

Plans:
- [x] 07-release-readiness-and-compatibility-01-PLAN.md - Define deterministic fixture/compatibility matrix and gate compatibility contract checks in CI.
- [ ] 07-release-readiness-and-compatibility-02-PLAN.md - Implement long-run stability budget and enforce smoke/stability signals in CI and VM matrix.
- [ ] 07-release-readiness-and-compatibility-03-PLAN.md - Add quarterly regression pack runner with drift artifacts and release-gate integration.
- [ ] 07-release-readiness-and-compatibility-04-PLAN.md - Finalize RC checklist, reproducibility evidence, and upgrade/rebase validation gating.

Planned Focus:
- Fixture corpus expansion and long-run stability validation.
- Quarterly update regression packs and drift detection.
- Final release checklist, package reproducibility, and documentation lock.

Exit Criteria:
- Release gate and compatibility suite pass on target environments.
- Upgrade/rebase behavior is documented and validated.

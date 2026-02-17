# STIGForge Roadmap (v1.1)

## Planning Mode

Solo developer + Claude execution, phase-by-phase delivery with small executable plans.

## Milestone History

- ✅ `v1.0` shipped on 2026-02-09
  - Archive: `.planning/milestones/v1.0-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.0-REQUIREMENTS.md`

## Current Position

- Current milestone: `v1.1` (execution complete)
- Starting phase index: `08`
- Requirement source: `.planning/REQUIREMENTS.md`
- Next action: complete milestone archival and closeout (`/gsd-complete-milestone v1.1`)

---

### Phase 08: Upgrade/Rebase Operator Workflow

**Status:** Completed (2026-02-09)
**Goal:** Complete deterministic diff/rebase operator workflows with blocking conflict semantics and audit-ready artifacts.
**Requirements:** `UR-01`, `UR-02`, `UR-03`, `UR-04`
**Plans:** 2 plans

Plans:
- [x] 08-upgrade-rebase-operator-workflow-01-PLAN.md - Harden deterministic diff/rebase contracts and artifact structure.
- [x] 08-upgrade-rebase-operator-workflow-02-PLAN.md - Enforce unresolved-conflict fail-closed semantics and recovery guidance across operator surfaces.

Exit Criteria:
- Diff/rebase outputs are deterministic and operator-actionable.
- Rebase completion blocks on unresolved blocking conflicts.
- Artifact bundle includes both machine-readable and operator-readable reports.

---

### Phase 09: WPF Parity and Recovery UX

**Status:** Completed (2026-02-09)
**Goal:** Remove remaining operator CLI fallback for scoped workflows and align WPF mission/recovery semantics with CLI.
**Requirements:** `WP-01`, `WP-02`, `WP-03`
**Plans:** 2 plans

Plans:
- [x] 09-wpf-parity-and-recovery-ux-01-PLAN.md - Wire end-to-end diff/rebase flows in WPF with parity to CLI orchestration.
- [x] 09-wpf-parity-and-recovery-ux-02-PLAN.md - Add recovery guidance and severity-consistent mission summaries in WPF surfaces.

Exit Criteria:
- WPF supports scoped diff/rebase mission path without standard CLI fallback.
- WPF and CLI classify blocking/warning/skip outcomes consistently.
- Operators receive explicit recovery guidance for failed rebase/apply flows.

---

### Phase 10: Quality and Release Signal Hardening

**Status:** Completed (2026-02-09)
**Goal:** Expand CI/VM/release signal quality for v1.1 workflows and make compatibility drift evidence trendable.
**Requirements:** `QA-01`, `QA-02`, `QA-03`
**Plans:** 2 plans

Plans:
- [x] 10-quality-and-release-signal-hardening-01-PLAN.md - Add deterministic automated coverage for diff/rebase and parity-critical paths.
- [x] 10-quality-and-release-signal-hardening-02-PLAN.md - Promote trendable stability/compatibility signal artifacts into release decision flow.

Exit Criteria:
- CI and VM gates enforce v1.1 diff/rebase and parity regressions.
- Release evidence packages include trendable compatibility/stability signals.
- Go/No-Go review has explicit v1.1 parity and regression coverage evidence.

---

### Phase 11: Verification Backfill for Upgrade/Rebase

**Status:** Completed (2026-02-16)
**Goal:** Close orphaned requirement evidence for upgrade/rebase workflows by restoring phase verification artifacts and machine-verifiable requirement closure metadata.
**Requirements:** `UR-01`, `UR-02`, `UR-03`, `UR-04`
**Gap Closure:** Requirement orphaning gaps from `v1.1-MILESTONE-AUDIT.md`
**Plans:** 1/1 plans complete

Plans:
- [x] 11-verification-backfill-for-upgrade-rebase-01-PLAN.md - Backfill Phase 08 verification artifact and reconcile UR requirement closure metadata across planning evidence sources.

Exit Criteria:
- Phase 08 verification artifact exists and maps UR requirement evidence.
- Summary metadata provides machine-verifiable requirement closure for UR requirements.
- Requirement traceability can move from Pending to Completed after execution verification.

---

### Phase 12: WPF Parity Evidence Promotion and Verification

**Status:** Completed (2026-02-16)
**Goal:** Close WPF parity evidence gaps by adding explicit WPF workflow contract evidence to promotion artifacts and verification outputs.
**Requirements:** `WP-01`, `WP-02`, `WP-03`
**Gap Closure:** Requirement orphaning and integration/flow gaps for explicit WPF parity evidence promotion.
**Plans:** 3/3 plans complete

Plans:
- [x] 12-wpf-parity-evidence-promotion-and-verification-01-PLAN.md - Backfill Phase 09 WP verification artifact and summary metadata for three-source closure inputs.
- [x] 12-wpf-parity-evidence-promotion-and-verification-02-PLAN.md - Promote explicit WPF workflow/severity/recovery contract signals into release-gate, workflows, and package evidence wiring.
- [x] 12-wpf-parity-evidence-promotion-and-verification-03-PLAN.md - Create Phase 12 verification closure artifact and reconcile WP traceability status fail-closed.

Exit Criteria:
- Phase 09 verification artifact exists and maps WP requirement evidence.
- Promotion evidence includes explicit WPF parity workflow contract signals.
- Operator diff/rebase to WPF parity to release evidence flow is fully wired.

---

### Phase 13: Mandatory Release-Gate Enforcement and Verification

**Status:** Completed (2026-02-17)
**Goal:** Enforce fail-closed release-package behavior and restore QA requirement verification evidence for promotion paths.
**Requirements:** `QA-01`, `QA-02`, `QA-03`
**Gap Closure:** Requirement orphaning and integration/flow gaps for fail-closed release gate enforcement.
**Plans:** 2/2 plans complete

Plans:
- [x] 13-mandatory-release-gate-enforcement-and-verification-01-PLAN.md - Enforce mandatory fail-closed release-evidence contract across CI, release-package, VM, and package build paths.
- [x] 13-mandatory-release-gate-enforcement-and-verification-02-PLAN.md - Backfill Phase 10 QA verification artifact and reconcile three-source requirement closure evidence.

Exit Criteria:
- Phase 10 verification artifact exists and maps QA requirement evidence.
- `release-package.yml` cannot produce package artifacts when required release-gate evidence is missing or disabled.
- CI, release, and VM promotion gate flow is consistently fail-closed.

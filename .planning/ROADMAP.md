# STIGForge Roadmap (v1.1)

## Planning Mode

Solo developer + Claude execution, phase-by-phase delivery with small executable plans.

## Milestone History

- ✅ `v1.0` shipped on 2026-02-09
  - Archive: `.planning/milestones/v1.0-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.0-REQUIREMENTS.md`

## Current Position

- Current milestone: `v1.1` (execution active)
- Starting phase index: `08`
- Requirement source: `.planning/REQUIREMENTS.md`
- Next action: close manual RC checklist blockers and rerun evidence on pinned commit

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

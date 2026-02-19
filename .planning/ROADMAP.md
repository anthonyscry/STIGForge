# STIGForge Roadmap

## Milestones

- ✅ **v1.0 Mission-Ready Baseline** — Phases 03-07 (shipped 2026-02-09)
  - Archive: `.planning/milestones/v1.0-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.0-REQUIREMENTS.md`
- ✅ **v1.1 Release Hardening and Evidence Continuity** — Phases 08-13 (shipped 2026-02-17)
  - Archive: `.planning/milestones/v1.1-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.1-REQUIREMENTS.md`
  - Milestone audit: `.planning/milestones/v1.1-MILESTONE-AUDIT.md`
- ✅ **v1.2 Verify Accuracy, Export Expansion, and Workflow Polish** — Phases 14-19 (shipped 2026-02-19)
  - Archive: `.planning/milestones/v1.2-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.2-REQUIREMENTS.md`

## Phases

<details>
<summary>✅ v1.0 Mission-Ready Baseline (Phases 03-07) — SHIPPED 2026-02-09</summary>

### Phase 03: Verification Integration

**Status:** Completed (2026-02-09)
**Plans:** 3 plans

- [x] 03-verification-integration-01-PLAN.md
- [x] 03-verification-integration-02-PLAN.md
- [x] 03-verification-integration-03-PLAN.md

### Phase 04: Compliance Export Integrity

**Status:** Completed (2026-02-09)
**Plans:** 3 plans

- [x] 04-compliance-export-integrity-01-PLAN.md
- [x] 04-compliance-export-integrity-02-PLAN.md
- [x] 04-compliance-export-integrity-03-PLAN.md

### Phase 05: Operator Workflow Completion

**Status:** Completed (2026-02-09)
**Plans:** 3 plans

- [x] 05-operator-workflow-completion-01-PLAN.md
- [x] 05-operator-workflow-completion-02-PLAN.md
- [x] 05-operator-workflow-completion-03-PLAN.md

### Phase 06: Security and Operational Hardening

**Status:** Completed (2026-02-08)
**Plans:** 4 plans

- [x] 06-security-and-operational-hardening-01-PLAN.md
- [x] 06-security-and-operational-hardening-02-PLAN.md
- [x] 06-security-and-operational-hardening-03-PLAN.md
- [x] 06-security-and-operational-hardening-04-PLAN.md

### Phase 07: Release Readiness and Compatibility

**Status:** Completed (2026-02-08)
**Plans:** 4 plans

- [x] 07-release-readiness-and-compatibility-01-PLAN.md
- [x] 07-release-readiness-and-compatibility-02-PLAN.md
- [x] 07-release-readiness-and-compatibility-03-PLAN.md
- [x] 07-release-readiness-and-compatibility-04-PLAN.md

</details>

<details>
<summary>✅ v1.1 Release Hardening and Evidence Continuity (Phases 08-13) — SHIPPED 2026-02-17</summary>

### Phase 08: Upgrade/Rebase Operator Workflow

**Status:** Completed (2026-02-09)
**Requirements:** `UR-01`, `UR-02`, `UR-03`, `UR-04`
**Plans:** 2 plans

- [x] 08-upgrade-rebase-operator-workflow-01-PLAN.md
- [x] 08-upgrade-rebase-operator-workflow-02-PLAN.md

### Phase 09: WPF Parity and Recovery UX

**Status:** Completed (2026-02-09)
**Requirements:** `WP-01`, `WP-02`, `WP-03`
**Plans:** 2 plans

- [x] 09-wpf-parity-and-recovery-ux-01-PLAN.md
- [x] 09-wpf-parity-and-recovery-ux-02-PLAN.md

### Phase 10: Quality and Release Signal Hardening

**Status:** Completed (2026-02-09)
**Requirements:** `QA-01`, `QA-02`, `QA-03`
**Plans:** 2 plans

- [x] 10-quality-and-release-signal-hardening-01-PLAN.md
- [x] 10-quality-and-release-signal-hardening-02-PLAN.md

### Phase 11: Verification Backfill for Upgrade/Rebase

**Status:** Completed (2026-02-16)
**Requirements:** `UR-01`, `UR-02`, `UR-03`, `UR-04`
**Plans:** 1/1 plans complete

- [x] 11-verification-backfill-for-upgrade-rebase-01-PLAN.md

### Phase 12: WPF Parity Evidence Promotion and Verification

**Status:** Completed (2026-02-16)
**Requirements:** `WP-01`, `WP-02`, `WP-03`
**Plans:** 3/3 plans complete

- [x] 12-wpf-parity-evidence-promotion-and-verification-01-PLAN.md
- [x] 12-wpf-parity-evidence-promotion-and-verification-02-PLAN.md
- [x] 12-wpf-parity-evidence-promotion-and-verification-03-PLAN.md

### Phase 13: Mandatory Release-Gate Enforcement and Verification

**Status:** Completed (2026-02-17)
**Requirements:** `QA-01`, `QA-02`, `QA-03`
**Plans:** 2/2 plans complete

- [x] 13-mandatory-release-gate-enforcement-and-verification-01-PLAN.md
- [x] 13-mandatory-release-gate-enforcement-and-verification-02-PLAN.md

</details>

<details>
<summary>✅ v1.2 Verify Accuracy, Export Expansion, and Workflow Polish (Phases 14-19) — SHIPPED 2026-02-19</summary>

### Phase 14: SCC Verify Correctness and Model Unification

**Status:** Completed (2026-02-18)
**Requirements:** VER-01, VER-02, VER-03, VER-04, VER-05
**Plans:** 2/2 plans complete

- [x] 14-01-PLAN.md — Async runner timeout and CLI wiring
- [x] 14-02-PLAN.md — Orchestrator wiring, model bridge, CklParser hardening, UI restructure

### Phase 15: Pluggable Export Adapter Interface

**Status:** Completed (2026-02-19)
**Requirements:** EXP-04, EXP-05
**Plans:** 1/1 plans complete

- [x] 15-01-PLAN.md — IExportAdapter, ExportAdapterRegistry, ExportOrchestrator; retrofit EmassExporter and CklExportAdapter

### Phase 16: XCCDF Result Export

**Status:** Completed (2026-02-19)
**Requirements:** EXP-01
**Plans:** 1/1 plans complete

- [x] 16-01-PLAN.md — XccdfExportAdapter with round-trip validation and export-xccdf CLI command

### Phase 17: CSV Compliance Report

**Status:** Completed (2026-02-19)
**Requirements:** EXP-02
**Plans:** 1/1 plans complete

- [x] 17-01-PLAN.md — CsvExportAdapter with RFC 4180 escaping and export-csv CLI command

### Phase 18: Excel Compliance Report

**Status:** Completed (2026-02-19)
**Requirements:** EXP-03
**Plans:** 1/1 plans complete

- [x] 18-01-PLAN.md — ExcelExportAdapter and ReportGenerator with ClosedXML; export-excel CLI command

### Phase 19: WPF Workflow UX Polish and Export Format Picker

**Status:** Completed (2026-02-19)
**Requirements:** UX-01, UX-02, UX-03
**Plans:** 2/2 plans complete

- [x] 19-01-PLAN.md — VerifyToolStatus progress model, error recovery panel, VerifyView.xaml
- [x] 19-02-PLAN.md — Quick Export tab with format picker driven by ExportAdapterRegistry

</details>

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 03 Verification Integration | v1.0 | 3/3 | Complete | 2026-02-09 |
| 04 Compliance Export Integrity | v1.0 | 3/3 | Complete | 2026-02-09 |
| 05 Operator Workflow Completion | v1.0 | 3/3 | Complete | 2026-02-09 |
| 06 Security and Operational Hardening | v1.0 | 4/4 | Complete | 2026-02-08 |
| 07 Release Readiness and Compatibility | v1.0 | 4/4 | Complete | 2026-02-08 |
| 08 Upgrade/Rebase Operator Workflow | v1.1 | 2/2 | Complete | 2026-02-09 |
| 09 WPF Parity and Recovery UX | v1.1 | 2/2 | Complete | 2026-02-09 |
| 10 Quality and Release Signal Hardening | v1.1 | 2/2 | Complete | 2026-02-09 |
| 11 Verification Backfill for Upgrade/Rebase | v1.1 | 1/1 | Complete | 2026-02-16 |
| 12 WPF Parity Evidence Promotion and Verification | v1.1 | 3/3 | Complete | 2026-02-16 |
| 13 Mandatory Release-Gate Enforcement and Verification | v1.1 | 2/2 | Complete | 2026-02-17 |
| 14 SCC Verify Correctness and Model Unification | v1.2 | 2/2 | Complete | 2026-02-18 |
| 15 Pluggable Export Adapter Interface | v1.2 | 1/1 | Complete | 2026-02-19 |
| 16 XCCDF Result Export | v1.2 | 1/1 | Complete | 2026-02-19 |
| 17 CSV Compliance Report | v1.2 | 1/1 | Complete | 2026-02-19 |
| 18 Excel Compliance Report | v1.2 | 1/1 | Complete | 2026-02-19 |
| 19 WPF Workflow UX Polish and Export Format Picker | v1.2 | 2/2 | Complete | 2026-02-19 |

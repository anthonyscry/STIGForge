# STIGForge Roadmap

## Milestones

- ✅ **v1.0 Mission-Ready Baseline** — Phases 03-07 (shipped 2026-02-09)
  - Archive: `.planning/milestones/v1.0-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.0-REQUIREMENTS.md`
- ✅ **v1.1 Release Hardening and Evidence Continuity** — Phases 08-13 (shipped 2026-02-17)
  - Archive: `.planning/milestones/v1.1-ROADMAP.md`
  - Requirements archive: `.planning/milestones/v1.1-REQUIREMENTS.md`
  - Milestone audit: `.planning/milestones/v1.1-MILESTONE-AUDIT.md`
- 🚧 **v1.2 Verify Accuracy, Export Expansion, and Workflow Polish** — Phases 14-19 (in progress)

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
**Gap Closure:** Requirement orphaning gaps
**Plans:** 1/1 plans complete

- [x] 11-verification-backfill-for-upgrade-rebase-01-PLAN.md

### Phase 12: WPF Parity Evidence Promotion and Verification

**Status:** Completed (2026-02-16)
**Requirements:** `WP-01`, `WP-02`, `WP-03`
**Gap Closure:** WPF parity evidence and promotion gaps
**Plans:** 3/3 plans complete

- [x] 12-wpf-parity-evidence-promotion-and-verification-01-PLAN.md
- [x] 12-wpf-parity-evidence-promotion-and-verification-02-PLAN.md
- [x] 12-wpf-parity-evidence-promotion-and-verification-03-PLAN.md

### Phase 13: Mandatory Release-Gate Enforcement and Verification

**Status:** Completed (2026-02-17)
**Requirements:** `QA-01`, `QA-02`, `QA-03`
**Gap Closure:** Release-gate and evidence contract gaps
**Plans:** 2/2 plans complete

- [x] 13-mandatory-release-gate-enforcement-and-verification-01-PLAN.md
- [x] 13-mandatory-release-gate-enforcement-and-verification-02-PLAN.md

</details>

### 🚧 v1.2 Verify Accuracy, Export Expansion, and Workflow Polish (In Progress)

**Milestone Goal:** Fix SCC verify returning 0 results, add XCCDF/SCAP and CSV/Excel export formats, and reduce operator friction across the verify and export workflow.

## Phase Details

### Phase 14: SCC Verify Correctness and Model Unification
**Goal**: Operators can run a verify scan that produces real SCC findings, not zero results
**Depends on**: Phase 13
**Requirements**: VER-01, VER-02, VER-03, VER-04, VER-05
**Success Criteria** (what must be TRUE):
  1. Operator runs verify against a live system; `consolidated-results.json` contains a non-zero result count when SCC detects findings
  2. Operator can configure the SCC scan timeout; scans longer than 30 seconds complete without premature termination
  3. Verify workflow discovers SCC output from `Sessions/` subdirectories and processes XCCDF XML files alongside CKL files
  4. Verify workflow routes XCCDF results through `VerifyOrchestrator` adapter chain; `ControlResult` and `NormalizedVerifyResult` are resolved to a single canonical model
  5. `CklParser` rejects malformed or XXE-bearing XML using the same `LoadSecureXml()` hardening applied to `CklAdapter`
**Plans**: 2 plans

Plans:
- [x] 14-01-PLAN.md — Add RunAsync with configurable timeout to ScapRunner and EvaluateStigRunner; add TimeoutSeconds to workflow options and CLI
- [x] 14-02-PLAN.md — Wire VerifyOrchestrator into VerificationWorkflowService; bridge NormalizedVerifyResult to ControlResult; harden CklParser; fix MainViewModel defaults; restructure VerifyView tabs

### Phase 15: Pluggable Export Adapter Interface
**Goal**: A defined, tested `IExportAdapter` contract is in place and all existing exporters implement it
**Depends on**: Phase 14
**Requirements**: EXP-04, EXP-05
**Success Criteria** (what must be TRUE):
  1. `IExportAdapter` interface exists with `ExportAdapterRequest` and `ExportAdapterResult` models; adapters return result (not void)
  2. `ExportAdapterRegistry` resolves adapters by format name; `ExportOrchestrator` dispatches to the correct adapter
  3. Existing `EmassExporter` and `CklExporter` implement `IExportAdapter` and existing call sites continue to work
**Plans**: 1 plan

Plans:
- [ ] 15-01-PLAN.md — Define IExportAdapter, ExportAdapterRegistry, ExportOrchestrator; retrofit EmassExporter and create CklExportAdapter

### Phase 16: XCCDF Result Export
**Goal**: Operators can export verify results as XCCDF 1.2 XML consumable by Tenable, ACAS, and STIG Viewer
**Depends on**: Phase 15
**Requirements**: EXP-01
**Success Criteria** (what must be TRUE):
  1. Operator exports verify results via CLI `export-xccdf` command; output is a valid XCCDF 1.2 XML file with correct `http://checklists.nist.gov/xccdf/1.2` namespace on every element
  2. Exported XCCDF file passes a round-trip test: `ScapResultAdapter.CanHandle()` returns true and parsed result count matches the original
  3. Export fails closed: partial output file is deleted if the adapter throws
**Plans**: TBD

Plans:
- [ ] 16-01: Implement XccdfExportAdapter with round-trip validation

### Phase 17: CSV Compliance Report
**Goal**: Operators can export a management-facing compliance report as CSV
**Depends on**: Phase 15
**Requirements**: EXP-02
**Success Criteria** (what must be TRUE):
  1. Operator exports via CLI `export-csv` command; output CSV includes system name, STIG title, CAT level, status, finding detail, and remediation priority columns
  2. CSV values containing commas, quotes, or newlines are correctly escaped; no malformed rows in the output file
  3. Export completes and produces a non-empty file when verify results are present
**Plans**: TBD

Plans:
- [ ] 17-01: Implement CsvExportAdapter with management-facing columns and property-based escape tests

### Phase 18: Excel Compliance Report
**Goal**: Operators can export a multi-tab Excel workbook for management and auditor review
**Depends on**: Phase 17
**Requirements**: EXP-03
**Success Criteria** (what must be TRUE):
  1. Operator exports via CLI `export-excel` command; output is an `.xlsx` file with four tabs: Summary, All Controls, Open Findings, Coverage
  2. Exported workbook opens correctly in Excel and contains the same control data as the CSV export
  3. `STIGForge.Reporting.ReportGenerator` is fully implemented (not a stub); ClosedXML 0.105.0 (MIT) is the only new dependency added
**Plans**: TBD

Plans:
- [ ] 18-01: Implement ExcelExportAdapter and ReportGenerator using ClosedXML

### Phase 19: WPF Workflow UX Polish and Export Format Picker
**Goal**: The WPF app surfaces meaningful verify progress, actionable error recovery, and a single adapter-driven export control
**Depends on**: Phase 15
**Requirements**: UX-01, UX-02, UX-03
**Success Criteria** (what must be TRUE):
  1. Operator running a verify scan sees live progress feedback showing tool name, state (Pending/Running/Complete/Failed), elapsed time, and finding count — not a blank UI
  2. When verify or export fails, the UI displays an actionable error message with specific recovery steps (not just an error code)
  3. Operator selects an export format from a ComboBox populated by registered `IExportAdapter` entries and triggers export with a single button; no per-format dialogs accumulate
  4. Export button is disabled while an export is running; `_isBusy` pattern prevents double-submission
**Plans**: TBD

Plans:
- [ ] 19-01: Add VerifyStatus progress model and bind to WPF verify view
- [ ] 19-02: Implement export format picker ComboBox driven by ExportAdapterRegistry; wire single Export command through ExportOrchestrator

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
| 15 Pluggable Export Adapter Interface | v1.2 | 0/1 | Not started | - |
| 16 XCCDF Result Export | v1.2 | 0/1 | Not started | - |
| 17 CSV Compliance Report | v1.2 | 0/1 | Not started | - |
| 18 Excel Compliance Report | v1.2 | 0/1 | Not started | - |
| 19 WPF Workflow UX Polish and Export Format Picker | v1.2 | 0/2 | Not started | - |

# Requirements: STIGForge v1.2

**Defined:** 2026-02-18
**Core Value:** Offline-first Windows hardening workflow: Build → Apply → Verify → Prove

## v1.2 Requirements

Requirements for v1.2 Verify Accuracy, Export Expansion, and Workflow Polish.

### Verify Correctness

- [ ] **VER-01**: Verify workflow completes SCC scans without premature timeout (fix 30s hardcoded limit)
- [ ] **VER-02**: Verify workflow discovers SCC XCCDF output from session subdirectories (fix `.ckl`-only glob)
- [ ] **VER-03**: Verify workflow routes XCCDF results through `ScapResultAdapter` via `VerifyOrchestrator`
- [ ] **VER-04**: Result models are unified (`ControlResult`/`NormalizedVerifyResult` bridge) for downstream export
- [ ] **VER-05**: `CklParser` uses hardened XML loading consistent with `CklAdapter` security baseline

### Export Adapters

- [ ] **EXP-01**: Operator can export verify results as XCCDF 1.2 XML for tool interop (Tenable, ACAS, STIG Viewer)
- [ ] **EXP-02**: Operator can export compliance report as CSV for management/auditor review
- [ ] **EXP-03**: Operator can export compliance report as Excel (.xlsx) multi-tab workbook
- [ ] **EXP-04**: Export adapters implement a pluggable `IExportAdapter` interface for extensibility
- [ ] **EXP-05**: Existing eMASS/CKL exporters are refactored to use the `IExportAdapter` contract

### Workflow UX

- [ ] **UX-01**: Verify workflow displays meaningful progress feedback during SCC scans
- [ ] **UX-02**: Error states include recovery guidance (actionable next steps, not just error messages)
- [ ] **UX-03**: WPF export view provides format picker driven by registered export adapters

## Future Requirements

### Deferred from v1.2

- **FUT-01**: Advanced bulk remediation simulation and preview workflows (PowerSTIG handles remediation; no gap identified)
- **FUT-03**: Policy pack template marketplace with signed template trust chain

### v2+ Candidates

- **ENT-01**: SCCM packaging integration
- **ENT-02**: Direct eMASS API write-back
- **ENT-03**: Full enterprise GPO management platform

## Out of Scope

| Feature | Reason |
|---------|--------|
| Bulk remediation simulation | PowerSTIG handles remediation natively; no operator gap identified |
| ARF (Assessment Results Format) export | Requires OVAL output data that SCC controls, not STIGForge; partial ARF produces broken files |
| Direct eMASS API write-back | Enterprise integration deferred to v2+ |
| SCCM enterprise rollout | Enterprise platform deferred to v2+ |
| Multi-tenant cloud control plane | Out of scope for offline-first tool |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| VER-01 | Phase 14 | Pending |
| VER-02 | Phase 14 | Pending |
| VER-03 | Phase 14 | Pending |
| VER-04 | Phase 14 | Pending |
| VER-05 | Phase 14 | Pending |
| EXP-04 | Phase 15 | Pending |
| EXP-05 | Phase 15 | Pending |
| EXP-01 | Phase 16 | Pending |
| EXP-02 | Phase 17 | Pending |
| EXP-03 | Phase 18 | Pending |
| UX-01 | Phase 19 | Pending |
| UX-02 | Phase 19 | Pending |
| UX-03 | Phase 19 | Pending |

**Coverage:**
- v1.2 requirements: 13 total
- Mapped to phases: 13
- Unmapped: 0

---
*Requirements defined: 2026-02-18*
*Last updated: 2026-02-18 after roadmap creation*

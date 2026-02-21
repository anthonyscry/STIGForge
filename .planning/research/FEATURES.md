# Feature Research

**Domain:** Windows compliance orchestration (apply + multi-scanner verification + checklist export)
**Researched:** 2026-02-20
**Confidence:** MEDIUM

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Baseline/profile management for STIG/SCAP content | Compliance teams must select, version, and scope controls before any enforcement | HIGH | Must support Windows role context (server/workstation/domain) and profile version pinning |
| Apply orchestration with safe execution controls | Products in this category are expected to enforce desired state, not only report drift | HIGH | Include preflight checks, remote/local target selection, job status, and failure capture |
| Multi-scanner ingestion and normalization | Real programs run more than one scanner and need a unified answer set | HIGH | Normalize scanner outputs into a canonical control result model |
| Crosswalked control correlation | Without control-ID correlation, multi-scanner verification becomes manual spreadsheet work | HIGH | Must map scanner findings to control IDs deterministically and preserve raw source references |
| Manual attestation/waiver workflow | Some controls remain manual/not reviewed and must still land in evidence packages | MEDIUM | Include reviewer identity, rationale, expiration, and audit history |
| Checklist/evidence export (CKL/XCCDF/CSV/POA&M-ready) | Exportable artifacts are required for audit handoff and package submission | HIGH | SAF and Heimdall ecosystems already expose these formats; users expect parity |
| End-to-end audit traceability | Auditors require proof of what was applied, verified, and changed over time | MEDIUM | Immutable run metadata, input hashes, and per-control provenance |

### Differentiators (Competitive Advantage)

Features that set the product apart. Not required, but valuable.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Deterministic mission loop (Build -> Apply -> Verify -> Prove) | Produces reproducible outputs for identical inputs, reducing audit disputes | HIGH | Strong fit with STIGForge core value and offline mission workflows |
| Multi-scanner consensus engine with conflict reasons | Moves beyond ingestion to explain why scanners disagree and what to trust | HIGH | Prioritize control-level disagreement views and operator resolution queue |
| Strict per-STIG mapping invariant with review-required ambiguity | Prevents false confidence caused by broad heuristic matching | HIGH | If mapping confidence is low, force review instead of auto-pass/fail |
| Offline-first evidence packaging and replay | Air-gapped teams can run complete workflows without cloud dependencies | MEDIUM | Support deterministic bundle regeneration from stored inputs |
| Guided remediation safety (dry-run + staged apply + rollback points) | Reduces blast radius when applying hardening at scale | HIGH | Especially valuable for production Windows server fleets |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but create problems.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| One-click auto-remediation on first scan without review | Feels fast and "hands-off" | High risk of breaking systems from scanner noise or context gaps | Require preflight + dry-run + approval gate for risky controls |
| Heuristic "best effort" crosswalk across unrelated benchmarks | Promises broader coverage quickly | Produces silent mis-mappings and untrustworthy verification | Use strict mapping contracts and explicit review-required states |
| Cloud-required core workflow | Easier central updates/telemetry | Breaks air-gapped and disconnected operations | Keep core mission loop fully offline; sync optional |
| Direct eMASS API writeback in v1 | Reduces manual submission steps | Adds credential/compliance boundary complexity and conflicts with current project scope | Export deterministic package for manual upload |

## Feature Dependencies

```text
[Baseline/profile management]
    └──requires──> [Control correlation model]
                        └──requires──> [Multi-scanner ingestion/normalization]

[Apply orchestration]
    └──requires──> [Baseline/profile management]

[Checklist/evidence export]
    └──requires──> [Apply orchestration]
                        └──requires──> [Crosswalked verification results]
                                             └──requires──> [Manual attestation/waiver workflow]

[Deterministic mission loop] ──enhances──> [Checklist/evidence export]

[One-click auto-remediation] ──conflicts──> [Guided remediation safety]
```

### Dependency Notes

- **Apply orchestration requires baseline/profile management:** you cannot safely enforce settings without a selected and versioned control set.
- **Checklist/evidence export requires crosswalked verification results:** exported packages must include normalized control outcomes, not raw scanner blobs.
- **Deterministic mission loop enhances checklist/evidence export:** reproducible ordering and hashing make packages defensible under audit.
- **One-click auto-remediation conflicts with guided remediation safety:** speed-first automation removes critical review gates for risky controls.

## MVP Definition

### Launch With (v1)

Minimum viable product - what is needed to validate this domain.

- [ ] Baseline/profile management - foundation for all downstream apply/verify/export flows
- [ ] Apply orchestration + multi-scanner verification normalization - core mission capability
- [ ] Checklist/evidence export with manual attestation support - required audit handoff capability

### Add After Validation (v1.x)

Features to add once core is working.

- [ ] Multi-scanner consensus conflict reasoning - add after stable normalization and correlation
- [ ] Guided remediation safety extras (rollback snapshots, staged policies) - add once core apply path is reliable

### Future Consideration (v2+)

Features to defer until product-market fit is established.

- [ ] Optional cloud sync and enterprise federation - defer to preserve offline-first guarantees in v1
- [ ] Direct system-of-record integrations (e.g., API writeback paths) - defer until governance boundaries are approved

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Baseline/profile management | HIGH | HIGH | P1 |
| Apply orchestration | HIGH | HIGH | P1 |
| Multi-scanner normalization + control correlation | HIGH | HIGH | P1 |
| Checklist/evidence export + manual attestation | HIGH | HIGH | P1 |
| Multi-scanner consensus conflict reasoning | HIGH | MEDIUM | P2 |
| Guided remediation safety extras | MEDIUM | HIGH | P2 |
| Optional cloud sync | MEDIUM | HIGH | P3 |

**Priority key:**
- P1: Must have for launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

## Competitor Feature Analysis

| Feature | Competitor A (MITRE SAF + Heimdall) | Competitor B (Microsoft SCT + DSC/LGPO) | Our Approach |
|---------|--------------------------------------|------------------------------------------|--------------|
| Multi-format ingest | Strong conversion ecosystem to/from many result formats | Focused on Windows baseline/policy artifacts | Ingest scanner-native outputs, normalize once, keep provenance |
| Apply/enforce capability | Primarily transformation/visualization, not a full Windows apply orchestrator | Strong Windows apply primitives (LGPO/DSC) | Combine enforcement orchestration with verification in one run model |
| Checklist/report export | Strong support for CKL/XCCDF/CSV-style outputs | Policy Analyzer exports comparison data; less checklist-centric | First-class deterministic checklist/evidence package output |
| Offline/disconnected operation | Heimdall Lite supports disconnected use | Works in enterprise/local Windows environments | Treat offline-first as non-negotiable for core mission loop |

## Sources

- HIGH: NIST SCAP project overview and SCAP 1.4 status (updated 2025-12-22): https://csrc.nist.gov/projects/security-content-automation-protocol
- HIGH: Microsoft Security Compliance Toolkit guide (updated 2025-08-18): https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/security-compliance-toolkit-10
- HIGH: Microsoft DSC overview (updated 2025-06-09): https://learn.microsoft.com/en-us/powershell/dsc/overview?view=dsc-3.0
- MEDIUM: Start-DscConfiguration command reference for apply semantics (last updated 2022-03-22): https://learn.microsoft.com/en-us/powershell/module/psdesiredstateconfiguration/start-dscconfiguration?view=dsc-1.1
- MEDIUM: MITRE SAF CLI README (format conversion, attestations, CKL/XCCDF export): https://raw.githubusercontent.com/mitre/saf/main/README.md
- MEDIUM: MITRE Heimdall README (multi-format visualization, DISA checklist/XCCDF outputs, disconnected use): https://raw.githubusercontent.com/mitre/heimdall2/master/README.md

---
*Feature research for: Windows compliance orchestration apply/verify/export workflows*
*Researched: 2026-02-20*

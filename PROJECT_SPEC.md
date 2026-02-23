# STIGForge Next - Rebuild Project Spec

Use this document as the source spec for GSD when starting a clean replacement project.

---

## 1) Product Identity

- **Name:** STIGForge Next
- **Mission:** Offline-first Windows hardening factory for compliance operations.
- **Core promise:**
  - If it can be automated, STIGForge automates it.
  - If it needs a human, STIGForge asks once, captures evidence, and reuses it safely.
- **Primary loop:** Build -> Apply -> Verify -> Prove

## 2) Problem Statement

Current workflows are fragmented across multiple tools and manual processes:

- STIG Viewer checklist editing
- SCAP/SCC scans
- Evaluate-STIG scans
- PowerSTIG/DSC enforcement
- GPO/LGPO enforcement
- Manual evidence collection and packaging

Quarterly DISA updates force repeated rework and inconsistent operator decisions. Evidence quality, traceability, and export consistency vary by operator.

## 3) Vision and Outcomes

### 3.1 Long-Term Vision

Make quarterly DISA updates routine:

1. Import content.
2. Rebase overlays and manual answers.
3. Rebuild/apply/verify.
4. Export submission-ready package.

### 3.2 Measurable Outcomes

- 1-click mission workflow for standard environments.
- Deterministic outputs for identical inputs.
- Strict per-STIG SCAP association (no broad false matches).
- Reusable manual answers with evidence autopilot.
- Full offline operation for production use.

## 4) Scope

### 4.1 In Scope (v1)

- Windows 11 + Windows Server 2019 baseline workflows.
- DISA content import (STIG, SCAP, GPO/LGPO, ADMX).
- Profile and overlay policy model.
- Build/apply/verify/manual/export pipeline.
- CKL, POA&M, and eMASS package export.
- Audit trail, hash integrity, deterministic packaging.
- WPF app + CLI parity for core workflows.

### 4.2 Out of Scope (v1)

- Direct eMASS API sync/upload.
- Enterprise-wide GPO management platform replacement.
- Universal perfect tool mapping across all vendor scanners.
- Internet dependency for critical workflows.

## 5) Personas

- **Operator/Admin:** Wants guided, low-friction execution with clear errors and next steps.
- **ISSO/ISSM:** Needs defensible evidence packages and repeatable export structure.
- **Maintainer:** Needs safe quarterly content update and rebase workflows.
- **Auditor/Reviewer:** Needs traceability, provenance, and integrity proof.

## 6) Product Principles

- Offline-first is mandatory.
- Safety-first defaults; no silent risky automation.
- Determinism and explainability over convenience hacks.
- Ambiguous mapping -> review-required (never silent auto-apply/auto-match).
- One canonical control model for all pipelines.

## 7) Functional Requirements

## 7.1 Content Ingestion

- Import compressed or raw sources for STIG/SCAP/GPO/LGPO/ADMX.
- Classify artifacts with confidence and dedupe repeated content.
- Persist metadata:
  - pack ID/name
  - benchmark IDs
  - release/version/date
  - source label
  - hash manifest
  - inferred applicability tags
- Produce import diagnostics with actionable reasons.

## 7.2 Canonical Control Model

Normalize all imported controls into `ControlRecord` with:

- internal GUID
- external IDs (V-, SV-, SRG, benchmark)
- title, discussion, check, fix, severity
- applicability tags and confidence
- automation mappings (SCAP, DSC/PowerSTIG, GPO, script)
- manual metadata (wizard prompt, allowed status/reason)
- revision provenance and source references

## 7.3 Profiles and Overlays

- **Profile dimensions:** OS target, role template, classification mode, automation mode, environment mode.
- **Overlay dimensions:** status overrides, waivers, NA reasons, manual answers, evidence recipe overrides.
- Deterministic overlay precedence and conflict resolution.
- Profile-level policy:
  - `new_rule_grace_days`
  - `auto_apply_requires_mapping`
  - confidence thresholds for automation and scope filtering

## 7.4 Classification Scope Filtering

- Support `classified`, `unclassified`, and `mixed` modes.
- Auto-NA only when confidence meets policy threshold.
- Emit `na_scope_filter_report.csv` with reason and confidence.
- Ambiguous scope classification must enter review queue.

## 7.5 Release-Age Automation Gate

- New/changed controls are review-required by default.
- Auto-apply only after grace period and only with trusted mapping.
- Missing/ambiguous release date blocks auto-apply.

## 7.6 Build Pipeline

Input:

- profile
- selected pack set
- overlays
- policy options

Output deterministic bundle layout:

- `Apply/`
- `Verify/`
- `Manual/`
- `Evidence/`
- `Reports/`
- `Manifest/`

## 7.7 Apply Pipeline

- Preflight gate checks:
  - admin elevation
  - OS/role compatibility
  - free disk, pending reboot
  - PowerShell host readiness
  - execution policy handling
  - constrained language detection
- Enforcement backends:
  - PowerSTIG/DSC (primary)
  - GPO/LGPO (optional)
  - script fallback for gaps
- Reboot-aware convergence loop with max-pass limits.
- Snapshot/rollback capture of critical policy state.

## 7.8 Verify Pipeline

- Wrapper support for SCAP/SCC and Evaluate-STIG.
- Normalize outputs into canonical result model.
- Preserve raw scanner artifacts and parser diagnostics.
- Link verification results back to canonical controls.

## 7.9 Strict STIG-to-SCAP Association (Critical Contract)

Must satisfy all of the following:

1. Mapping is computed per selected STIG.
2. Benchmark overlap is primary signal.
3. If multiple valid candidates remain, deterministic canonical selection is applied.
4. Fallback tag matching is strict:
   - feature SCAP (firewall/edge/etc) must overlap feature tags of that STIG
   - generic SCAP fallback requires explicit OS tag overlap
5. No confident match -> mark missing/review-required.
6. Never broad fallback one SCAP across unrelated STIG rows.

## 7.10 Manual Check Wizard

- Show only unresolved manual controls after policy filtering.
- Per-control panel includes:
  - plain language explanation
  - risk and operational impact
  - validation steps/commands
  - status entry (Pass/Fail/NA + reason)
  - evidence capture action
- Reusable answer files for repeated environment patterns.

## 7.11 Evidence Autopilot

- Control-level evidence recipes:
  - command capture
  - registry snapshots
  - policy exports
  - event excerpts
  - file collection
  - optional screenshot metadata references
- Auto-index each artifact with metadata and checksums.

## 7.12 Diff and Rebase

- Pack-to-pack diff:
  - added/removed controls
  - changed check/fix text
  - mapping deltas
- Overlay and answer rebase:
  - auto-carry for high-confidence matches
  - flagged review for uncertain carries
- Generate review workload summary and confidence report.

## 7.13 Export Pipeline

- Export CKL and standalone POA&M.
- Build deterministic eMASS submission package with:
  - manifests
  - scans
  - checklists/answers
  - POA&M
  - evidence tree
  - attestations
  - indices and checksums

## 7.14 Fleet Operations (v1-lite)

- WinRM status/apply/verify across host list.
- Concurrency control and per-host outcomes.
- Host-separated artifacts plus unified summary index.

## 7.15 Audit and Integrity

- Record all critical actions: import, profile edit, apply, verify, manual answer, export.
- Hash-chained audit log validation command.
- Full artifact SHA-256 manifest for package integrity.

## 8) System Architecture

Core project/module boundaries:

- `STIGForge.App` - WPF desktop orchestration UX
- `STIGForge.Cli` - automation/CI/operator command surface
- `STIGForge.Content` - parsers, inbox scanner, import classification
- `STIGForge.Core` - canonical models, policies, applicability/mapping rules
- `STIGForge.Build` - deterministic bundle compiler
- `STIGForge.Apply` - hardening orchestration and preflight
- `STIGForge.Verify` - scanner wrappers and result normalization
- `STIGForge.Evidence` - evidence collection and metadata indexing
- `STIGForge.Export` - CKL/POA&M/eMASS packaging
- `STIGForge.Reporting` - summaries, diffs, and diagnostics
- `STIGForge.Infrastructure` - filesystem/db/process/task scheduler/winrm integrations
- `STIGForge.Shared` - shared contracts and constants

## 9) Data Contracts (Required JSON Schemas)

Produce versioned schemas for:

- `ContentPack`
- `ControlRecord`
- `Profile`
- `Overlay`
- `BundleManifest`
- `VerificationResult`
- `EvidenceRecord`
- `ExportIndexEntry`

Each schema must define:

- required fields
- enum constraints
- provenance fields
- backward-compatibility strategy
- migration/version policy

## 10) Deterministic Output Contracts

For identical inputs (pack/profile/overlay/tool versions/policies), outputs must match:

- same file tree and naming convention
- same index ordering
- same status derivations
- same checksums (when timestamps are normalized by policy)

Any non-deterministic field must be explicitly labeled and isolated.

## 11) UX Requirements

Required screens/tabs:

- Content Packs
- Profiles
- Build
- Apply
- Verify
- Manual Wizard
- Reports/Exports
- Diffs/Rebase

Required UX qualities:

- clear severity statuses
- actionable diagnostics
- keyboard-friendly workflows
- accessibility baseline
- explicit selection/mapping rationale panels

## 12) Security Requirements

- Least privilege where possible; explicit elevation gates where needed.
- Script provenance checks and hash validation.
- Redaction policy for sensitive logs and support bundles.
- Tamper-evident audit trail.
- Safe defaults for automation and scope decisions.

## 13) Testing Strategy

## 13.1 Unit Tests

- Parser correctness and classifier confidence behavior.
- Applicability and policy rule engine.
- strict STIG-to-SCAP association rules.
- overlay merge and rebase confidence scoring.

## 13.2 Integration Tests

- import -> build -> verify contract flow with fixtures.
- scanner wrapper parse normalization.
- export package structure/index/checksum correctness.

## 13.3 Contract Tests

- bundle structure contract
- export index contract
- critical UI XAML layout/style contracts

## 13.4 End-to-End Scenarios

- Win11 workstation classified profile
- Server2019 domain controller classified profile
- offline/air-gap execution flow

## 14) Acceptance Criteria (MVP Gate)

Release candidate must pass all:

- quarterly pack import and indexing works
- classified scope filtering works with traceable auto-NA report
- deterministic bundle build contract validated
- at least one apply backend completes successfully
- at least one verify backend completes successfully
- manual wizard + evidence autopilot operational
- CKL + POA&M + eMASS exports validated with indices/hashes
- strict per-STIG SCAP mapping behavior enforced by tests

## 15) Milestone Plan

- **M1 Foundation:** schemas, import engine, canonical model, profile system
- **M2 Execution Core:** build/apply/verify orchestration and manifests
- **M3 Human Loop:** manual wizard, answer files, evidence autopilot
- **M4 Export:** CKL/POA&M/eMASS packaging and index contracts
- **M5 Lifecycle:** diff/rebase engine and quarterly update workflow
- **M6 Fleet + hardening:** remote ops, resilience, release polish

## 16) Explicit Deliverables for GSD

GSD output must include:

- architecture and module contract docs
- canonical JSON schemas listed above
- STIG-to-SCAP association algorithm + pseudocode
- diff/rebase algorithm + confidence scoring spec
- security model and rollback strategy
- release gate checklist and evidence matrix
- sample output bundles (Win11 and Server2019 classified)
- executable roadmap with phase-level acceptance tests

## 17) Constraints

- offline-first mandatory
- deterministic outputs mandatory
- no dependence on STIG Viewer for normal operations
- safe defaults; no broad dangerous exceptions

## 18) Recommended GSD Prompt Starter

Use this exact kickoff line with this file:

`Create a new project from PROJECT_SPEC.md. Build a roadmap with phase gates, explicit schema contracts, strict per-STIG SCAP mapping invariants, deterministic output guarantees, and a test-first execution plan.`

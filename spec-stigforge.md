# Mega-Spec Prompt: STIGForge (One-App Hardening + Verify + Prove)

You are a senior product engineer + security automation architect. Design and specify an application called STIGForge that makes building compliant Windows 11 and Windows Server 2019 images push-button easy.

## Vision Statement

Write a strong vision statement that can be carried forward for years. The app's promise:

If it can be automated, STIGForge automates it.

If it requires a human, STIGForge asks once, captures evidence, and never makes the operator chase it again.

Turn quarterly DISA content updates into an easy "import + rebase + rebuild" flow.

End "tool thrash" (STIG Viewer back-and-forth) by converting manual checks into a guided wizard with answer files and evidence autopilot.

## 1) Problem & Goals

### Problem

Operators currently waste time bouncing between:

- STIG Viewer (manual checklist entry, NA decisions, comments)
- SCAP/SCC results
- Evaluate-STIG results
- PowerSTIG/DSC and scripts
- GPO/LGPO application
- Evidence collection and eMASS packaging

Quarterly DISA releases force rework. Manual checks + evidence are inconsistent.

### Primary Goals

- One pipeline: Build → Apply → Verify → Prove
- Import quarterly DISA content (STIG/SCAP/GPO) as Content Packs
- Create hardened Win11/Server2019 images/templates with minimal thinking
- Replace STIG Viewer workflows with a Manual Check Wizard + reusable Answer Files
- Evidence Autopilot: one click collects evidence per control
- Role templates: Workstation / Member Server / Domain Controller / Lab VM
- Offline-first: create a complete USB/ISO bundle to run air-gapped
- Export CKL, POA&M, evidence, and a submission-ready eMASS package

### Non-goals (v1 guardrails)

- No perfect 100% mapping across all tools on day one
- No direct eMASS API integration in v1 (export only)
- No full enterprise GPO management platform (consume/apply only)

## 2) User Stories (include these and add more)

- As a Sailor/operator, I select a profile and click Build, then Apply, then Verify, then export eMASS package.
- As an ISSO/ISSM, I need CKL/POA&M + scans + evidence in a consistent structure with audit trail and hashes.
- As a maintainer, I import new quarterly DISA pack and app shows What changed and reuses my overlays/answers safely.

## 3) Core Concepts & Data Model (must be explicit)

### 3.1 Content Packs (quarterly ingestion)

A Content Pack is a versioned library entry created by uploading DISA zips/files:

- STIG benchmark content (e.g., XCCDF XML and related)
- SCAP benchmark bundles (XCCDF/OVAL)
- STIG GPO packages (GPO backups/exports)
- Optional supporting scripts

Pack metadata must include:

- pack name like Q1_2026
- benchmark IDs
- version/release/date
- hash manifest of imported artifacts
- OS applicability (Win11 / Server 2019)

### 3.2 Canonical ControlRecord (single source of truth)

Normalize each STIG rule into a ControlRecord:

- internal GUID
- external IDs: Vuln ID (V-), Rule ID (SV-), SRG ID (if present), benchmark ID
- title, discussion, check text, fix text, severity
- automation pointers:
  - SCAP mapping (XCCDF/OVAL references)
  - Evaluate-STIG mapping (if supported)
  - DSC/PowerSTIG mapping (if supported)
  - GPO mapping (if available)
- applicability:
  - OS targets
  - role tags
  - classification_scope: classified_only | unclassified_only | both | unknown
  - confidence high|medium|low
- manual fields:
  - is_manual
  - wizard prompt
  - allowed responses (Pass/Fail/NA + reasons)
  - evidence recipe (see below)
- revision info: pack, version/release, dates

### 3.3 Profile + Overlay system (Baseline as Code)

Profiles define target and policies:

- OS target
- role template
- environment mode: audit_only | safe | full
- classification_mode: classified | unclassified | mixed
- NA policies, including scope filtering
- overlay list applied in order

Overlays persist enterprise decisions across quarters:

- status overrides, NA reasons, waivers, notes
- manual answers (answer file records)
- evidence recipe overrides
- global rules: "always NA these patterns," "never apply these in safe mode," etc.

## 4) Classification Scope Auto-NA (must match THIS requirement)

We are operating in a CLASSIFIED environment.
Add a profile option that automatically marks UNCLASSIFIED-ONLY controls as Not Applicable to reduce noise.

Requirements:

- Profile setting: classification_mode = classified
- Toggle: "Auto-NA controls out of scope"
- Confidence threshold: high (default) | medium | aggressive

Every auto-NA must produce:

- NA status
- standard comment template
- entry in na_scope_filter_report.csv with match reason + confidence

Ambiguous items go to a "Review required" queue (never silently NA).

Also support inverse (unclassified environments) as optional, but primary is classified.

## 5) Pipeline & Workflows (ELI5-friendly UX must be achievable)

### 5.1 Build

Input: Profile + Content Pack + overlays + policies

Output: Offline-first bundle with deterministic structure:

- Apply/
- Verify/
- Manual/
- Evidence/
- Reports/
- Manifest/ (hashes, versions, logs)

### 5.2 Apply (hardening)

Must support:

- PowerSTIG/DSC application (primary)
- optional GPO/LGPO application
- scripts for gaps (registry/secpol/auditpol/service config)

Must have:

- safe/full mode behavior
- conflict detection (GPO vs DSC vs scripts)
- rollback snapshot (at least policy exports / key state backups)

### 5.3 Verify

Must support:

- SCAP/SCC benchmark execution wrapper
- Evaluate-STIG execution wrapper

Normalize all results into:

- consolidated JSON + CSV
- per-control status, evidence links, scan source links, timestamps, tool versions

### 5.4 Manual Check Wizard + Answer Files

Replace STIG Viewer thrash:

- show only manual controls (after applicability + auto-NA)
- group into logical pages
- for each manual control:
  - plain-English prompt
  - Pass/Fail/NA + reason
  - Collect evidence button (Evidence Autopilot)
  - attachments + notes

Answer Files:

- reusable
- optionally expiring (v2)
- Export CKL pre-filled with results and comments.

## 6) "Spicy" Features (must be included)

### 6.1 "What changed between STIG releases?"

Provide a baseline diff UI and rebase engine:

- new controls, removed, changed check logic, changed fix text
- highlight new/changed manual items
- overlay impact summary ("your exceptions touched X changed controls")
- rebase overlays safely:
  - auto-carry when rule IDs and check hashes match
  - otherwise flag for review with suggested mapping confidence

### 6.2 "Explain this control like I'm helpdesk"

For each control generate:

- what it's trying to prevent (plain English)
- what it might break
- how to validate (commands + expected result)
- how STIGForge enforces it (DSC/GPO/script/manual)
- link to collected evidence

### 6.3 Evidence Autopilot

Evidence recipes per control:

- command execution (PowerShell/cmd)
- exports (policy, auditpol, event logs, registry snapshots)
- file collection
- optional screenshots

Store:

- evidence files
- metadata: command, timestamp, context

Attach evidence to the control automatically.

### 6.4 Role templates

Templates control:

- applicability
- safe/full remediation sets
- default NA patterns
- required attestations

### 6.5 Offline-first

Everything must run without internet:

- content packs stored locally
- bundle contains all runners, benchmarks, scripts, report templates

## 7) eMASS Export Package (must be included)

Add export feature: eMASS Submission Package.

### 7.1 Export types

- System package (one per system/build)
- Optional batch package (multi-system; v2)

### 7.2 Package structure (deterministic)

Root:
```
EMASS_<System>_<OS>_<Role>_<Profile>_<Pack>_<YYYYMMDD-HHMM>/
```

Include:

- 00_Manifest/ (manifest.json, hashes, logs)
- 01_Scans/ (raw + consolidated)
- 02_Checklists/ (CKLs + answer files)
- 03_POAM/ (xlsx/csv + normalized json)
- 04_Evidence/ (by_control/<V- or RuleID>/ files + metadata)
- 05_Attestations/ (attestation records)
- 06_Index/ (index.html offline + CSV indices, NA scope report)
- README_Submission.txt explaining where artifacts are

### 7.3 Index mapping requirements

Generate control_evidence_index.csv containing:

- Vuln ID, Rule ID, Title, Severity
- Status (Pass/Fail/NA/Open)
- NA reason + policy origin (e.g., classification scope filter)
- Evidence file paths
- Scan source file paths
- Last verified timestamp

Generate hashes for all files.

## 8) UI Screens (minimal but complete)

- Content Packs (import, validate, metadata view)
- Profiles (role/classification/safe mode/overlays)
- Build (compile bundle)
- Apply (execute hardening)
- Verify (execute scans + normalized results)
- Manual Wizard (answer + evidence autopilot)
- Reports (dashboard + export CKL/POA&M/eMASS)
- Diffs (What changed + rebase overlays)

## 9) Logging, Audit Trail, and Integrity

Every run must log:

- system identity (hostname, OS build)
- profile + overlays applied
- content pack versions + hashes
- tool versions
- per-control outcomes and why (including auto-NA reasons)
- evidence metadata

Include SHA-256 hashes for export package.

## 10) Acceptance Criteria (MVP v1)

Must satisfy:

- Import quarterly content packs and index controls
- Create profile with classification mode = classified and auto-NA unclassified-only controls
- Build offline bundle with deterministic structure
- Apply at least one enforcement backend (DSC/PowerSTIG and/or scripts)
- Verify via at least one scan backend (SCAP or Evaluate-STIG)
- Manual Wizard functions with evidence autopilot for common evidence types
- Export CKL + POA&M + evidence zip
- Export complete eMASS submission package with indices + hashes

## 11) Milestones (practical build order)

- MVP Orchestrator (Build/Apply/Verify + consolidated results)
- Manual Wizard + Answer Files + Evidence Autopilot (big ROI)
- Content Pack rebase + "What changed" diffs + overlay review queue
- SCCM packaging + drift detection (v2/v3)

## 12) Deliverables from you (the agent)

Produce:

- Full system architecture diagram (text-based ok)
- Detailed module list + interfaces/contracts
- Canonical JSON schemas for ContentPack, ControlRecord, Profile, Overlay, manifest.json
- Rebase algorithm pseudocode and confidence scoring approach
- Classification scope detection rules (tiered: explicit metadata → strong text → review)
- eMASS export generator design (folder builder + index builder + hashing)
- Test plan:
  - simulate common fails (unsigned exe, user-profile paths, MSI installer, scripts)
  - verify NA scope filter correctness
  - verify diff/rebase carries safely
- Security considerations + rollback strategy
- Example bundle output for:
  - Win11 Workstation Classified profile
  - Server 2019 DC Classified profile

Constraints:

- Offline-first
- Deterministic outputs
- No reliance on STIG Viewer for normal ops
- Favor safe defaults; never suggest dangerous broad exceptions

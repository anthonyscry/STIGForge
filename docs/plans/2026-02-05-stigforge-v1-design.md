# STIG Forge v1 Design

## 1) Vision statement and north-star outcomes

STIG Forge is an offline-first Windows hardening workbench that turns quarterly DISA updates into a predictable, auditable workflow. It helps mixed teams auto-apply safe baselines, handle the remaining manual checks, and package evidence without cloud dependencies. It makes changes visible by highlighting what is new or changed between releases and by recording every NA decision. It ships fast with a focused v1 while keeping a clean path to scale into multi-system operations.

North-star outcomes:
- Quarterly updates go from download to diff to action list in under an hour.
- Fully unattended auto-apply covers the majority of auto-applicable settings.
- Manual checks are easy to answer and attach evidence to, without external tools.
- Evidence packages are accepted by eMASS reviewers without structural corrections.
- Everything works offline with a local database and deterministic exports.

## 2) ELI5 end-to-end workflow

You import the new DISA files into the app. The app compares them to the last release and shows what changed, especially new or changed manual checks. It then auto-applies everything it safely can using PowerSTIG/DSC and trusted PowerShell scripts. Next, it shows you the remaining manual checks, asks you simple questions, and lets you attach proof like screenshots or logs. You can also run scans or import results from SCAP, Evaluate-STIG, NIWC, or PowerSTIG evaluation outputs. When you are done, it builds an eMASS-ready package with your checklists, results, and evidence.

## 3) v1 scope, exclusions, success metrics

V1 scope (IN):
- Import quarterly DISA releases (STIG/SCAP/GPO) and detect changes vs prior release.
- Mark NA and reverse NA with stored rationale and carry-forward logic.
- What changed between releases view with manual-check highlights.
- Fully unattended auto-correct by default using PowerSTIG/DSC and vetted PowerShell scripts.
- Manual check wizard that prompts for text answers and saves to AnswerFile JSON.
- Import and export AnswerFile XML (Evaluate-STIG) and CKL files.
- Ingest SCAP/SCC, Evaluate-STIG, NIWC SCAP data, and PowerSTIG evaluation outputs into checklists.
- Evidence capture and export suitable for eMASS packages.

V1 exclusions (OUT):
- Multi-user RBAC, centralized servers, or cloud sync.
- Fleet or agent deployment across many systems.
- Full ATO project management or portfolio dashboards.
- Automated remediation for checks that require manual verification or unsafe changes.
- Custom policy authoring UI beyond importing profiles/overlays.

V1 success metrics:
- Import plus diff plus auto-apply completes in under 60 minutes on a typical pack.
- 90%+ of auto-applicable settings applied via PowerSTIG/DSC or scripts.
- What changed view has zero false negatives for changed manual checks in test sets.
- eMASS export accepted without structural corrections.
- Users complete end-to-end flow without external tools beyond SCAP/PowerShell.

## 4) Data model sketch

Key entities:
- Release: quarterly pack import (id, name, date, source, tool versions).
- Benchmark/STIG: grouping (title, version, type: STIG/SCAP/GPO).
- Check: canonical control (VulnId, RuleId, title, severity, discussion/check/fix, manual flag, references).
- TailoringRule/NA: scoped rules (predicate, decision, reason, carry-forward behavior).
- AnswerFile (JSON canonical): profileId, packId, createdAt, answers[].
- ManualAnswer: RuleId/VulnId, status, reason, comment, updatedAt.
- ScanResult: per-run record (tool label, timestamps, raw artifact paths, tool versions).
- CheckResult: per-check normalized result with status, source, evidence links.
- Evidence: files and metadata (command, timestamp, context) linked to Check.
- Asset: optional in v1, minimal identity for scoping results.
- Diff: release comparison (new/removed/changed, manual highlights, hash diffs).

## 5) Architecture proposal

WPF MVVM app with a thin UI layer, a service layer for domain actions, and a local SQLite database for metadata. Raw artifacts and evidence stay on disk with indexed paths. Services: ImportService (XCCDF/OVAL/CKL/GPO), DiffService (release comparison with content hashes), TailoringService (NA rules and carry-forward), AutoApplyService (PowerSTIG/DSC and PowerShell orchestrator), VerifyService (SCAP/Evaluate-STIG/NIWC ingestion), EvidenceService (capture and attach), ExportService (eMASS package builder).

Quarterly updates create a new Release, run DiffService against the prior Release, and rebase tailoring and AnswerFiles forward using RuleId/VulnId and content hashes. Ambiguous matches are flagged. Auto-apply runs immediately after import (fully unattended default) and emits a deterministic run log and rollback guidance. Manual checks are filtered after auto-apply and scan ingestion; the wizard prompts for text answers, stores JSON, and can export Evaluate-STIG AnswerFile.xml and CKL.

Offline-first: SQLite for metadata, file system for packs/bundles/evidence, deterministic export folders, and no cloud dependencies.

## 6) Feature backlog by phase

v1:
- Import DISA packs (STIG/SCAP/GPO) with basic XCCDF parsing.
- Unattended auto-apply via PowerSTIG/DSC and PowerShell scripts.
- Manual Check Wizard and AnswerFile JSON with import/export to Evaluate-STIG XML + CKL.
- What changed between releases diff with manual highlights.
- Ingest SCAP/SCC, Evaluate-STIG, NIWC SCAP data, and PowerSTIG evaluation outputs.
- Evidence capture and eMASS export package with hashes.
- Basic remediation guidance for remaining checks.

v1.5:
- Improved XCCDF parsing fidelity and ID normalization.
- Coverage reporting and post-auto-apply delta views.
- Role-based NA templates and better rebase heuristics.
- Evidence autopilot recipes per control.
- Safer rollback guidance and change-impact summaries.
- UI quality: bulk actions, search, and filters.
- Release history and notes.

v2:
- Asset management and batch scans/exports.
- Project or ATO workspaces and cross-release dashboards.
- Advanced diff logic with confidence scoring.
- Optional remote storage and sync (still offline-capable).
- Policy authoring tools for custom overlays.
- Delegated workflows and approvals.

## 7) Risks and mitigations

- Performance on large packs: streaming XML parsing, background indexing, cached hashes.
- Parsing variability: tolerant parsers with fixtures for known DISA formats.
- False positives in diffs: use RuleId/VulnId plus content hashes and flag ambiguous mappings.
- Evidence integrity: hash evidence files and export a manifest with timestamps and tool versions.
- Auto-apply safety: detailed logs, post-run report, and rollback guidance; mark risky controls.
- Audit defensibility: preserve raw artifacts and provenance for every status.

## 8) Minimal test strategy

- Unit: XCCDF/OVAL parsers, CKL ingest, diff engine, AnswerFile JSON <-> Evaluate-STIG XML.
- Integration: import pack -> build bundle -> auto-apply (mocked) -> ingest scans -> export eMASS.
- UI smoke: import, diff, manual wizard, evidence attach, export.

# Phase 4: Human Resolution and Evidence Continuity - Context

**Gathered:** 2026-02-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver manual control resolution with reusable answer files, evidence autopilot with control-level artifacts, and answer rebase for carrying decisions across pack updates. This phase closes the gap between automated apply/verify (Phase 3) and export (Phase 5) by ensuring every control has a resolution — automated or manual — with evidence attached. Existing services (ManualAnswerService, EvidenceCollector, OverlayRebaseService, BaselineDiffService) are wired together with new answer rebase logic and answer file portability.

</domain>

<decisions>
## Implementation Decisions

### Manual Wizard Workflow
- ManualCheckWizard already shows only unresolved controls via GetUnansweredControls() — preserve this pattern
- Wizard captures status, reason, and comment per control — no changes to the wizard flow
- ManualAnswerService already validates reasons (rejects "N/A", "TBD", placeholders) — keep this enforcement
- No batch-answer mode for v1 — each control gets individual attention (compliance requirement)
- Wizard should show CheckText and FixText inline so operators don't need to reference external documents

### Answer File Reusability
- Export/import answers as standalone JSON files for cross-bundle and cross-system reuse
- Export format: AnswerFile with metadata header (profileId, packId, stigId, exportedAt, exportedBy) + answers array
- Import matches by RuleId (stable across versions) — VulnId used as secondary match
- Import conflict resolution: imported answer overwrites existing only if existing is still "Open/NotReviewed"
- CLI commands: `export-answers --bundle B --output answers.json` and `import-answers --bundle B --file answers.json`
- WPF: export/import buttons on ManualView toolbar
- No CSV export for answers — JSON is the canonical format (CSV loses structured reason/comment data)

### Answer Rebase Logic (Critical Gap)
- Follow OverlayRebaseService pattern: confidence scoring (0.0-1.0) with action types
- AnswerRebaseService takes baseline answers + BaselineDiff and produces AnswerRebaseReport
- Action types: Carry (unchanged control), CarryWithWarning (minor text change), ReviewRequired (major text change), Remove (control deleted), Remap (RuleId changed)
- Confidence thresholds: >= 0.8 auto-carry, 0.5-0.8 carry-with-warning, < 0.5 review-required
- Blocking conflicts: removed controls with existing answers block until operator resolves
- Answer rebase writes `answers_rebased.json` with lineage tracking (originalAnswerAt, rebasedAt, rebaseConfidence)
- CLI: `rebase-answers --bundle B --baseline PACK_A --target PACK_B --apply --output report.md`
- WPF: AnswerRebaseWizard follows same 3-step pattern as RebaseWizard (Select → Analyze → Apply)

### Evidence Autopilot Enhancement
- EvidenceAutopilot already handles registry, config files, command output, service status, user rights
- Add evidence index service: query all evidence for a control, filter by type/run/tag
- Evidence lineage via SupersedesEvidenceId is already modeled — wire it into the index for cross-run queries
- No new evidence types for v1 — existing Command/File/Registry/PolicyExport/Screenshot/Other covers the domain
- Evidence index stored as `Evidence/evidence_index.json` in bundle — flat file, no database needed

### Pack Diff Impact on Answers
- Extend BaselineDiff output to include answer impact assessment when answers exist
- For each modified control in diff: flag whether existing answer is "still valid", "uncertain", or "invalid"
- Validity logic: CheckText unchanged = valid, CheckText changed but FixText same = uncertain, both changed = invalid
- This assessment surfaces in both diff CLI output and DiffViewer WPF
- No automatic answer invalidation — operators review and confirm

### Claude's Discretion
- Exact AnswerRebaseReport JSON schema
- Evidence index query API design (method signatures on the service)
- Whether to show answer validity in the existing DiffViewer tabs or add a new "Answer Impact" tab
- AnswerRebaseWizard visual layout details
- Test coverage strategy for answer rebase edge cases

</decisions>

<specifics>
## Specific Ideas

- Answer rebase is the mirror of overlay rebase — same confidence model, same blocking semantics, same report pattern
- The import/export flow enables "golden answer files" that teams can share across systems with same STIG profile
- Evidence index should be lightweight — a JSON manifest, not a queryable database; keep it simple for v1
- Pack diff + answer impact gives operators a single view of "what changed and what do I need to re-review"
- Rebase reports (overlay and answer) should be side-by-side in the WPF rebase wizard for operators doing both at once

</specifics>

<deferred>
## Deferred Ideas

- Batch-answer mode for high-volume manual resolution — future enhancement if operators request it
- Evidence discovery by content similarity (find similar artifacts across controls) — Phase 6+
- Answer templates per STIG category — would reduce manual effort but needs design
- Multi-system answer synchronization — Phase 5 (Fleet-lite) would be the natural home
- Answer approval workflow (dual-review for high-severity controls) — future compliance enhancement

</deferred>

---

*Phase: 04-human-resolution-and-evidence-continuity*
*Context gathered: 2026-02-22*

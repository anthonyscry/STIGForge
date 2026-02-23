# Phase 4 Research: Human Resolution and Evidence Continuity

**Researched:** 2026-02-22
**Phase:** 04-human-resolution-and-evidence-continuity
**Requirements:** MAN-01, EVD-01, REB-01, REB-02

## Executive Summary

Phase 4 bridges the gap between automated mission execution (Phase 3) and proof packaging (Phase 5). It delivers four capabilities: (1) answer file export/import for manual control portability, (2) evidence index service for control-level artifact querying, (3) pack diff with answer impact assessment, and (4) answer rebase for carrying manual decisions across STIG version updates. The existing codebase provides strong foundations: ManualAnswerService, EvidenceCollector, BaselineDiffService, and OverlayRebaseService are all implemented. Phase 4 extends these with new services, CLI commands, and WPF views.

## Existing Foundation Analysis

### ManualAnswerService (STIGForge.Core.Services)
- **What exists:** Complete answer CRUD — LoadAnswerFile, SaveAnswer, GetAnswer, GetUnansweredControls, GetProgressStats
- **AnswerFile model:** ProfileId, PackId, CreatedAt, List<ManualAnswer>
- **ManualAnswer model:** RuleId, VulnId, Status, Reason, Comment, UpdatedAt
- **Storage:** `{bundleRoot}/Manual/answers.json`
- **Matching:** By RuleId primary, VulnId secondary (case-insensitive)
- **Validation:** PlaceholderReasons rejection (na, n/a, none, unknown, test, tbd), RequiresReason for Fail/NotApplicable
- **What's missing for MAN-01:** Export/import answer files as standalone JSON, metadata header (stigId, exportedAt, exportedBy), import conflict resolution (only overwrite Open/NotReviewed), CLI export-answers/import-answers commands, WPF toolbar buttons

### EvidenceCollector (STIGForge.Evidence)
- **What exists:** WriteEvidence with SHA-256 checksums, EvidenceMetadata with full provenance (RunId, StepName, SupersedesEvidenceId), organized in `Evidence/by_control/{controlKey}/`
- **EvidenceAutopilot:** Automated collection by control analysis (registry, config, command, service, user rights)
- **Models:** EvidenceArtifactType enum (Command, File, Registry, PolicyExport, Screenshot, Other), EvidenceWriteRequest, EvidenceWriteResult
- **What's missing for EVD-01:** Evidence index service (query all evidence for a control, filter by type/run/tag), evidence_index.json manifest in bundle, lineage wiring via SupersedesEvidenceId for cross-run queries

### BaselineDiffService (STIGForge.Core.Services)
- **What exists:** Full pack comparison (added, removed, modified controls), FieldChangeImpact (Low/Medium/High), review-required routing
- **Control matching:** RuleId > VulnId > ControlId (same as ManualAnswerService)
- **CLI:** diff-packs command with --baseline, --target, --output, --json flags, markdown and JSON output
- **WPF:** DiffViewer with ViewModel
- **What's missing for REB-01:** Answer impact assessment on diff results — for each modified control, flag whether existing answer is "still valid", "uncertain", or "invalid" based on CheckText/FixText changes

### OverlayRebaseService (STIGForge.Core.Services)
- **What exists:** Complete overlay rebase with confidence scoring (0.0-1.0), RebaseActionType (Keep/KeepWithWarning/ReviewRequired/Remove/Remap), blocking conflict enforcement
- **Pattern:** Takes overlay + BaselineDiff, produces RebaseReport with actions
- **WPF:** RebaseWizard with 3-step flow (Welcome/Select -> Analysis -> Completion)
- **What's needed for REB-02:** Mirror this pattern for AnswerRebaseService — takes AnswerFile + BaselineDiff, produces AnswerRebaseReport. Same confidence model, same blocking semantics. CLI rebase-answers command. WPF AnswerRebaseWizard.

## Architecture Decisions

### Answer File Export/Import Format
The AnswerFile model already has ProfileId and PackId. For export, extend with: `StigId`, `ExportedAt`, `ExportedBy` in the export metadata header. Import matches by RuleId (stable across versions), VulnId as fallback. Import only overwrites "Open" or "NotReviewed" answers — this prevents clobbering operator decisions.

### Evidence Index Service Design
A lightweight `EvidenceIndexService` that scans `Evidence/by_control/` directories, reads `.json` metadata files, and builds an in-memory index. Writes `Evidence/evidence_index.json` as a flat manifest. Query methods: `GetEvidenceForControl(controlKey)`, `GetEvidenceByType(type)`, `GetEvidenceByRun(runId)`, `GetEvidenceByTag(key, value)`. Lineage: follows SupersedesEvidenceId chains.

### Answer Impact on Diff
Extend BaselineDiff output model with optional `AnswerImpact` per ControlDiff. Validity logic per CONTEXT.md decisions:
- CheckText unchanged = "valid"
- CheckText changed but FixText same = "uncertain"
- Both changed = "invalid"
This is computed lazily when answers exist for a bundle.

### Answer Rebase Service
Mirror OverlayRebaseService exactly:
- AnswerRebaseService takes AnswerFile + BaselineDiff -> AnswerRebaseReport
- Action types: Carry (unchanged), CarryWithWarning (minor change), ReviewRequired (major change), Remove (deleted), Remap (RuleId changed)
- Confidence: >= 0.8 auto-carry, 0.5-0.8 carry-with-warning, < 0.5 review-required
- Blocking: removed controls with existing answers block until resolved
- Output: answers_rebased.json with lineage (originalAnswerAt, rebasedAt, rebaseConfidence)

## Risk Assessment

### Low Risk
- Answer export/import — straightforward JSON serialization extending existing AnswerFile model
- Evidence index — read-only scan of existing evidence directory structure

### Medium Risk
- Answer impact on diff — requires integration with ManualAnswerService from BaselineDiffService (cross-service dependency)
- Answer rebase — new service but mirrors proven OverlayRebaseService pattern closely

### High Risk
- None identified — all capabilities extend existing, well-tested patterns

## Dependency Map

```
MAN-01 (Answer Export/Import)
  ├── ManualAnswerService (existing)
  ├── AnswerFile model (existing, needs export metadata)
  └── CLI + WPF (new commands/buttons)

EVD-01 (Evidence Index)
  ├── EvidenceCollector (existing)
  ├── EvidenceMetadata model (existing)
  └── EvidenceIndexService (new)

REB-01 (Pack Diff + Answer Impact)
  ├── BaselineDiffService (existing)
  ├── ManualAnswerService (existing, for answer lookup)
  └── AnswerImpact model (new)

REB-02 (Answer Rebase)
  ├── BaselineDiffService (existing)
  ├── ManualAnswerService (existing)
  ├── OverlayRebaseService (pattern reference)
  └── AnswerRebaseService (new)
```

## Plan Decomposition Recommendation

**Wave 1** (independent, can parallelize):
1. Answer file export/import + CLI commands (MAN-01) — extends ManualAnswerService
2. Evidence index service + CLI command (EVD-01) — new standalone service

**Wave 2** (depends on Wave 1 for answer file patterns):
3. Pack diff answer impact + DiffViewer enhancement (REB-01) — extends BaselineDiffService
4. Answer rebase service + CLI + WPF wizard (REB-02) — new service mirroring overlay rebase

## RESEARCH COMPLETE

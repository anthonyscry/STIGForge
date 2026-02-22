---
phase: 04-human-resolution-and-evidence-continuity
plan: 03
status: complete
duration: ~8 min
---

# Plan 04-03 Summary: Answer Rebase and Diff Impact

## What was built
Answer impact assessment on BaselineDiff and AnswerRebaseService with confidence-scored rebase actions mirroring OverlayRebaseService pattern. Extended diff-packs CLI with --bundle for answer impact display, added rebase-answers CLI command.

## Key files

### Created
- `src/STIGForge.Core/Services/AnswerRebaseService.cs` — AnswerRebaseService, AnswerRebaseReport, AnswerRebaseAction, AnswerRebaseActionType
- `tests/STIGForge.UnitTests/Core/AnswerImpactTests.cs` — 4 unit tests for answer impact assessment
- `tests/STIGForge.UnitTests/Core/AnswerRebaseServiceTests.cs` — 6 unit tests for answer rebase service

### Modified
- `src/STIGForge.Core/Services/BaselineDiffService.cs` — Added AnswerValidity enum, AnswerImpact class, AssessAnswerImpact extension, AnswerImpact property on ControlDiff
- `src/STIGForge.Cli/Commands/DiffRebaseCommands.cs` — Added --bundle to diff-packs, new rebase-answers command

## Decisions
- Confidence thresholds from CONTEXT.md: >= 0.8 auto-carry, 0.5-0.8 carry-with-warning, < 0.5 review-required
- Removed controls with existing answers are blocking conflicts (IsBlockingConflict=true)
- High-impact field changes (CheckText, Severity, IsManual) -> ReviewRequired at 0.4 confidence
- Medium-impact (FixText) -> CarryWithWarning at 0.7 confidence
- Low-impact (Title, Discussion) -> Carry at 0.9 confidence
- Rebase metadata carried in ManualAnswer.Comment field as "[REBASED: {confidence}]" prefix
- ApplyAnswerRebase throws InvalidOperationException if blocking conflicts exist (same as OverlayRebaseService)

## Self-Check: PASSED
- [x] AnswerValidity enum (Valid, Uncertain, Invalid, NoAnswer)
- [x] AssessAnswerImpact extension method on BaselineDiff
- [x] AnswerRebaseService with confidence-scored actions
- [x] Blocking conflicts for removed controls
- [x] diff-packs --bundle displays answer impact
- [x] rebase-answers CLI with --apply writing answers_rebased.json
- [x] 10/10 unit tests passing (4 impact + 6 rebase)

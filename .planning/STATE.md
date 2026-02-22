# Project State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-02-20)

**Core value:** Deterministic offline hardening missions with reusable manual evidence and defensible submission artifacts.
**Current focus:** Phase 2 - Policy Scope and Safety Gates (COMPLETE)

## Current Position

Phase: 2 of 5 (Policy Scope and Safety Gates)
Plan: 5 of 5 in current phase
Status: Complete
Last activity: 2026-02-22 - Phase 2 complete: all 5 plans executed across 2 waves.

Progress: [=========>] 40%

## Performance Metrics

**Velocity:**
- Total plans completed: 9
- Average duration: 5 min
- Total execution time: ~50 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 1 | 4 | 27 min | 7 min |
| 2 | 5 | 23 min | 5 min |
| 3 | 0 | 0 | n/a |
| 4 | 0 | 0 | n/a |
| 5 | 0 | 0 | n/a |

**Recent Trend:**
- Last 5 plans: 02-01 (5 min), 02-02 (3 min), 02-03 (5 min), 02-04 (4 min), 02-05 (6 min)
- Trend: Stable, faster than Phase 1

## Accumulated Context

### Decisions

- [Phase 1] Canonical ingestion and schema contracts remain the first delivery boundary.
- [Phase 2] Policy/scope/safety gates stay isolated before host execution.
- [Phase 3] Build/apply/verify are grouped to deliver a complete executable mission loop with strict SCAP mapping.
- [01-01] Store enum values as TEXT strings in SQLite and parse via Enum.Parse in repository mapping layer.
- [01-01] Use PRAGMA journal_mode=WAL to prevent SQLite lock contention under concurrent CLI/WPF access.
- [01-01] Restrict run mutations to status/finishedAt/detail only; timeline events are append-only with no update path.
- [01-02] ImportOperationState is a planning-level lifecycle distinct from ImportStage (execution-level crash-recovery checkpoints); both coexist.
- [01-02] ExecutePlannedImportAsync mutates PlannedContentImport in-place to avoid wrapping existing return types.
- [01-02] StagedOutcomes emitted deterministically for all operations regardless of success or failure for auditable summaries.
- [01-03] Timeline emission is non-blocking: IMissionRunRepository failures emit Trace warnings to avoid aborting compliance-critical mission flows.
- [01-03] EvidenceCollector is an optional ApplyRunner constructor param; existing DI registrations don't require changes.
- [01-03] SHA-256 rerun deduplication uses apply_run.json for per-step comparison; mission ledger provides authoritative timeline history.
- [01-04] BundleMissionSummaryService accepts IMissionRunRepository as optional constructor param; returns null from LoadTimelineSummaryAsync when not configured.
- [01-04] MissionTimelineSummary.IsBlocked is derived from presence of any Failed event in the run's timeline (not run.Status alone).
- [01-04] mission-timeline is a standalone CLI command (not a mode of bundle-summary) to keep single-responsibility.
- [01-04] RefreshTimelineAsync is fire-and-forget to avoid delaying apply/verify/orchestrate completion notification.
- [02-01] ProfileValidator is a plain class (no interface needed) — pure logic with no I/O.
- [02-01] Profile create with --from-json auto-generates ProfileId if empty.
- [02-02] MeetsThreshold maps High=3, Medium=2, Low=1 and compares actual >= threshold.
- [02-02] FilterControls() left unchanged — pre-build filtering separate from Compile().
- [02-03] Blocking conflict = different StatusOverride values; non-blocking = same status, different details.
- [02-03] Control key prefers RuleId, falls back to VulnId.
- [02-03] Break-glass (ForceAutoApply) allows proceeding past blocking overlay conflicts.
- [02-04] overlay diff creates temporary 2-element list (a at index 0, b at index 1) for last-wins precedence.
- [02-04] review-queue reads static CSV files from disk, no DI services needed.
- [02-05] Profile edits persist on Save, not during live mission runs.
- [02-05] Break-glass dialog minimum 8-character justification matches CLI pattern.

### Pending Todos

None yet.

### Blockers/Concerns

- [01-01] Pre-existing test `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` in CliHostFactoryTests is flaky (passes in isolation, occasionally fails in full suite). Not caused by Phase 2 changes.

## Session Continuity

Last session: 2026-02-22
Stopped at: Completed Phase 2 (all 5 plans)
Resume file: None

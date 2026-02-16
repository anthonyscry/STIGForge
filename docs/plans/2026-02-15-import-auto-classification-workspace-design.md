# Import Auto-Classification Workspace Design

Date: 2026-02-15
Status: Approved
Scope: STIGForge WPF Import tab readability and flow optimization

## Context

The current Import tab feels busy and requires too much manual flow management, which increases operator mistakes. The target outcome is to reduce mistakes by making import and classification automatic by default, while keeping high-signal exception handling visible and actionable.

Constraints:
- No new external dependencies.
- Preserve existing architecture and MVVM patterns.

## Goals

1. Auto-import and classify packs from the project `import/` folder.
2. Reduce user error by shifting from manual setup to exception-driven review.
3. Improve readability with clearer information hierarchy and subtabs.
4. Keep implementation risk moderate by reusing existing UI and service patterns.

## Non-Goals

1. Replacing the import backend architecture.
2. Introducing third-party UI frameworks.
3. Redesigning unrelated tabs.

## Chosen Approach

Use a guided, subtabbed Import workspace in the existing WPF app:

1. `Auto Import` - default automatic ingest pipeline and queue visibility.
2. `Classification Results` - grouped results and confidence-oriented review.
3. `Exceptions Queue` - only hard blockers requiring intervention.
4. `Activity Log` - end-to-end processing trace, warnings, and retries.

This approach balances strong usability gains with controlled code churn.

## Architecture

Keep Import inside current `MainViewModel.Import.*` structure and introduce an import-workspace coordinator that manages:

- Active subtab state.
- Queue lifecycle and phase transitions.
- Shared status counters and phase badges.
- Exception routing and retry orchestration.

UI remains a single Import workspace with explicit subtabs to reduce visual density while preserving discoverability.

## Components

### View/UI Components

- `AutoImportPanel`
  - Shows detected packs from `import/` and background processing state.
- `ClassificationResultsPanel`
  - Displays classified/ambiguous/failed buckets with sortable results.
- `ExceptionsQueuePanel`
  - Displays actionable exception cards with remediation actions.
- `ImportActivityLogPanel`
  - Shows processing timeline, warnings, and per-pack outcomes.
- `ImportStatusSummary`
  - Shared sticky summary (counts, phase badge, queue state).

### ViewModel/Service Responsibilities

- `ImportWorkspaceCoordinator` (within existing Import VM partials)
  - Coordinates subtab transitions and status projections.
- Existing import pipeline services
  - Add queue-based auto-run trigger and bucket projection.
- Persistence/audit
  - Commit imported metadata and controls; append audit records per pack.

## Data Flow

1. Import workspace detects candidate packs in the project `import/` directory.
2. Each pack enters queue processing automatically:
   - Parse -> Classify -> Bucket -> Validate.
3. If a pack has no hard blockers, it is auto-committed without manual confirmation.
4. Ambiguous cases attempt policy-default resolution where safe.
5. Hard blockers are isolated to `Exceptions Queue` for targeted user action.
6. Other packs continue processing in parallel queue order.
7. Retry action reprocesses only failed stages for the selected pack.

## Error Handling

Error classes:

- `recoverable`
  - Continue with safe defaults; log warnings.
- `policy-blocking`
  - Quarantine specific pack; require explicit remediation.
- `fatal`
  - Stop that pack immediately; preserve diagnostics and recovery action.

Behavior:

- Never block the full queue due to one failed pack.
- Show exact reason and next step in exception cards.
- Keep all warnings/errors visible in Activity Log.

## Testing Strategy

### Unit Tests

- Queue detection from `import/`.
- Bucketing logic (classified/ambiguous/failed).
- Policy-default resolution for ambiguous items.

### Integration Tests

- Multi-pack folder ingest with mixed outcomes.
- Per-pack quarantine behavior while queue continues.
- Incremental retry of failed stages.

### UI Contract Tests

- Subtab visibility and state transitions.
- Sticky status summary accuracy.
- Exception-card remediation actions.

### Regression Tests

- Auto-imports all eligible packs from `import/`.
- Clean packs do not require manual final confirmation.

## Success Criteria

1. Fewer import reversals/retries caused by classification mistakes.
2. Higher first-pass successful imports for clean packs.
3. Faster operator triage for exceptions with explicit remediation guidance.

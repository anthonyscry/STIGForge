# Import UX Orchestration Design

Date: 2026-02-15
Status: Approved
Scope: UI + app orchestration only (no scanner/dedupe internals)

## Context

STIGForge import and content selection already support folder scanning, library display, and mission selection. The next iteration prioritizes deterministic outcomes in the app layer so repeated runs with equivalent inputs produce equivalent results and operator-visible state.

The design keeps import scanner and dedupe internals stable, and focuses on orchestration in the app/view-model layer.

## Goals

1. Ensure deterministic selection and dependency derivation for import mission setup.
2. Keep STIG as the primary selectable content source.
3. Auto-include dependent SCAP/GPO/ADMX content in a deterministic and explainable way.
4. Allow missing SCAP dependencies with explicit warnings (non-blocking behavior).
5. Validate determinism through targeted automated tests.

## Non-Goals

1. Rewriting `STIGForge.Content.Import` scanner/dedupe logic.
2. Redesigning full import UX visual language beyond clarity updates needed for orchestration output.
3. Introducing persistence-heavy session replay models in this iteration.

## Approaches Considered

### 1) App-layer orchestrator (selected)

Create an app-layer orchestration service that computes a full selection plan from current inventory, applicability signals, and operator STIG choices.

Pros:
- Strong determinism with clear test seam.
- Keeps view model thinner and easier to reason about.
- Low risk to content import internals.

Cons:
- Adds new app-layer abstractions/DTOs.

### 2) ViewModel state machine

Keep all logic in `MainViewModel.Import` with explicit state transitions.

Pros:
- Fewer new files.

Cons:
- Higher coupling and broader unit test surface.
- More difficult to guarantee stable output contracts.

### 3) Session manifest model

Persist each scan/selection session and derive UI state from persisted manifest.

Pros:
- Excellent traceability.

Cons:
- Heavier scope than needed for this iteration.

## Selected Design

### Architecture

Introduce an app-layer `ImportSelectionOrchestrator` that sits between `MainViewModel.Import` and existing imported content/applicability sources.

- Scanner and dedupe remain source systems for normalized content inventory.
- View model invokes orchestrator and binds to returned projection data.
- UI state is reconstructed from orchestration snapshot objects rather than incremental mutation.

### Components

1. `ImportSelectionOrchestrator` (new app service)
   - Inputs: canonical inventory, machine applicability signals, selected STIG IDs.
   - Outputs: immutable `ImportSelectionPlan` snapshot.

2. DTOs (new app-layer models)
   - `ImportSelectionPlan`
   - `DependencyInclusion`
   - `SelectionWarning`
   - `SelectionCounts`

3. Deterministic comparer helpers
   - Stable ordering by content type, canonical name, and pack ID.

4. `MainViewModel.Import`
   - Collects inputs, calls orchestrator, atomically publishes bound collections and status.

5. `ImportView.xaml`
   - Clearly separates selectable STIG rows from auto-included dependency rows.
   - Displays non-blocking missing-dependency warnings.

### Data Flow

1. Import refresh builds canonical inventory.
2. Operator selects STIG entries and (optionally) applies machine applicability signals.
3. View model calls orchestrator with inventory + signals + selected STIG IDs.
4. Orchestrator derives deterministic dependency closure:
   - STIG selected directly.
   - SCAP/GPO/ADMX derived and marked auto-included/locked.
5. Missing SCAP dependency generates warning, while keeping STIG selected.
6. Orchestrator returns `ImportSelectionPlan` snapshot.
7. View model replaces bound state atomically from that snapshot.

## Determinism and Error Handling

### Determinism rules

1. Equivalent logical inputs always produce equivalent plan content and ordering.
2. Input ordering variations must not affect output ordering or counts.
3. Stable warning emission and stable warning ordering are required.

### Error and warning policy

1. Missing dependency is warning-level, not blocking.
2. Unknown/invalid metadata is skipped with deterministic warning reason.
3. Unexpected orchestrator failure preserves previous valid plan and emits one actionable status message.

### Logging

Emit one structured orchestration summary per run including:
- selected STIG count
- auto-included counts by dependency type
- warning count
- deterministic input hash (for diagnostics and test support)

## Testing Strategy

Primary acceptance is deterministic test coverage.

1. Unit tests for orchestrator deterministic behavior:
   - equivalent inputs in different order -> identical plan
   - stable dependency derivation for selected STIGs
   - missing SCAP -> warning + STIG retained
   - STIG-only rule/control counts
   - deterministic skips/warnings for invalid metadata

2. View model integration seam tests:
   - atomic replacement of projection state
   - no partial old/new mixed UI state after rerun

3. Assertion style:
   - deep equivalence or snapshot assertions over sorted DTOs

## Acceptance Criteria

1. Re-running orchestration with equivalent inputs yields equivalent plan output.
2. UI clearly differentiates selectable STIG content from locked auto dependencies.
3. Missing SCAP dependencies are visible warnings and do not block selection.
4. Automated tests enforce deterministic behavior and STIG-only counting rules.

## User Validation Record

Approved interactively in sections:
1. Architecture
2. Components
3. Data Flow
4. Error Handling and Determinism Rules
5. Testing Strategy

# STIGForge Troubleshooting UX Improvement Design

**Date:** 2026-02-25  
**Status:** Approved  
**Focus:** Faster troubleshooting for Dashboard Scan/Verify failures

## Goal

Make Scan/Verify failures immediately actionable by replacing generic error outcomes with clear, structured failure cards that show root cause, next step, and one-click recovery actions.

## Why This First

Current pain is not only failure frequency, but failure ambiguity. Operators lose time figuring out whether the issue is elevation, tool path, output artifacts, or generic tool execution.

The highest-value first change is an actionable error layer in the dashboard flow, without replacing the existing verification pipeline.

## Scope

### In Scope

- Add an actionable failure card UI surface for Scan/Verify errors.
- Centralize failure classification in `WorkflowViewModel`.
- Map high-frequency known failures to stable root-cause codes.
- Expose direct next-step actions from the card (open settings/output/retry).
- Persist root-cause code in mission diagnostics for trendability.

### Out of Scope

- Full troubleshooting wizard in this phase.
- New external logging infrastructure.
- Broad workflow architecture changes.

## Architecture

Keep existing Scan/Verify command flow and add a failure translation layer:

1. Raw workflow failure evidence enters from preflight and tool-run results.
2. A centralized classifier maps evidence to a `RootCauseCode`.
3. A `WorkflowFailureCard` view model is generated from that code.
4. Dashboard renders the card with concise guidance and action buttons.

This preserves existing execution behavior while improving operator clarity.

## Components

### 1) Failure Card Model

Add a model/state shape for dashboard troubleshooting:

- `Title`
- `RootCauseCode`
- `WhatHappened`
- `NextStep`
- `Confidence` (`High`/`Medium`)
- `Actions` (bound command actions)

Expose:

- `CurrentFailureCard`
- Optional short `FailureHistory` (recent items) for context

### 2) Classification Mapper

Add a single mapping method to classify Scan/Verify failures.

Initial high-value root-cause codes:

- `ELEVATION_REQUIRED`
- `EVALUATE_PATH_INVALID`
- `NO_CKL_OUTPUT`
- `TOOL_EXIT_NONZERO`
- `OUTPUT_NOT_WRITABLE`
- `UNKNOWN_FAILURE`

### 3) Dashboard Error Card UI

Render a dedicated card in `DashboardView` when Scan/Verify state is `Error`:

- Headline in plain language
- "What happened" summary
- "Do this now" next step
- 1-3 action buttons

Keep existing status text for short context, but move primary troubleshooting guidance to card content.

### 4) Action Wiring

Prefer existing commands to avoid redundant logic:

- Open settings
- Open output folder
- Retry scan
- Retry verify
- Optional copy diagnostics

## Data Flow

1. User runs `Scan` or `Verify`.
2. Preflight checks run first.
3. If preflight/tool failure occurs, execution path returns raw evidence.
4. Classifier resolves `RootCauseCode` and builds `WorkflowFailureCard`.
5. UI renders card immediately.
6. Mission diagnostics capture stage + root-cause code + technical details.

Known failures do not fall back to ambiguous "0 findings" messaging.

## Error Handling Strategy

- Deterministic mapping for known signatures (for example, exit code `5` + admin guidance).
- Confidence scoring:
  - `High`: explicit signatures (elevation required, no CKL diagnostic signature)
  - `Medium`: generic non-zero exits with incomplete detail
- Safe fallback (`UNKNOWN_FAILURE`) with a concrete recovery path:
  - open output folder
  - copy diagnostics
  - retry after checklist

## Testing Strategy

### Unit Tests

Add targeted tests for each `RootCauseCode` path:

- elevation-required mapping
- evaluate-path invalid mapping
- no-CKL mapping
- generic non-zero tool exit mapping
- output-not-writable mapping
- unknown fallback mapping

Validate card content fields (`Title`, `NextStep`, actions) and state transitions.

### Behavior and Regression Tests

- Card appears on Scan/Verify error and hides on success/reset.
- Card action buttons trigger expected existing commands.
- Mission diagnostics include stage + root-cause code.
- Existing successful workflow path remains unchanged.

### Manual QA

- Non-admin run -> elevation card with direct next step.
- Invalid Evaluate path -> path-invalid card.
- No CKL output -> no-CKL card.
- Generic non-zero exit -> non-zero card.
- Retry/open-settings/open-output actions reduce time-to-recovery.

## Success Criteria

- Known Scan/Verify failures no longer appear as ambiguous generic errors.
- First recovery action is obvious from the dashboard card.
- Operators can recover from common failures without leaving the app.
- Test coverage exists for each mapped failure category.

## Delivery Plan (Phased)

### Phase 1 (Now)

- Implement failure card model + mapper + initial root-cause codes.
- Add dashboard card and button actions.
- Add unit/regression coverage.

### Phase 2 (Next)

- Add optional preflight "Run Checks" button before Scan/Verify.
- Add richer diagnostics copy/export shortcuts.

### Phase 3 (Later)

- Introduce full guided troubleshooter and trend reporting UI.

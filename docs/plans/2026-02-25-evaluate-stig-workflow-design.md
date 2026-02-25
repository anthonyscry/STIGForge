# Evaluate-STIG Workflow Reliability and UX Design

**Date:** 2026-02-25  
**Status:** Approved  
**Selected Approach:** Option 3 (Full hardening pass)

## Goal

Eliminate ambiguous `0 findings` outcomes during dashboard `Scan`/`Verify` runs by enforcing preflight checks, surfacing actionable diagnostics, and adding advanced Evaluate-STIG configuration while improving workflow button readability.

## Problem Statement

Operators report `Baseline scan returned 0 findings` / `No CKL results were found under output root` in the dashboard.

Deep-dive evidence confirms two classes of failure:

1. **Invocation/environment failure** (tool did not produce scan artifacts), most notably non-elevated execution.
2. **Post-run artifact absence** (tool completed but no CKL found under output root).

Manual execution of the current Evaluate-STIG command path returned:

- Exit code: `5`
- Error: `You must run this from an elevated PowerShell session or use -AltCredential.`

This currently collapses into generic zero-findings messaging, which obscures root cause and slows recovery.

## Objectives

1. Prevent non-actionable `0 findings` messages when root cause is known.
2. Require and communicate admin/elevation prerequisites for Scan/Verify.
3. Preserve strict, deterministic Evaluate-STIG command construction.
4. Expose advanced Evaluate-STIG options in Settings with persistence.
5. Improve workflow action button readability (spacing between step number and label).

## Non-Goals

- Replacing Evaluate-STIG execution with a different scanner.
- Re-architecting the full workflow state machine.
- Adding remote credential orchestration beyond current scope.

## User Experience Design

### Scan/Verify preflight behavior

- `Scan` and `Verify` perform preflight validation before tool execution.
- If preflight fails, execution does not start.
- Operator receives a precise status message with next action.

### Elevation messaging

- Non-elevated runs show explicit guidance:
  - `Scan requires Administrator mode. Relaunch STIGForge as administrator.`
- Messaging replaces generic zero-findings status for this class of failure.

### Advanced settings

Settings window gains an `Evaluate-STIG Advanced` group to configure optional flags such as:

- `AnswerFile` (or AnswerFiles location equivalent)
- `AFPath`
- `SelectSTIG`
- Additional passthrough arguments (operator-supplied)

The app still enforces system-owned arguments:

- `-Output CKL`
- `-OutputPath <output folder>`
- Optional `-ComputerName <target>` when applicable

### Workflow button readability

Action labels are updated from `1 Import` style to spaced labels (`1  Import`, `2  Scan`, `3  Harden`, `4  Verify`) so number and name are visually separated.

## Technical Design

### Components to update

- `src/STIGForge.App/WorkflowViewModel.cs`
  - Shared preflight gate for Scan/Verify.
  - Evaluate argument composition with advanced options + enforced core args.
  - Structured diagnostics capture from tool runs.
  - Deterministic error classification for `0 findings` paths.
- `src/STIGForge.App/WorkflowSettings.cs`
  - New persisted advanced Evaluate-STIG fields and defaults.
- `src/STIGForge.App/Views/SettingsWindow.xaml`
  - New advanced settings UI section.
- `src/STIGForge.App/Views/DashboardView.xaml`
  - Workflow action label spacing update.

### Preflight contract

`RunVerificationPreflight(stage)` validates in order:

1. Stage prerequisites (for Scan: imported content exists).
2. Evaluate-STIG path resolves to `Evaluate-STIG.ps1`.
3. Output folder configured and usable.
4. Process elevation/admin rights present.

If any check fails, return actionable failure details and stop execution before `_verifyService.RunAsync(...)`.

### Diagnostics model

Capture stage-scoped diagnostics from `VerificationWorkflowResult.ToolRuns`:

- Stage (`Scan`/`Verify`)
- Tool (`Evaluate-STIG`/`SCAP`)
- `Executed` flag
- Exit code
- Key stderr/stdout snippets
- Timestamp

Diagnostics should be reflected in status text and persisted mission artifacts where appropriate.

### Zero-findings classification

Replace single-path zero result handling with explicit categories:

1. `tool-failed`
   - Preflight failure or non-zero tool exit.
2. `no-ckl-produced`
   - Tool reports execution but no CKL under output root.
3. `true-zero`
   - Valid artifact presence with zero consolidated findings.

Each category maps to different operator guidance and UI status text.

## Data Flow

1. Operator clicks `Scan` or `Verify`.
2. Preflight gate executes; failures short-circuit with explicit message.
3. Command args assembled from structured settings + enforced system args.
4. Verification workflow runs.
5. Tool results and diagnostics captured.
6. CKL discovery + consolidation performed.
7. Outcome classified (`tool-failed` / `no-ckl-produced` / `true-zero` / normal findings).
8. UI status and artifact outputs updated.

## Testing Strategy

### Unit tests

- `tests/STIGForge.UnitTests/App/WorkflowViewModelTests.cs`
  - Non-elevated Scan/Verify blocked with admin guidance.
  - Exit code `5` maps to elevation-required guidance.
  - Advanced argument composition includes selected options and enforced args.
  - Zero-findings classification behavior.
- `tests/STIGForge.UnitTests/App/WorkflowSettingsTests.cs`
  - Advanced settings persistence and defaults.
- `tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs` (targeted additions if needed)
  - Diagnostics availability for UI classification.

### Manual QA

1. Launch app non-admin, run Scan: verify explicit admin requirement message.
2. Launch app as admin, run Scan: verify CKL generation path and consolidated outputs.
3. Configure invalid Evaluate path: verify deterministic validation failure.
4. Configure advanced Evaluate options, save/restart: verify persistence.
5. Confirm workflow action buttons render readable spaced labels.

## Risks and Mitigations

- **Risk:** Operators provide malformed passthrough args.
  - **Mitigation:** Keep core args system-owned and append validated optional args.
- **Risk:** Too much diagnostic output overwhelms UI.
  - **Mitigation:** Show concise summaries in status, keep full details in artifacts/logs.
- **Risk:** Elevation check false negatives on non-Windows contexts.
  - **Mitigation:** Gate elevation logic to Windows-only runtime checks.

## Success Criteria

1. Non-elevated Evaluate-STIG attempts no longer surface as generic `0 findings`.
2. Scan/Verify status messages identify root cause and next operator action.
3. Advanced Evaluate-STIG settings persist and are reflected in command construction.
4. Zero-findings outcomes are classified and reported deterministically.
5. Workflow action labels are visually readable with clear number/step separation.

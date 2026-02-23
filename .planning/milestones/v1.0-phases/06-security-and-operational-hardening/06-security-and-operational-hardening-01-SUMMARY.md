---
phase: 06-security-and-operational-hardening
plan: 01
subsystem: security
tags: [break-glass, cli, wpf, audit, orchestration]

requires:
  - phase: 05-operator-workflow-completion
    provides: manual workflow canonicalization and in-flow evidence capture
provides:
  - Explicit CLI break-glass contracts for force/skip bypass flags
  - WPF apply/orchestrate parity for skip-snapshot break-glass semantics
  - Break-glass audit records including operator reason on high-risk paths
affects: [apply, orchestrate, audit, operator-safety]

tech-stack:
  added: []
  patterns:
    - High-risk bypass requires explicit acknowledgement plus specific reason
    - Break-glass actions emit dedicated tamper-evident audit records

key-files:
  created:
    - tests/STIGForge.UnitTests/Cli/BuildCommandsTests.cs
    - .planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-01-SUMMARY.md
  modified:
    - src/STIGForge.Cli/Commands/BuildCommands.cs
    - src/STIGForge.App/MainViewModel.ApplyVerify.cs
    - src/STIGForge.App/MainViewModel.cs
    - src/STIGForge.Build/BundleModels.cs
    - src/STIGForge.Build/BundleOrchestrator.cs
    - src/STIGForge.Core/Services/ManualAnswerService.cs
    - tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs

key-decisions:
  - "Break-glass reason quality is enforced centrally via ManualAnswerService validation and reused across CLI/WPF/orchestration paths."
  - "Orchestration now supports guarded skip-snapshot execution only when explicit break-glass acknowledgement and reason are provided."

patterns-established:
  - "High-risk flags: require --break-glass-ack + --break-glass-reason before execution."
  - "Audit detail format: Action=<surface>; Bypass=<flag>; Reason=<operator text>."

duration: 2h 4m
completed: 2026-02-08
---

# Phase 06 Plan 01: Break-Glass Guardrails Summary

**High-risk CLI and WPF bypass paths now require explicit break-glass acknowledgement and specific operator reason, with dedicated audit trail entries for every accepted override.**

## Performance

- **Duration:** 2h 4m
- **Started:** 2026-02-08T18:58:00Z
- **Completed:** 2026-02-08T21:01:58Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- Added CLI guard semantics for `--force-auto-apply` and `--skip-snapshot` using explicit acknowledgement and reason validation.
- Added WPF apply/orchestrate skip-snapshot parity guardrails and break-glass audit recording.
- Added orchestration request support for guarded skip-snapshot and break-glass metadata propagation.
- Centralized reason-quality checks in manual-answer service and expanded tests for break-glass reason validation.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add explicit break-glass contract to CLI high-risk flags** - `ae0d8e4`, `0ad1a6c`, `2d77183` (feat/fix)
2. **Task 2: Add WPF parity and audit trace for break-glass behavior** - `1dcc254` (feat)
3. **Task 1 verification hardening: Assert break-glass audit reason capture** - `ff1b55d` (test)

**Plan metadata:** `18cd696`, `60ec00d` (docs)

## Files Created/Modified
- `src/STIGForge.Cli/Commands/BuildCommands.cs` - Adds high-risk option guards and break-glass audit recording for CLI commands.
- `tests/STIGForge.UnitTests/Cli/BuildCommandsTests.cs` - Covers positive/negative validation paths for break-glass CLI contracts.
- `src/STIGForge.App/MainViewModel.ApplyVerify.cs` - Enforces break-glass acknowledgement/reason for WPF skip-snapshot apply/orchestrate paths.
- `src/STIGForge.Build/BundleOrchestrator.cs` - Requires and records break-glass metadata for orchestrated skip-snapshot execution.
- `src/STIGForge.Core/Services/ManualAnswerService.cs` - Adds reusable specific-reason validation for risk decisions.
- `tests/STIGForge.UnitTests/Services/ManualAnswerServiceTests.cs` - Verifies placeholder/short reason rejection and break-glass reason acceptance rules.

## Decisions Made
- Used a shared reason-quality policy in `ManualAnswerService` to keep CLI/WPF/orchestrator behavior consistent.
- Treated skip-snapshot in orchestration as an explicit high-risk path requiring operator acknowledgement and audit trace.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added explicit tests for CLI break-glass guard contract**
- **Found during:** Task 1 (CLI guard semantics)
- **Issue:** Plan referenced BuildCommands tests file path, but no dedicated test file existed.
- **Fix:** Added `BuildCommandsTests` to verify reject/accept behavior for high-risk invocation contracts.
- **Files modified:** `tests/STIGForge.UnitTests/Cli/BuildCommandsTests.cs`
- **Verification:** Test selection command prepared for targeted suite execution.
- **Committed in:** `ae0d8e4`

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Improved safety verification coverage without changing requested scope.

## Issues Encountered
- `csharp-ls` was unavailable (`csharp-ls: command not found`), so `lsp_diagnostics` verification could not run.
- CLI runtime verification with valid break-glass arguments reached host startup, but host initialization failed in this Linux environment due write access denial on `/usr/share/STIGForge`; unit tests were used to verify break-glass audit reason capture behavior.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Break-glass operator contract is implemented across targeted surfaces and auditable for high-risk bypass actions.
- Plan 06-02 can proceed, but environment-level tooling (`dotnet`, `csharp-ls`) must be available to run full automated verification locally.

---
*Phase: 06-security-and-operational-hardening*
*Completed: 2026-02-08*

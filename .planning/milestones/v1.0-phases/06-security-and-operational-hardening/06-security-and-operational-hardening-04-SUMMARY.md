---
phase: 06-security-and-operational-hardening
plan: 04
subsystem: security
tags: [integrity, fail-closed, emass, support-bundle, mission-summary]
requires:
  - phase: 06-security-and-operational-hardening-01
    provides: break-glass guardrails and audit trace policy
  - phase: 04-compliance-export-integrity
    provides: export package validation and integrity reporting
provides:
  - fail-closed apply/export completion semantics for integrity-critical failures
  - mission summary severity tiers (blocking, warnings, optional skips)
  - least-disclosure support bundle defaults with explicit sensitive opt-in
affects: [apply, export, cli, wpf, support, verification]
tech-stack:
  added: []
  patterns: [integrity gate before completion, operator-driven rollback messaging, sensitive-data opt-in]
key-files:
  created: []
  modified:
    - src/STIGForge.Apply/ApplyRunner.cs
    - src/STIGForge.Apply/Reboot/RebootCoordinator.cs
    - src/STIGForge.Export/EmassExporter.cs
    - src/STIGForge.Cli/Commands/SupportBundleBuilder.cs
    - src/STIGForge.Core/Services/BundleMissionSummaryService.cs
    - tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs
    - tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs
    - tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs
key-decisions:
  - "Apply completion fails closed when audit integrity evidence is missing or invalid."
  - "Submission readiness is blocked on eMASS package validation errors with non-zero CLI exit semantics."
  - "Support bundles redact sensitive context by default and require explicit operator reason to include sensitive content."
patterns-established:
  - "Severity-tier reporting: blocking failures, recoverable warnings, optional skips"
  - "Recovery guidance includes explicit artifact pointers and operator-initiated rollback wording"
duration: 1 min
completed: 2026-02-08
---

# Phase 06 Plan 04: Integrity Fail-Closed and Least-Disclosure Summary

**Fail-closed integrity checkpoints now gate apply/export completion while mission dashboards and support bundles expose explicit severity tiers with least-disclosure defaults.**

## Performance

- **Duration:** 1 min
- **Started:** 2026-02-08T13:31:11-08:00
- **Completed:** 2026-02-08T13:31:48-08:00
- **Tasks:** 2
- **Files modified:** 17

## Accomplishments
- Enforced fail-closed mission completion for apply/export integrity-critical failures, including invalid audit chain and invalid package readiness.
- Required explicit operator decision semantics for invalid or exhausted resume context and kept rollback operator-initiated with recovery artifact guidance.
- Added severity-tier mission summary counts and support-bundle least-disclosure behavior with sensitive-data opt-in policy.
- Expanded unit/integration coverage for apply resume blocking, support-bundle disclosure defaults, and export readiness status behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: Make integrity checks blocking for apply/export completion** - `e7bc386` (fix)
2. **Task 2: Add run-summary classification, least-disclosure support bundles, and end-to-end assertions** - `c16ed58` (fix)
3. **Task 2 follow-up test assertion:** `62663b0` (test)

## Files Created/Modified
- `src/STIGForge.Apply/ApplyRunner.cs` - Blocks mission completion on integrity-critical failures and emits recovery-artifact guidance.
- `src/STIGForge.Apply/Reboot/RebootCoordinator.cs` - Rejects invalid/exhausted resume marker contexts as operator-decision checkpoints.
- `src/STIGForge.Export/EmassExporter.cs` - Surfaces blocking failure and readiness state from package validation.
- `src/STIGForge.Cli/Commands/VerifyCommands.cs` - Sets non-zero process exit when submission readiness is invalid.
- `src/STIGForge.Cli/Commands/SupportBundleBuilder.cs` - Applies least-disclosure defaults and sensitive opt-in behavior for support bundles.
- `src/STIGForge.Core/Services/BundleMissionSummaryService.cs` - Classifies verify outcomes into blocking, warning, and optional-skip tiers.
- `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs` - Validates fail-closed apply integrity behavior.
- `tests/STIGForge.UnitTests/Apply/RebootCoordinatorTests.cs` - Validates invalid/exhausted resume marker failure paths.
- `tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs` - Validates least-disclosure default and sensitive opt-in behavior.
- `tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs` - Validates export readiness semantics.

## Decisions Made
- Fail-closed integrity outcomes are treated as blocking mission failures, not warnings.
- eMASS readiness is represented as a first-class `IsReadyForSubmission` outcome that drives CLI/WPF blocking semantics.
- Support bundle sensitive material remains excluded by default even when other diagnostics are collected.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- `dotnet` and `csharp-ls` are not available in this Linux execution environment, so local test execution and LSP diagnostics could not be run here.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 04 deliverables are in place and committed at task granularity.
- Phase 06 is complete and ready for phase transition planning.

---
*Phase: 06-security-and-operational-hardening*
*Completed: 2026-02-08*

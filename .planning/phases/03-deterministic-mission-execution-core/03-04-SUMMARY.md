---
plan: 03-04
status: complete
commit: a81dea5
---

## Summary

Plan 03-04 (APL-02: Multi-backend Apply with LGPO and Convergence) is complete.

### Changes

1. **LgpoRunner** (`src/STIGForge.Apply/Lgpo/LgpoRunner.cs`): Wraps LGPO.exe for policy import (`/m` Machine, `/u` User scope) and export (`/parse`). 60s timeout, FileNotFoundException for missing exe/pol.

2. **LgpoModels** (`src/STIGForge.Apply/Lgpo/LgpoModels.cs`): `LgpoScope` enum, `LgpoApplyRequest`, `LgpoApplyResult` models.

3. **ApplyRunner LGPO step** (`src/STIGForge.Apply/ApplyRunner.cs`): Added `apply_lgpo` step after DSC, before summary. Optional `LgpoRunner` and `PreflightRunner` constructor params. Convergence tracking: counts reboots, sets `ConvergenceStatus` on final result.

4. **ApplyModels** (`src/STIGForge.Apply/ApplyModels.cs`): Added `ConvergenceStatus` enum (Converged/Diverged/Exceeded/NotApplicable), LGPO fields on `ApplyRequest`, `RebootCount` and `ConvergenceStatus` on `ApplyResult`.

5. **Max reboot enforcement** (`src/STIGForge.Apply/Reboot/RebootCoordinator.cs`): `MaxReboots = 3` constant. `ScheduleReboot` checks count before scheduling, throws `RebootException("max_reboot_exceeded")` when exceeded. Increments count before writing marker.

6. **RebootModels** (`src/STIGForge.Apply/Reboot/RebootModels.cs`): Added `RebootCount` to `RebootContext`.

7. **Tests**: 11 tests across `LgpoRunnerTests.cs` and `ApplyConvergenceTests.cs` covering missing exe, missing pol, reboot limit enforcement, count increment, and convergence status values.

### Verification

- All 11 new tests pass.
- All existing RebootCoordinator and ApplyRunner tests pass.

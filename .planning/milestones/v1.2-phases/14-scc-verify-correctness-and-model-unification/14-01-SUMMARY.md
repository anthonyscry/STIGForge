# Phase 14 Plan 01 Summary: Async Runner Timeout and CLI Wiring

**Completed:** 2026-02-18
**Requirements:** VER-01

## Changes

### ScapRunner.cs
- Added `RunAsync(commandPath, arguments, workingDirectory, CancellationToken, TimeSpan?)` method with configurable timeout (default 600s).
- Uses `Task.Run(() => process.WaitForExit(ms))` for net48 compatibility.
- Cancellation kills process via `ct.Register(() => KillProcess(process))`.
- Timeout kills process and throws `TimeoutException`.
- `#if NET5_0_OR_GREATER` guard for `Process.Kill(true)` vs `Process.Kill()`.
- Existing sync `Run()` preserved for backward compatibility.

### EvaluateStigRunner.cs
- Added `RunAsync(toolRoot, arguments, workingDirectory, CancellationToken, TimeSpan?)` with identical pattern.
- Same timeout, cancellation, and net48 compatibility handling.

### Services.cs (STIGForge.Core.Abstractions)
- Added `TimeoutSeconds` property (default 600) to both `ScapWorkflowOptions` and `EvaluateStigWorkflowOptions`.

### VerifyCommands.cs (CLI)
- Added `--timeout` option (default 600) to both `verify-evaluate-stig` and `verify-scap` commands.
- Wired timeout into `EvaluateStigWorkflowOptions.TimeoutSeconds` and `ScapWorkflowOptions.TimeoutSeconds`.

## Test Results

- ScapRunnerTests: 3/3 passed (exit code, timeout, cancellation).
- EvaluateStigRunnerTests: 3/5 passed (timeout, cancellation, shell metacharacter injection passed; 2 PowerShell exit code tests skipped/failed on Linux due to `$LASTEXITCODE` platform behavior -- pre-existing).
- VerifyCommandsTests: 5/5 passed.

## Artifacts

| File | What Changed |
|------|-------------|
| `src/STIGForge.Verify/ScapRunner.cs` | Added `RunAsync` with configurable timeout |
| `src/STIGForge.Verify/EvaluateStigRunner.cs` | Added `RunAsync` with configurable timeout |
| `src/STIGForge.Core/Abstractions/Services.cs` | Added `TimeoutSeconds` to workflow options |
| `src/STIGForge.Cli/Commands/VerifyCommands.cs` | Added `--timeout` CLI option to both verify commands |

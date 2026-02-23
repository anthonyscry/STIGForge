# Plan 02-01 Summary: Profile CLI CRUD and Validation

**Status:** Complete
**Duration:** ~5 min
**Commits:** 2

## What Was Built

ProfileValidator service in STIGForge.Core.Services validates profile policy consistency: required fields (ProfileId, Name), null checks (NaPolicy, AutomationPolicy), range validation (NewRuleGraceDays >= 0), enum integrity, and overlay ID sanity. Returns all errors, not just first.

ProfileCommands in STIGForge.Cli.Commands provides six CLI subcommands: list (tabular output), show (full detail), create (from JSON with validation), update (preserves ID), export (to JSON), validate (from repo or file). Wired into Program.cs.

## Key Files

- `src/STIGForge.Core/Services/ProfileValidator.cs` — Validation service with ProfileValidationResult
- `src/STIGForge.Cli/Commands/ProfileCommands.cs` — Full CRUD CLI commands
- `src/STIGForge.Cli/Program.cs` — Registration wiring
- `tests/STIGForge.UnitTests/Services/ProfileValidatorTests.cs` — 10 tests covering all validation paths

## Decisions

- ProfileValidator is a plain class (no interface needed) — it's pure logic with no I/O
- All six CLI commands follow BuildCommands.Register() pattern with System.CommandLine
- Profile create with --from-json auto-generates ProfileId if empty
- Profile update preserves original ID regardless of JSON content

## Self-Check: PASSED
- [x] ProfileValidator catches null, empty, negative, and invalid enum values
- [x] All 10 validation tests pass
- [x] CLI builds successfully with all 6 subcommands
- [x] Program.cs wires ProfileCommands.Register()

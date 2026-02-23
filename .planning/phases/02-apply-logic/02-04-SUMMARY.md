# Plan 02-04 Summary: Overlay Diff and Review Queue CLI

**Status:** Complete
**Duration:** ~4 min
**Commits:** 1

## What Was Built

OverlayCommands.cs with two subcommands:
- `overlay list` -- Lists all overlays with ID, name, override count, and updated date
- `overlay diff <a> <b>` -- Diffs two overlays using OverlayConflictDetector, shows field-level conflicts in formatted table, warns on blocking conflicts

BundleCommands.cs extended with `review-queue` subcommand:
- Reads Reports/review_required.csv from a built bundle
- Outputs formatted table: VulnId, RuleId, Title, Reason
- Checks for overlay_conflict_report.csv and shows conflict count
- Handles empty review queue, missing files, and CSV parsing with quoted fields

## Key Files

- `src/STIGForge.Cli/Commands/OverlayCommands.cs` -- overlay list and diff commands
- `src/STIGForge.Cli/Commands/BundleCommands.cs` -- review-queue command added
- `src/STIGForge.Cli/Program.cs` -- OverlayCommands.Register() wired

## Decisions

- overlay diff creates a temporary 2-element list (a at index 0, b at index 1) so b has higher precedence per last-wins rule
- review-queue reads static CSV files, no DI services needed
- CSV parser handles quoted fields with escaped double-quotes
- Title truncated to 38 chars for display readability

## Self-Check: PASSED
- [x] overlay diff shows field-level conflicts with blocking warnings
- [x] overlay list shows available overlays
- [x] review-queue reads and displays review_required.csv
- [x] Cross-references overlay_conflict_report.csv when present
- [x] CLI builds, all 443 tests pass

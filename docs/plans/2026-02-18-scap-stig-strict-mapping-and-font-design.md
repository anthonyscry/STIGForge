# SCAP-to-STIG Strict Mapping and Font Refresh Design

## Context

The current machine scan flow can assign the same SCAP pack (for example, firewall) across unrelated detected STIG rows when benchmark IDs are missing, because fallback candidate matching is too broad. The UI also uses `Bahnschrift` as the main window font, and we want a Windows-native default (`Segoe UI`).

## Goals

- Ensure SCAP pairing is evaluated per STIG pack and not by broad cross-selected tag sets.
- Prefer benchmark overlap as the primary STIG-to-SCAP signal.
- If fallback matching is ambiguous or weak, leave SCAP as missing/review instead of forcing incorrect matches.
- Set app-wide default typography to `Segoe UI` while keeping code/log monospace faces unchanged.
- Record the strict SCAP pairing rule in the product spec.

## Design

### 1) Strict SCAP fallback compatibility

Add a shared rule in `PackApplicabilityRules` that defines when fallback tag matching between one STIG and one SCAP is allowed:

- If SCAP has feature tags (`firewall`, `edge`, `defender`, etc.), require overlap with feature tags from that exact STIG.
- If SCAP has no feature tags, allow fallback only when both STIG and SCAP have explicit overlapping OS tags.
- No fallback match when tags are empty or only broad machine defaults are present.

### 2) Per-STIG candidate generation

In `MainViewModel.Import`, update SCAP candidate derivation so each STIG computes candidates from:

- benchmark overlap first,
- then strict per-STIG fallback compatibility,
- and never from unioned selected-STIG tags or default machine feature tags.

This keeps deterministic selection behavior but prevents unrelated STIG rows from inheriting the same SCAP candidate.

### 3) Typography refresh

- Define an app-wide `Window` style in `App.xaml` with `FontFamily="Segoe UI"` and default size `13`.
- Remove explicit `Bahnschrift` assignments in window-level XAML so the global style applies.
- Keep explicit `Consolas` uses where the view intentionally displays log/diff text.

### 4) Spec memory update

Add an explicit requirement to `spec-stigforge.md`:

- STIG-to-SCAP association must be deterministic and per-STIG.
- Benchmark-ID overlap is primary.
- If no confident match exists, mark as review/missing rather than broad fallback assignment.

## Validation

- Unit tests for strict fallback compatibility behavior in `PackApplicabilityRulesTests`.
- Targeted unit test run for services tests.
- Build and run for `STIGForge.App` to ensure no XAML/theme regression.

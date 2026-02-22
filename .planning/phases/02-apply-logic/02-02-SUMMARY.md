# Plan 02-02 Summary: Classification Scope Service Extension

**Status:** Complete
**Duration:** ~3 min
**Commits:** 1

## What Was Built

ClassificationScopeService.Evaluate() extended with symmetric Unclassified mode: ClassifiedOnly controls marked NotApplicable when confidence meets threshold, low confidence routes to review queue. Mixed mode passes all controls through as Open but routes Unknown scope to review when AutoNaOutOfScope is enabled. Deterministic ordering added via OrderBy ControlId with StringComparer.OrdinalIgnoreCase on both compiled and review lists.

14 unit tests covering: Classified mode (4 tests), Unclassified mode (4 tests), Mixed mode (2 tests), determinism (2 tests), edge cases (2 tests).

## Key Files

- `src/STIGForge.Core/Services/ClassificationScopeService.cs` -- Extended Evaluate() and Compile()
- `tests/STIGForge.UnitTests/Services/ClassificationScopeServiceTests.cs` -- 14 tests

## Decisions

- MeetsThreshold maps High=3, Medium=2, Low=1 and compares actual >= threshold
- FilterControls() left unchanged -- it handles pre-build filtering separately from Compile()
- Deterministic ordering uses StringComparer.OrdinalIgnoreCase for case-insensitive ControlId sort

## Self-Check: PASSED
- [x] Unclassified mode marks ClassifiedOnly controls as NA when confidence meets threshold
- [x] Mixed mode includes all controls with no auto-NA filtering
- [x] Ambiguous scope (Unknown) routes to review in all three modes
- [x] Identical inputs produce identical outputs (determinism proven)
- [x] All 14 tests pass

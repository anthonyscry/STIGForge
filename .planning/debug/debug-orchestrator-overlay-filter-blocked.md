# Debug Session: Orchestrator Overlay Filter Blocked

Date: 2026-02-20
Scope: UAT Test 4 root-cause diagnosis only (no code changes)

## Investigation Checklist

1. Verify orchestration filtering implementation exists in current runnable branch.
2. Verify prerequisite artifact `Reports/overlay_decisions.json` is present/produced.
3. Verify mismatch between summary claims and executable branch state.
4. Identify exact files/lines tied to the blocking root cause.

## Evidence Collected

### A) Orchestration filtering implementation in runnable branch

- `src/STIGForge.Build/BundleOrchestrator.cs:59-63` generates PowerStig data from `LoadBundleControls(root)` and `LoadBundlePowerStigOverrides(root)` only.
- `src/STIGForge.Build/BundleOrchestrator.cs:167-180` (`LoadBundleControls`) reads `Manifest/pack_controls.json` directly and returns all controls; there is no `overlay_decisions.json` read path and no `NotApplicable` exclusion logic.
- Search for `NotApplicable` in `src/STIGForge.Build/BundleOrchestrator.cs` returned no matches.

Conclusion: The claimed orchestration-time filter for merged overlay decisions is not implemented in this branch.

### B) Prerequisite artifact availability (`Reports/overlay_decisions.json`)

- `src/STIGForge.Build/BundleBuilder.cs:39-58` writes reports `na_scope_filter_report.csv` and `review_required.csv`.
- `src/STIGForge.Build/BundleBuilder.cs` contains no logic to write `Reports/overlay_decisions.json` or `Reports/overlay_conflicts.csv`.
- Workspace-wide file glob for `**/overlay_decisions.json` returned no files.
- UAT report explicitly records missing overlay artifacts: `.planning/phases/2026-02-19-stigforge-next/01-UAT.md:22-25` and `.planning/phases/2026-02-19-stigforge-next/01-UAT.md:61-67`.

Conclusion: The prerequisite artifact is not being generated, so apply-time behavior depending on that artifact is blocked/untestable.

### C) Summary claims vs executable state mismatch

- Summary claims implementation exists:
  - `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md:19` claims `overlay_decisions.json` emission.
  - `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md:24` claims orchestration excludes `NotApplicable` IDs.
  - `.planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md:24` and `.planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md:39-40` repeat those claims.
- Claimed tests do not exist in this executable branch:
  - `tests/STIGForge.UnitTests/Build/BundleOrchestratorControlOverrideTests.cs` file not found.
  - `dotnet test ... --filter "FullyQualifiedName~BundleOrchestratorControlOverrideTests"` returned: no matching tests.

Conclusion: Planning/summary artifacts describe work not present in current runnable code state.

## Root Cause

The runnable branch does not contain the Phase 01 overlay-merge/apply-filter implementation that the summary/verification docs claim, so `overlay_decisions.json` is neither produced by build nor consumed by orchestrator for `NotApplicable` control exclusion.

## Files and Lines Tied to Root Cause

- `src/STIGForge.Build/BundleBuilder.cs:56-58` writes only NA/review reports; no overlay decision/conflict report emission.
- `src/STIGForge.Build/BundleOrchestrator.cs:59-63` uses unfiltered controls for PowerStig generation.
- `src/STIGForge.Build/BundleOrchestrator.cs:167-180` loads all controls from `pack_controls.json` with no overlay decision filtering.
- `.planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md:19,24` claims behavior not present in executable code.
- `.planning/phases/2026-02-19-stigforge-next/01-VERIFICATION.md:24,39-40` marks behavior verified despite missing implementation in branch.
- `.planning/phases/2026-02-19-stigforge-next/01-UAT.md:22-25,33-37` documents artifact absence and blocked downstream orchestration test.

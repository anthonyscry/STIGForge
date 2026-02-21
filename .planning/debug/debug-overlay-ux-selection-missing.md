# Debug Session: Overlay UX Selection Missing

Date: 2026-02-20
Scope: UAT Test 3 root-cause diagnosis only (no code changes)

## Repro Target
- UAT Test 3 (`.planning/phases/2026-02-19-stigforge-next/01-UAT.md:27`): a rule overridden to `NotApplicable` by overlays should not appear in `Reports/review_required.csv`.
- User report (`.planning/phases/2026-02-19-stigforge-next/01-UAT.md:30`): overlays appear non-functional, and operator expected selection from content-pack-derived items instead of manual typing.

## Investigation Findings

### 1) Overlay editor UX flow requires manual text entry and has no pack-derived picker
- `OverlayEditorWindow.xaml` provides only free-text inputs for `RuleId`, `Setting`, and `Value` (`src/STIGForge.App/OverlayEditorWindow.xaml:19`, `src/STIGForge.App/OverlayEditorWindow.xaml:20`, `src/STIGForge.App/OverlayEditorWindow.xaml:21`, `src/STIGForge.App/OverlayEditorWindow.xaml:23`).
- There is no `ComboBox`/pick-list bound to selected content pack controls in overlay editor UI.
- `OverlayEditorViewModel` only depends on `IOverlayRepository` and has no `IControlRepository`/selected-pack context (`src/STIGForge.App/OverlayEditorViewModel.cs:13`, `src/STIGForge.App/OverlayEditorViewModel.cs:15`).

### 2) Overlay editor saves PowerSTIG mapping overrides, not control status overrides
- Add flow creates `PowerStigOverride` entries only (`src/STIGForge.App/OverlayEditorViewModel.cs:27`, `src/STIGForge.App/OverlayEditorViewModel.cs:36`, `src/STIGForge.App/OverlayEditorViewModel.cs:38`).
- Save flow persists `PowerStigOverrides` only; it never populates `Overlay.Overrides` with `ControlOverride.StatusOverride=NotApplicable` (`src/STIGForge.App/OverlayEditorViewModel.cs:82`, `src/STIGForge.Core/Models/Overlay.cs:24`, `src/STIGForge.Core/Models/Overlay.cs:25`).

### 3) Bundle build/review queue path does not merge/apply overlay control decisions on this branch
- Build compile path is classification-scope only: `BundleBuilder` calls `_scope.Compile(profile, controls)` and writes `review_required.csv` from that result (`src/STIGForge.Build/BundleBuilder.cs:51`, `src/STIGForge.Build/BundleBuilder.cs:57`).
- `BundleBuilder` writes `overlays.json` but does not generate `overlay_decisions.json`/`overlay_conflicts.csv` and does not apply overlay statuses to compiled controls (`src/STIGForge.Build/BundleBuilder.cs:106`).
- `IClassificationScopeService.Compile` has no overlay input, so overlays cannot affect review queue (`src/STIGForge.Core/Abstractions/Services.cs:64`, `src/STIGForge.Core/Abstractions/Services.cs:66`).
- `ClassificationScopeService` evaluates only classification policy for `NotApplicable`; no overlay decision logic exists (`src/STIGForge.Core/Services/ClassificationScopeService.cs:8`, `src/STIGForge.Core/Services/ClassificationScopeService.cs:29`, `src/STIGForge.Core/Services/ClassificationScopeService.cs:38`).

### 4) Apply/orchestration also does not consume merged overlay NotApplicable decisions
- Orchestrator loads full `pack_controls.json` directly without overlay filtering (`src/STIGForge.Build/BundleOrchestrator.cs:167`, `src/STIGForge.Build/BundleOrchestrator.cs:172`, `src/STIGForge.Build/BundleOrchestrator.cs:179`).
- Orchestrator only consumes `PowerStigOverrides` for data generation (`src/STIGForge.Build/BundleOrchestrator.cs:61`, `src/STIGForge.Build/BundleOrchestrator.cs:182`, `src/STIGForge.Build/BundleOrchestrator.cs:194`).

## Root Cause
The active branch implements overlay editing as PowerSTIG mapping input (manual RuleId text entry) but does not implement content-pack-derived control selection or any build-time/apply-time merge of overlay `ControlOverride.StatusOverride` decisions, so `NotApplicable` overlay intent never reaches `review_required.csv`.

## Impact Mapping to UAT Gap
- UX expectation is unmet: operator must type Rule IDs manually; no guided selection from selected pack controls.
- Functional expectation is unmet: overlay `NotApplicable` decisions are not produced/consumed in build pipeline for review queue suppression.
- Result: rules remain in `review_required.csv` even when user expects overlay-driven exclusion.

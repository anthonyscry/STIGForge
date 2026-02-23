# Plan 02-05 Summary: WPF Profile Editor and Break-Glass Dialog

**Status:** Complete
**Duration:** ~6 min
**Commits:** 1

## What Was Built

**Profile editor extensions:** MainViewModel.Profile.cs adds observable properties for ConfidenceThreshold, AutomationMode, RequiresMapping, and ReleaseDateSource. SaveProfileAsync now uses these instead of hardcoded values. LoadProfileFields calls extended loader to populate all fields on profile selection. ProfileView.xaml updated with confidence threshold combo, automation mode combo, requires-mapping checkbox, and release date source combo.

**Review queue panel:** GuidedRunView.xaml gains a "Controls Requiring Review" step between Build and Orchestration. Shows VulnId, RuleId, Title, and ReviewReason for each flagged control. Panel auto-hides when review queue is empty via NullOrEmptyToCollapsed converter. MainViewModel.Profile.cs provides PopulateReviewQueue() method and ReviewQueueItems ObservableCollection.

**Break-glass dialog:** BreakGlassDialog.xaml/cs is a modal Window with bypass description, TextBox for justification (minimum 8 characters), and Override/Cancel buttons. Override button disabled until reason meets length requirement. Returns Confirmed flag and Reason string for audit trail logging.

## Key Files

- `src/STIGForge.App/MainViewModel.Profile.cs` -- Profile editing properties, review queue, parse helpers
- `src/STIGForge.App/MainViewModel.Import.cs` -- Updated SaveProfileAsync and LoadProfileFields
- `src/STIGForge.App/Views/ProfileView.xaml` -- Extended with scope and automation policy fields
- `src/STIGForge.App/Views/GuidedRunView.xaml` -- Review queue panel added
- `src/STIGForge.App/Views/BreakGlassDialog.xaml` -- Modal dialog XAML
- `src/STIGForge.App/Views/BreakGlassDialog.xaml.cs` -- Dialog code-behind

## Decisions

- Profile edits persist on Save, not during live mission runs (per user decision)
- Review queue panel uses same grid layout pattern as timeline panel
- Break-glass dialog follows AboutDialog window pattern with dark title bar
- Minimum 8-character justification matches CLI break-glass requirement

## Self-Check: PASSED
- [x] All profile policy knobs editable in settings editor
- [x] Review queue shows flagged controls with reasons
- [x] Break-glass dialog captures reason with minimum length
- [x] WPF App builds successfully
- [x] All 443 tests pass

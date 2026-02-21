---
status: diagnosed
phase: 2026-02-19-stigforge-next
source: 01-01-SUMMARY.md
started: 2026-02-21T01:10:53Z
updated: 2026-02-21T03:48:07Z
---

## Current Test

[testing complete]

## Tests

### 1. Directory import records deterministic manifest hash
expected: Import the same directory-based content pack twice. The imported pack metadata should show a 64-character lowercase SHA-256 manifest hash, and it should stay identical across both imports when files are unchanged.
result: issue
reported: "files were imported but the hash did not stay the same / when i imported the data it didnt dedupe and instead jsut imported all the files again so they all showe dup twice"
severity: major

### 2. Bundle build emits overlay merge artifacts
expected: Build a bundle with overlays. The bundle Reports folder should include both overlay_conflicts.csv and overlay_decisions.json.
result: issue
reported: "both are missing when i tried to click add to an overlay and saved it created a hash but the rule did not appear in db"
severity: major

### 3. Review queue reflects merged NotApplicable decisions
expected: For a rule overridden to NotApplicable by overlays, that rule should not appear in Reports/review_required.csv for the built bundle.
result: issue
reported: "overlays dont work plus nothign shows up in overlay i dont want user to have to type by hand it should be selected from the contnt packs selecetd in the bundle"
severity: major

### 4. Orchestration excludes NotApplicable rules from apply control set
expected: Run orchestration with merged overlay decisions present. Rules marked NotApplicable in overlay_decisions.json should be excluded from the control set used for apply-time PowerStig data generation.
result: issue
reported: "doesnt work due to earlier issues"
severity: major

### 5. Malformed import surfaces diagnostics and audit failure entry
expected: Trigger a malformed import. Import should fail with actionable diagnostics, and an audit failure entry should be persisted with failure context.
result: skipped
reason: user requested to skip remaining manual tests

## Summary

total: 5
passed: 0
issues: 4
pending: 0
skipped: 1

## Gaps

- truth: "Import the same directory-based content pack twice. The imported pack metadata should show a 64-character lowercase SHA-256 manifest hash, and it should stay identical across both imports when files are unchanged."
  status: failed
  reason: "User reported: files were imported but the hash did not stay the same / when i imported the data it didnt dedupe and instead jsut imported all the files again so they all showe dup twice"
  severity: major
  test: 1
  root_cause: "Runnable branch still sets directory ManifestSha256 to packId and lacks persisted import dedupe, so repeated imports produce new hashes and duplicate stored packs."
  artifacts:
    - path: "src/STIGForge.Content/Import/ContentPackImporter.cs"
      issue: "Directory import path assigns ManifestSha256 from packId in active branch."
    - path: "src/STIGForge.Content/Import/ImportDedupService.cs"
      issue: "Dedupe scope is scan-local only and does not check previously imported packs."
    - path: "src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs"
      issue: "Pack upsert conflicts only on pack_id, so same content imports persist again."
  missing:
    - "Port deterministic directory manifest hashing implementation into active runnable branch."
    - "Add persisted-pack dedupe keyed by deterministic manifest/content identity before save."
  debug_session: ".planning/debug/debug-dir-import-hash-dedupe.md"
- truth: "Build a bundle with overlays. The bundle Reports folder should include both overlay_conflicts.csv and overlay_decisions.json."
  status: failed
  reason: "User reported: both are missing when i tried to click add to an overlay and saved it created a hash but the rule did not appear in db"
  severity: major
  test: 2
  root_cause: "Active runnable branch does not include overlay merge/report pipeline and app overlay editor persists PowerStig overrides rather than merge-consumed control overrides."
  artifacts:
    - path: "src/STIGForge.Build/BundleBuilder.cs"
      issue: "No OverlayMergeService call and no writes for overlay_conflicts.csv/overlay_decisions.json in active branch."
    - path: "src/STIGForge.App/OverlayEditorViewModel.cs"
      issue: "Save flow writes PowerStigOverrides only, not Overlay.Overrides decisions used by merge path."
    - path: "src/STIGForge.Cli/Commands/BundleCommands.cs"
      issue: "CLI overlay edit path writes Overlay.Overrides, showing model mismatch with app editor."
  missing:
    - "Bring overlay merge service and deterministic report writers into active runnable branch."
    - "Align app overlay persistence with ControlOverride/Overlay.Overrides model used by build merge."
  debug_session: ".planning/debug/debug-overlay-artifacts-missing.md"
- truth: "For a rule overridden to NotApplicable by overlays, that rule should not appear in Reports/review_required.csv for the built bundle."
  status: failed
  reason: "User reported: overlays dont work plus nothign shows up in overlay i dont want user to have to type by hand it should be selected from the contnt packs selecetd in the bundle"
  severity: major
  test: 3
  root_cause: "Overlay decision UX/integration is missing: users manually type IDs, app does not populate Overlay.Overrides from selected content packs, and review queue generation in active branch ignores overlay decisions."
  artifacts:
    - path: "src/STIGForge.App/OverlayEditorWindow.xaml"
      issue: "Overlay entry is manual text-based with no content-pack-derived selection list."
    - path: "src/STIGForge.App/OverlayEditorViewModel.cs"
      issue: "No selected-pack control data source and no persistence of ControlOverride decisions."
    - path: "src/STIGForge.Build/BundleBuilder.cs"
      issue: "Review queue is derived from classification compile output without overlay merge application."
    - path: "src/STIGForge.Core/Services/ClassificationScopeService.cs"
      issue: "Service contract/pipeline has no overlay decision input path."
  missing:
    - "Provide selectable RuleId/VulnId list sourced from controls in selected content packs."
    - "Persist UI-selected override decisions to Overlay.Overrides."
    - "Apply merged overlay decisions before generating review_required.csv."
  debug_session: ".planning/debug/debug-overlay-ux-selection-missing.md"
- truth: "Run orchestration with merged overlay decisions present. Rules marked NotApplicable in overlay_decisions.json should be excluded from the control set used for apply-time PowerStig data generation."
  status: failed
  reason: "User reported: doesnt work due to earlier issues"
  severity: major
  test: 4
  root_cause: "Downstream orchestration filter is absent in active branch and is blocked by missing upstream overlay_decisions artifact generation."
  artifacts:
    - path: "src/STIGForge.Build/BundleBuilder.cs"
      issue: "No overlay_decisions.json emission in active runnable branch."
    - path: "src/STIGForge.Build/BundleOrchestrator.cs"
      issue: "No load/filter path for NotApplicable decisions before apply-time control set generation."
    - path: ".planning/phases/2026-02-19-stigforge-next/01-01-SUMMARY.md"
      issue: "Summary claims behavior that is not present in active executable branch."
  missing:
    - "Implement overlay decision artifact emission in active branch bundle build path."
    - "Consume overlay_decisions.json in BundleOrchestrator and exclude NotApplicable rules before apply generation."
    - "Add/enable executable regression tests in active branch for this flow."
  debug_session: ".planning/debug/debug-orchestrator-overlay-filter-blocked.md"

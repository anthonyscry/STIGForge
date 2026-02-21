---
status: complete
phase: 2026-02-19-stigforge-next
source: 01-01-SUMMARY.md
started: 2026-02-21T01:10:53Z
updated: 2026-02-21T03:43:22Z
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
  artifacts: []
  missing: []
- truth: "Build a bundle with overlays. The bundle Reports folder should include both overlay_conflicts.csv and overlay_decisions.json."
  status: failed
  reason: "User reported: both are missing when i tried to click add to an overlay and saved it created a hash but the rule did not appear in db"
  severity: major
  test: 2
  artifacts: []
  missing: []
- truth: "For a rule overridden to NotApplicable by overlays, that rule should not appear in Reports/review_required.csv for the built bundle."
  status: failed
  reason: "User reported: overlays dont work plus nothign shows up in overlay i dont want user to have to type by hand it should be selected from the contnt packs selecetd in the bundle"
  severity: major
  test: 3
  artifacts: []
  missing: []
- truth: "Run orchestration with merged overlay decisions present. Rules marked NotApplicable in overlay_decisions.json should be excluded from the control set used for apply-time PowerStig data generation."
  status: failed
  reason: "User reported: doesnt work due to earlier issues"
  severity: major
  test: 4
  artifacts: []
  missing: []

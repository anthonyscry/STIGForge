---
phase: 07-release-readiness-and-compatibility
plan: 04
type: execute
wave: 3
depends_on:
  - 07-02
  - 07-03
files_modified:
  - docs/release/ShipReadinessChecklist.md
  - docs/release/ReleaseCandidatePlaybook.md
  - docs/release/UpgradeAndRebaseValidation.md
  - tools/release/Invoke-PackageBuild.ps1
  - tools/release/Invoke-ReleaseGate.ps1
  - .github/workflows/release-package.yml
  - .github/workflows/vm-smoke-matrix.yml
autonomous: true
must_haves:
  truths:
    - "Release candidate readiness has an explicit, enforceable checklist."
    - "Package reproducibility and checksum evidence are required release artifacts."
    - "Upgrade/rebase compatibility validation is documented and gated for release decisions."
  artifacts:
    - "docs/release/ReleaseCandidatePlaybook.md"
    - "docs/release/UpgradeAndRebaseValidation.md"
    - "docs/release/ShipReadinessChecklist.md"
  key_links:
    - "release gate artifacts -> ship readiness checklist completion"
    - "upgrade/rebase validation results -> final go/no-go decision"
---

<objective>
Lock release-candidate process, reproducibility evidence, and upgrade/rebase validation into a final go/no-go workflow.

Purpose: Convert existing release tooling into a deterministic, auditable release-candidate procedure with documentation lock.
Output: Finalized RC playbook/checklist, reproducibility assertions, and upgrade/rebase validation workflow integration.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/07-release-readiness-and-compatibility/07-RESEARCH.md
@docs/release/ShipReadinessChecklist.md
@.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-02-SUMMARY.md
@.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-03-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Finalize release-candidate checklist and reproducibility gate artifacts</name>
  <files>docs/release/ShipReadinessChecklist.md, docs/release/ReleaseCandidatePlaybook.md, tools/release/Invoke-PackageBuild.ps1, tools/release/Invoke-ReleaseGate.ps1</files>
  <action>Refine the ship-readiness checklist into enforceable release-candidate gates tied to actual generated artifacts (release summary, security summary, quarterly drift report, checksums, dependency/SBOM outputs). Ensure package/release scripts emit and cross-reference reproducibility artifacts needed for audit-ready RC review.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-PackageBuild.ps1 -Configuration Release -Runtime win-x64 -OutputRoot .\.artifacts\release-package\phase07-rc</verify>
  <done>RC checklist and playbook are concrete, artifact-backed, and reproducibility evidence is emitted consistently by release tooling.</done>
</task>

<task type="auto">
  <name>Task 2: Document and gate upgrade/rebase validation in release workflows</name>
  <files>docs/release/UpgradeAndRebaseValidation.md, .github/workflows/release-package.yml, .github/workflows/vm-smoke-matrix.yml</files>
  <action>Define upgrade/rebase validation procedure and evidence requirements (baseline -> target diff, overlay rebase behavior, data retention expectations, rollback safety). Wire these checks into release workflows as explicit gating/reporting steps aligned with existing smoke matrix and release package flow.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\phase07-rc</verify>
  <done>Upgrade/rebase compatibility is documented, repeatable, and represented in release workflow evidence for go/no-go decisions.</done>
</task>

</tasks>

<verification>
- Run package build and release gate commands to confirm RC artifacts/checklists are generated.
- Confirm release workflows include upgrade/rebase validation evidence steps.
</verification>

<success_criteria>
- Release-candidate process is deterministic, documented, and artifact-complete.
- Compatibility and upgrade/rebase evidence is required and visible before release promotion.
</success_criteria>

<output>
After completion, create `.planning/phases/07-release-readiness-and-compatibility/07-release-readiness-and-compatibility-04-SUMMARY.md`
</output>

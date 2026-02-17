# Release Candidate Playbook

This playbook defines the deterministic path from RC candidate commit to go/no-go decision.

## 1) Pin the candidate

1. Select the exact RC commit hash.
2. Run all release commands from that commit only.
3. Keep all generated artifacts for audit review.

## 2) Run release gate

Generate release, security, quarterly, and upgrade/rebase evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 `
  -Configuration Release `
  -OutputRoot .\.artifacts\release-gate\v1.1-rc1
```

Required pass conditions:

- `report/release-gate-summary.json` -> `overallPassed=true`
- `security/reports/security-gate-summary.json` -> no unresolved blocking findings
- `quarterly-pack/quarterly-pack-summary.json` -> policy-compliant result
- `quarterly-pack/quarterly-pack-summary.json` -> `overallPassed=true` and `decision=pass`
- `upgrade-rebase/upgrade-rebase-summary.json` -> `status=passed`
- `upgrade-rebase/upgrade-rebase-summary.json` includes `upgrade-rebase-parity-contract` with `succeeded=true`

Mandatory contract enforcement uses `tools/release/Test-ReleaseEvidenceContract.ps1` and blocks promotion with explicit categories:

- `[missing-proof]` required deterministic evidence artifact/step is missing.
- `[failed-check]` required summary status or contract step is present but failing.
- `[disabled-check]` required gate execution was disabled (for example `run_release_gate=false` in `release-package.yml`).

Checklist-first blocker guidance appears in console output and in `report/release-evidence-contract-report.md`:

- `what blocked`
- `why blocked`
- `next command`

## 3) Build package with reproducibility evidence

Build package artifacts and cross-link release evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-PackageBuild.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot .\.artifacts\release-package\v1.1-rc1 `
  -ReleaseGateRoot .\.artifacts\release-gate\v1.1-rc1
```

`Invoke-PackageBuild.ps1` now performs mandatory preflight contract validation before `Compress-Archive` and terminates on any blocker category. Recovery command reference (copy/paste):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\v1.1-rc1
```

Required outputs:

- `bundle/stigforge-cli-win-x64.zip`
- `bundle/stigforge-app-win-x64.zip`
- `manifest/release-package-manifest.json`
- `manifest/reproducibility-evidence.json`
- `manifest/sha256-checksums.txt`
- `sbom/dotnet-packages.json` (unless explicitly skipped by policy)

## 4) Review ship checklist

Use `docs/release/ShipReadinessChecklist.md` and confirm every item for the same commit hash.

Complete operator-only checklist sections in `docs/release/ManualValidationSignoff-v1.1-rc1.md`.

The release owner and approver must review:

- Release gate report + summary
- Security gate report + summary
- Quarterly pack summary + report
- Upgrade/rebase summary + report
- Upgrade/rebase parity contract evidence (`upgrade-rebase-parity-contract`)
- Reproducibility evidence + checksums

## 5) Workflow parity checks

Before promotion, verify workflow parity:

- `release-package.yml` run succeeds and uploads package + release gate artifacts.
- `vm-smoke-matrix.yml` passes for `win11`, `server2019`, and `server2022`.
- VM artifacts include stability budget and upgrade/rebase evidence references.
- VM artifacts include quarterly trend summary/report and stability summary/report for each runner.

## 6) Go/No-Go decision record

Record the final decision in release notes or release PR with:

- RC commit hash
- Artifact roots used for review
- Any policy exceptions and approvals
- Final decision: Go or No-Go

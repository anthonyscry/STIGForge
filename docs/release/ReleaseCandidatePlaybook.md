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
  -OutputRoot .\.artifacts\release-gate\phase07-rc
```

Required pass conditions:

- `report/release-gate-summary.json` -> `overallPassed=true`
- `security/reports/security-gate-summary.json` -> no unresolved blocking findings
- `quarterly-pack/quarterly-pack-summary.json` -> policy-compliant result
- `upgrade-rebase/upgrade-rebase-summary.json` -> `status=passed`

## 3) Build package with reproducibility evidence

Build package artifacts and cross-link release evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-PackageBuild.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot .\.artifacts\release-package\phase07-rc `
  -ReleaseGateRoot .\.artifacts\release-gate\phase07-rc
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

The release owner and approver must review:

- Release gate report + summary
- Security gate report + summary
- Quarterly pack summary + report
- Upgrade/rebase summary + report
- Reproducibility evidence + checksums

## 5) Workflow parity checks

Before promotion, verify workflow parity:

- `release-package.yml` run succeeds and uploads package + release gate artifacts.
- `vm-smoke-matrix.yml` passes for `win11`, `server2019`, and `server2022`.
- VM artifacts include stability budget and upgrade/rebase evidence references.

## 6) Go/No-Go decision record

Record the final decision in release notes or release PR with:

- RC commit hash
- Artifact roots used for review
- Any policy exceptions and approvals
- Final decision: Go or No-Go

# STIGForge Ship Readiness Checklist

Use this checklist to make the final RC go/no-go decision.

## 1) Release gate must pass with complete evidence

Run `tools/release/Invoke-ReleaseGate.ps1` on the exact RC commit and confirm:

- `report/release-gate-report.md` shows `Result: PASS`.
- `report/release-gate-summary.json` has all required steps with `succeeded: true`.
- `security/reports/security-gate-report.md` and `security/reports/security-gate-summary.json` show no unresolved blocking findings.
- `quarterly-pack/quarterly-pack-summary.json` and `quarterly-pack/quarterly-pack-report.md` exist and are policy-clean for release.
- `upgrade-rebase/upgrade-rebase-summary.json` has `status: passed`.
- `upgrade-rebase/upgrade-rebase-report.md` documents diff contract, overlay rebase contract, CLI integration contract, and rollback safety checks.
- `report/sha256-checksums.txt` exists and includes release gate outputs.
- `sbom/dotnet-packages.json` exists unless explicitly skipped with policy exception.

## 2) Package build must emit reproducibility artifacts

Run `tools/release/Invoke-PackageBuild.ps1` for the same RC commit and confirm:

- `bundle/stigforge-cli-<runtime>.zip` exists.
- `bundle/stigforge-app-<runtime>.zip` exists.
- `manifest/release-package-manifest.json` exists and references release gate and dependency evidence.
- `manifest/reproducibility-evidence.json` exists and records commit, logs, dependency inventory, and linked gate artifacts.
- `manifest/sha256-checksums.txt` exists and hashes all package artifacts.
- `sbom/dotnet-packages.json` exists (unless intentionally skipped with approval).

## 3) Functional mission validation

- Validate import -> build -> apply -> verify -> export in a clean environment.
- Validate at least one manual-control flow (answer + evidence + summary).
- Validate `export-emass` package passes validator without errors.
- Validate `support-bundle` output contains expected manifest and redacted artifacts.

## 4) Upgrade, rebase, and rollback assurance

- Validate upgrade from previous production release to RC.
- Validate overlay rebase workflow against quarterly content update.
- Validate rollback/uninstall path and post-rollback operability.
- Validate data retention for `.stigforge` packs, overlays, and profile state.

## 5) Workflow promotion checks

- `release-package.yml` manual run succeeds and uploads package + release gate artifacts.
- `vm-smoke-matrix.yml` succeeds for `win11`, `server2019`, and `server2022`.
- VM artifacts include release-gate, stability-budget, and upgrade/rebase evidence for each runner.

## 6) Go / No-Go

Go only when all conditions hold:

- Automated release/security/compatibility gates pass.
- Reproducibility artifacts and checksums are complete and auditable.
- Upgrade/rebase and rollback evidence is present and reviewed.
- No unresolved critical or high-risk findings remain.
- Release owner and approver sign off on the exact RC commit hash.

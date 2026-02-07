# STIGForge Ship Readiness Checklist

Use this checklist before declaring a build release-ready.

## 1) Automated quality gates

- Run `tools/release/Invoke-ReleaseGate.ps1` on the release candidate commit.
- Confirm `release-gate-report.md` shows `Result: PASS`.
- Confirm `release-gate-summary.json` has all steps with `succeeded: true`.
- Confirm `security/reports/security-gate-report.md` shows 0 unresolved vulnerabilities, 0 rejected licenses, and 0 secret findings.
- Confirm `security/reports/security-gate-summary.json` aligns with release expectations.
- Confirm `sha256-checksums.txt` exists and includes all generated artifacts.
- Confirm dependency inventory exists at `sbom/dotnet-packages.json` (unless intentionally skipped).

## 2) Functional verification

- Validate import -> build -> apply -> verify -> export path in a clean environment.
- Validate at least one manual-control workflow (answer + evidence attach + summary).
- Validate `export-emass` output passes package validator with no errors.
- Validate `support-bundle` command produces a zip and manifest for triage.

## 3) Security and compliance

- Validate dependency/license/secrets scans are completed and policy exceptions are reviewed.
- Validate signing status for installer and CLI artifacts.
- Validate published checksums match produced release binaries.
- Validate release notes include known limitations and remediation guidance.

## 4) Upgrade and rollback

- Validate upgrade from previous production release.
- Validate rollback or uninstall path.
- Validate DB compatibility and data retention expectations.
- Validate no data loss in `.stigforge` content packs, overlays, and profiles.

## 5) Release package contents

- Installer artifacts (MSI/MSIX or equivalent)
- CLI artifact(s)
- `release-gate-report.md`
- `release-gate-summary.json`
- `security-gate-report.md`
- `security-gate-summary.json`
- `sha256-checksums.txt`
- Dependency inventory (SBOM/dependency report)
- Release notes

## 6) Workflow promotion checks

- `release-package.yml` manual run completes successfully.
- `vm-smoke-matrix.yml` succeeds for all runner labels (`win11`, `server2019`, `server2022`).
- VM smoke artifacts are attached and reviewed before promotion.

## 7) Go / No-Go decision

Go only when:

- All automated gates pass.
- No unresolved high/critical security findings.
- Upgrade and rollback checks pass.
- Artifact signing/checksum verification passes.
- Release owner and approver both sign off.

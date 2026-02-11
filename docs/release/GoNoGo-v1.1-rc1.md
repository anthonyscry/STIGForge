# v1.1 RC1 Go/No-Go Record

Date (UTC): 2026-02-09

## Candidate Snapshot

- Candidate commit (`git rev-parse HEAD`): `0b0f5ed206f6b22dcf58f82a543cf0ea8a796ce3`
- Release gate evidence root: `.artifacts/release-gate/v1.1-rc1`
- Package evidence root: `.artifacts/release-package/v1.1-rc1`
- Working tree state at evaluation: clean
- Release workflow run (`release-package.yml`): `https://github.com/anthonyscry/STIGForge/actions/runs/21814432313` (success)
- VM workflow run (`vm-smoke-matrix.yml`): `https://github.com/anthonyscry/STIGForge/actions/runs/21814963973` (success)

## Checklist Evaluation

| Checklist Area | Status | Evidence | Notes |
|---|---|---|---|
| 1) Release gate must pass with complete evidence | PASS | `.artifacts/release-gate/v1.1-rc1/report/release-gate-summary.json`, `.artifacts/release-gate/v1.1-rc1/security/reports/security-gate-summary.json`, `.artifacts/release-gate/v1.1-rc1/quarterly-pack/quarterly-pack-summary.json`, `.artifacts/release-gate/v1.1-rc1/upgrade-rebase/upgrade-rebase-summary.json` | `overallPassed=true`, quarterly `decision=pass`, upgrade/rebase `status=passed`, parity contract step passed. |
| 2) Package build must emit reproducibility artifacts | PASS | `.artifacts/release-package/v1.1-rc1/manifest/release-package-manifest.json`, `.artifacts/release-package/v1.1-rc1/manifest/reproducibility-evidence.json`, `.artifacts/release-package/v1.1-rc1/manifest/sha256-checksums.txt` | CLI/App bundles produced; release-gate linkage `status=linked`. |
| 3) Functional mission validation | PARTIAL | `tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs`, `tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs`, `tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs` | Automated coverage exists, but clean-environment operator UAT checklist steps are not yet executed as RC signoff evidence. |
| 4) Upgrade, rebase, and rollback assurance | PARTIAL | `.artifacts/release-gate/v1.1-rc1/upgrade-rebase/upgrade-rebase-summary.json`, `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs`, `tests/STIGForge.IntegrationTests/Apply/SnapshotIntegrationTests.cs` | Diff/rebase/rollback contracts pass; manual upgrade-from-previous-release and post-rollback operability validation still required. |
| 5) Workflow promotion checks | PASS (automation) | `.github/workflows/release-package.yml`, `.github/workflows/vm-smoke-matrix.yml` | Both workflows succeeded for commit `0b0f5ed206f6b22dcf58f82a543cf0ea8a796ce3`. VM run used temporary self-hosted runner labels to clear queue and execute evidence steps. |

## Blocking Items Before Go

1. Execute manual checklist validation in target environment for section 3 (mission flow + manual control + support bundle review evidence).
2. Execute manual checklist validation in target environment for section 4 (upgrade from previous production release, rollback/uninstall, data retention).

## Decision

Decision: NO-GO (temporary)

Reason: Automated gates and artifacts are passing, but required manual RC checklist validations and promotion workflow evidence are not yet complete for a pinned immutable release commit.

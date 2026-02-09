# v1.1 RC1 Go/No-Go Record

Date (UTC): 2026-02-09

## Candidate Snapshot

- Candidate commit (`git rev-parse HEAD`): `949ca8e50793ab75c165f93315c9c8c560718f85`
- Release gate evidence root: `.artifacts/release-gate/phase10-rc`
- Package evidence root: `.artifacts/release-package/phase10-rc`
- Working tree state at evaluation: dirty (uncommitted release-target changes present)

## Checklist Evaluation

| Checklist Area | Status | Evidence | Notes |
|---|---|---|---|
| 1) Release gate must pass with complete evidence | PASS | `.artifacts/release-gate/phase10-rc/report/release-gate-summary.json`, `.artifacts/release-gate/phase10-rc/security/reports/security-gate-summary.json`, `.artifacts/release-gate/phase10-rc/quarterly-pack/quarterly-pack-summary.json`, `.artifacts/release-gate/phase10-rc/upgrade-rebase/upgrade-rebase-summary.json` | `overallPassed=true`, quarterly `decision=pass`, upgrade/rebase `status=passed`, parity contract step passed. |
| 2) Package build must emit reproducibility artifacts | PASS | `.artifacts/release-package/phase10-rc/manifest/release-package-manifest.json`, `.artifacts/release-package/phase10-rc/manifest/reproducibility-evidence.json`, `.artifacts/release-package/phase10-rc/manifest/sha256-checksums.txt` | CLI/App bundles produced; release-gate linkage `status=linked`. |
| 3) Functional mission validation | PARTIAL | `tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs`, `tests/STIGForge.IntegrationTests/Export/EmassExporterIntegrationTests.cs`, `tests/STIGForge.UnitTests/Cli/SupportBundleBuilderTests.cs` | Automated coverage exists, but clean-environment operator UAT checklist steps are not yet executed as RC signoff evidence. |
| 4) Upgrade, rebase, and rollback assurance | PARTIAL | `.artifacts/release-gate/phase10-rc/upgrade-rebase/upgrade-rebase-summary.json`, `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs`, `tests/STIGForge.IntegrationTests/Apply/SnapshotIntegrationTests.cs` | Diff/rebase/rollback contracts pass; manual upgrade-from-previous-release and post-rollback operability validation still required. |
| 5) Workflow promotion checks | NOT RUN | `.github/workflows/release-package.yml`, `.github/workflows/vm-smoke-matrix.yml` | Required GitHub workflow runs (`release-package`, `vm-smoke-matrix`) have not been executed for this RC candidate; current RC changes are uncommitted/unpushed so remote workflow parity evidence is not yet valid for signoff. |

## Blocking Items Before Go

1. Pin an immutable RC commit for final review and regenerate release evidence from that exact commit.
2. Execute manual checklist validation in target environment for section 3 (mission flow + manual control + support bundle review evidence).
3. Execute manual checklist validation in target environment for section 4 (upgrade from previous production release, rollback/uninstall, data retention).
4. Run and archive `release-package.yml` and `vm-smoke-matrix.yml` outputs for the same RC commit.

## Decision

Decision: NO-GO (temporary)

Reason: Automated gates and artifacts are passing, but required manual RC checklist validations and promotion workflow evidence are not yet complete for a pinned immutable release commit.

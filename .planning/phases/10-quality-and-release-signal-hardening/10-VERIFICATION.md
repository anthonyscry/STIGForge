# Phase 10 Verification - Quality and Release Signal Hardening

## Phase Goal

Restore canonical QA closure evidence for `QA-01`, `QA-02`, and `QA-03` so
requirement closure remains machine-verifiable and fail-closed across planning,
verification, and promotion enforcement artifacts.

## Observable Truths

| # | Truth | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Phase 10 outputs capture deterministic quality and release-signal coverage aligned to QA requirements. | VERIFIED | `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-01-SUMMARY.md`, `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-02-SUMMARY.md` |
| 2 | Phase 13 enforcement wiring requires mandatory release evidence with fail-closed blocker semantics. | VERIFIED | `tools/release/Test-ReleaseEvidenceContract.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release-package.yml`, `.github/workflows/vm-smoke-matrix.yml`, `tools/release/Invoke-PackageBuild.ps1` |
| 3 | QA closure reverts to unresolved when any required source (traceability, summary metadata, or verification evidence) drifts. | VERIFIED | This artifact's Three-Source Cross-Check fail-closed rule and `.planning/REQUIREMENTS.md` traceability rows |

## Required Artifacts

| Artifact | Expected | Status | Details |
| --- | --- | --- | --- |
| `.planning/phases/10-quality-and-release-signal-hardening/10-VERIFICATION.md` | Canonical QA evidence mapping and three-source cross-check | VERIFIED | Contains Observable Truths, Requirement Evidence Mapping, Promotion Wiring Checks, and Three-Source Cross-Check sections |
| `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-01-SUMMARY.md` | Machine-readable `requirements-completed` metadata for QA IDs | VERIFIED | Frontmatter includes `requirements-completed` with `QA-01`, `QA-02`, `QA-03` |
| `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-02-SUMMARY.md` | Machine-readable `requirements-completed` metadata for QA IDs | VERIFIED | Frontmatter includes identical `requirements-completed` metadata |
| `.planning/REQUIREMENTS.md` | QA checklist and traceability rows reconciled to Completed | VERIFIED | QA checklist entries marked complete and traceability rows map QA IDs to Phase 13 as Completed |
| `tools/release/Test-ReleaseEvidenceContract.ps1` | Shared fail-closed validator for missing-proof/failed-check/disabled-check blockers | VERIFIED | Mandatory evidence contract validator used across promotion surfaces |

## Key Link Verification

| From | To | Via | Status | Details |
| --- | --- | --- | --- | --- |
| `.planning/phases/10-quality-and-release-signal-hardening/10-VERIFICATION.md` | `.planning/REQUIREMENTS.md` | Three-source cross-check aligns verification mapping with traceability completion | WIRED | QA rows are checked against verification evidence and summary metadata before closure |
| `.planning/phases/10-quality-and-release-signal-hardening/10-VERIFICATION.md` | `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-01-SUMMARY.md` | Requirement mapping references machine-readable `requirements-completed` | WIRED | Summary metadata is required source for QA closure |
| `.planning/phases/10-quality-and-release-signal-hardening/10-VERIFICATION.md` | `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-02-SUMMARY.md` | Requirement mapping references matching `requirements-completed` metadata | WIRED | Metadata parity is required for deterministic closure verification |
| `.planning/phases/10-quality-and-release-signal-hardening/10-quality-and-release-signal-hardening-0[12]-SUMMARY.md` | `.planning/REQUIREMENTS.md` | QA closure metadata reconciles with checklist/traceability state | WIRED | Traceability status remains valid only while metadata stays aligned |
| `tools/release/Test-ReleaseEvidenceContract.ps1` | `.github/workflows/ci.yml` | CI must pass mandatory release evidence contract checks | WIRED | Missing, failed, or disabled required evidence blocks promotion |
| `tools/release/Test-ReleaseEvidenceContract.ps1` | `.github/workflows/release-package.yml` | Release-package flow enforces mandatory contract and disabled-check blockers | WIRED | `run_release_gate=false` is treated as blocking disabled-check |
| `tools/release/Test-ReleaseEvidenceContract.ps1` | `.github/workflows/vm-smoke-matrix.yml` | VM workflow validates required evidence contract paths | WIRED | VM promotion evidence cannot bypass missing/failed contract checks |
| `tools/release/Test-ReleaseEvidenceContract.ps1` | `tools/release/Invoke-PackageBuild.ps1` | Package preflight validates mandatory release evidence before archive creation | WIRED | Packaging fails closed and persists blocked metadata when contract checks fail |

## Requirement Evidence Mapping

| Requirement | Source implementation evidence (Phase 10) | Promotion enforcement evidence (Phase 13) | Mapping status |
| --- | --- | --- | --- |
| QA-01 | Phase 10 Plan 01 summary documents deterministic CI automated coverage for diff/rebase and conflict handling paths, including targeted and full unit/integration test runs | Shared validator enforced in CI/release/VM/package flows with fail-closed blocker taxonomy (`missing-proof`, `failed-check`, `disabled-check`) | present |
| QA-02 | Phase 10 Plan 01 summary documents VM/release gate parity validation signals and explicit workflow checks for required evidence presence and success | Phase 13 mandatory release-gate contract enforcement blocks promotion surfaces when required release evidence is missing, failed, or disabled | present |
| QA-03 | Phase 10 Plan 02 summary documents trendable stability and compatibility artifacts enforced in CI/release/VM promotion checks | Phase 13 package and workflow preflight enforce deterministic release evidence contract before promotion/package archive generation | present |

## Promotion Wiring Checks

| Check area | Source | Required enforcement | Status |
| --- | --- | --- | --- |
| Shared contract validation | `tools/release/Test-ReleaseEvidenceContract.ps1` | Deterministic required-check evaluation with standardized blocker semantics | WIRED |
| CI gate enforcement | `.github/workflows/ci.yml` | Contract validator execution plus required-proof artifact checks | WIRED |
| Release-package enforcement | `.github/workflows/release-package.yml` | Contract validator execution, disabled-check blocking, and required uploads | WIRED |
| VM smoke enforcement | `.github/workflows/vm-smoke-matrix.yml` | Contract validator execution against VM evidence roots | WIRED |
| Package pre-archive fail-closed path | `tools/release/Invoke-PackageBuild.ps1` | Mandatory contract preflight before `Compress-Archive` with blocked-state metadata | WIRED |

## Three-Source Cross-Check

| Requirement | REQUIREMENTS traceability and checklist | Summary metadata (`requirements-completed`) | Verification evidence mapping | Verdict |
| --- | --- | --- | --- | --- |
| QA-01 | present (`Completed` and checked) | present (`QA-01`) in both Phase 10 summaries | present | closed |
| QA-02 | present (`Completed` and checked) | present (`QA-02`) in both Phase 10 summaries | present | closed |
| QA-03 | present (`Completed` and checked) | present (`QA-03`) in both Phase 10 summaries | present | closed |

Fail-closed reversion rule: treat any requirement verdict as `unresolved` if one or
more required sources become missing, mismatched, or non-passing after closure
(traceability row drift, missing summary metadata, or stale/missing verification mapping).

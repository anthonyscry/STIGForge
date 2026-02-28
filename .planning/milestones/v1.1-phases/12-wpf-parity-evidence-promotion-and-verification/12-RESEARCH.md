# Phase 12: WPF Parity Evidence Promotion and Verification - Research

**Researched:** 2026-02-16
**Domain:** WPF parity evidence closure, promotion artifact wiring, and requirement-verification traceability
**Confidence:** HIGH

<user_constraints>
## User Constraints

### Locked Decisions
No `*-CONTEXT.md` exists for this phase, so there are no additional locked decisions from `/gsd-discuss-phase`.

### Provided Phase Constraints (authoritative input)
- Offline-first operation must be preserved.
- PowerShell 5.1 target must be preserved.
- Fail-closed semantics for closure/verification artifacts must be preserved.

### Claude's Discretion
- Choose the concrete evidence contracts and artifact field wiring that prove `WP-01`, `WP-02`, and `WP-03` in promotion flows.
- Choose where to anchor WPF parity evidence in release-gate and packaging evidence catalogs.
- Choose verification command set and requirement cross-check structure for Phase 09/12 closure artifacts.

### Deferred Ideas (OUT OF SCOPE)
None specified for this phase.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| WP-01 | WPF app exposes diff/rebase workflow end-to-end without CLI fallback for standard operator paths. | Use Phase 09 plan/summary evidence plus a new explicit WPF workflow contract step in release-gate summaries and workflow validators; map in Phase 09 verification artifact and Phase 12 verification outputs. |
| WP-02 | WPF status and mission summaries match CLI semantics for blocking failures, warnings, and optional skips. | Use mission summary parity contract (`BundleMissionSummaryServiceTests`) and require explicit evidence linkage in upgrade/rebase summary, Phase 09 verification mapping, and release docs/workflow checks. |
| WP-03 | WPF surfaces actionable recovery guidance for failed apply/rebase paths. | Use Phase 09 summary/doc evidence for required artifacts/next action/rollback guidance, add dedicated promotion evidence signal and verification mapping for recovery contract presence. |
</phase_requirements>

## Summary

Phase 12 should be planned as an evidence-promotion and verification-closure phase, not a new feature phase. The underlying WPF parity behavior was implemented in Phase 09 (`09-...-01-SUMMARY.md`, `09-...-02-SUMMARY.md`), but closure is still orphaned because there is no Phase 09 verification artifact and no machine-verifiable WP closure metadata path yet.

The existing release gate already emits upgrade/rebase evidence (`upgrade-rebase-summary.json`, `upgrade-rebase-report.md`) and enforces a parity contract step (`upgrade-rebase-parity-contract`), but that step currently proves mission summary parity only. It does not explicitly prove WPF end-to-end diff/rebase workflow contract or WPF recovery guidance contract as first-class promotion evidence signals.

**Primary recommendation:** Implement Phase 12 using the Phase 11 three-source closure pattern, plus explicit WPF contract evidence keys in release-gate summaries/workflow validations/package evidence catalogs, so `WP-01`..`WP-03` can move from orphaned to machine-verifiable closed under fail-closed rules.

## Standard Stack

### Core
| Library/Component | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 8.0.0 (`global.json`) | Build/runtime baseline | Repository baseline for all current app/test/gate assets |
| WPF app (`net8.0-windows`) | .NET 8 | UI surface for WP requirements | WP requirements are explicitly WPF parity requirements |
| PowerShell gate scripts (`tools/release/*.ps1`) | Windows PowerShell-compatible style | Release/promotion evidence generation | Existing authoritative source for promotion artifacts and fail/ pass contract outputs |
| GitHub Actions workflows (`ci.yml`, `release-package.yml`, `vm-smoke-matrix.yml`) | current repo workflows | Promotion enforcement points | Already enforce parity/trend evidence; Phase 12 should extend these checks |

### Supporting
| Library/Component | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| CommunityToolkit.Mvvm | 8.4.0 | WPF MVVM implementation substrate | For any additional WPF-facing contract probes/coverage tied to ViewModels |
| xUnit | 2.9.3 | Contract tests | For explicit WP contract tests included in release gate evidence |
| FluentAssertions | 8.8.0 | Expressive assertions | For deterministic, operator-semantic contract assertions |
| Existing verification artifact format (`08-VERIFICATION.md`) | in-repo pattern | Requirement evidence mapping and cross-check | For Phase 09 verification artifact backfill and WP closure |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Reusing release-gate summary schema | Ad-hoc new artifact file tree for WPF parity only | Increases wiring complexity and weakens existing promotion contract path |
| Three-source closure pattern (requirements + summary metadata + verification mapping) | Manual checklist-only closure | Not machine-verifiable; fails established fail-closed closure model |
| Extending existing parity contract evidence checks | Relying on docs/manual signoff text only | Insufficient for deterministic automated promotion gating |

**Installation/restore baseline:**
```bash
dotnet restore STIGForge.sln --nologo -p:EnableWindowsTargeting=true
```

## Architecture Patterns

### Recommended Project Structure
```
.planning/
├── REQUIREMENTS.md                                  # WP checkbox + traceability status
├── phases/09-wpf-parity-and-recovery-ux/            # Source behavior summaries + new 09-VERIFICATION.md
└── phases/12-wpf-parity-evidence-promotion-and-verification/
    └── 12-VERIFICATION.md                            # Phase 12 closure + wiring verification

tools/release/
├── Invoke-ReleaseGate.ps1                            # Emits upgrade/rebase summary/report + requiredEvidence + steps
└── Invoke-PackageBuild.ps1                           # Release-gate evidence catalog linked into package manifests

.github/workflows/
├── ci.yml                                            # Validates required parity evidence in summary JSON
├── release-package.yml                               # Promotion path checks
└── vm-smoke-matrix.yml                               # Per-runner parity evidence checks
```

### Pattern 1: Three-Source Requirement Closure (reuse Phase 11 model)
**What:** Requirement is closed only when traceability row, summary metadata, and verification mapping all agree.
**When to use:** `WP-01`, `WP-02`, `WP-03` closure.
**Example:**
```markdown
<!-- Source: .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md -->
| Requirement | REQUIREMENTS.md traceability row | Summary metadata (`requirements-completed`) | Verification evidence mapping | Verdict |
|---|---|---|---|---|
| WP-01 | present (`Completed`) | present (`WP-01`) | present | closed |
```

### Pattern 2: Promotion Evidence as Machine-Readable Contract Signals
**What:** Promotion workflows validate explicit step presence and success in JSON summaries, not just overall status.
**When to use:** WPF parity contract promotion checks for CI/release/VM flows.
**Example:**
```powershell
# Source: .github/workflows/release-package.yml
$summary = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
$parityStep = @($summary.steps | Where-Object { $_.name -eq 'upgrade-rebase-parity-contract' })
if ($parityStep.Count -ne 1) { throw "Upgrade/rebase parity evidence step missing from summary." }
if (-not [bool]$parityStep[0].succeeded) { throw "Upgrade/rebase parity evidence step failed." }
```

### Pattern 3: Release-Gate Required Evidence Catalog Drives Downstream Linking
**What:** Required evidence keys are emitted by release gate and linked by package build manifests.
**When to use:** Adding explicit WPF parity evidence so package reproducibility artifacts can prove it.
**Example:**
```powershell
# Source: tools/release/Invoke-PackageBuild.ps1
$releaseGateCatalog = @(
  [pscustomobject]@{ key = "upgradeRebaseSummary"; relativePath = "upgrade-rebase/upgrade-rebase-summary.json"; required = $true },
  [pscustomobject]@{ key = "upgradeRebaseReport";  relativePath = "upgrade-rebase/upgrade-rebase-report.md";  required = $true }
)
```

### Anti-Patterns to Avoid
- **WP closure by narrative only:** Do not mark `WP-*` complete from plan summaries alone; require verification artifact + metadata alignment.
- **Single-status gating:** Do not rely only on `summary.status == passed`; require explicit WPF evidence step presence/success.
- **New standalone gate path:** Do not fork a separate WPF gate pipeline when upgrade/rebase evidence path already exists.
- **Fail-open missing evidence:** Missing WPF evidence artifacts/keys must block closure/promotion checks.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Requirement closure logic | New custom closure workflow | Existing three-source pattern from Phase 11 + `08-VERIFICATION.md` style | Already proven to eliminate orphaning with deterministic checks |
| Promotion evidence schema | New custom artifact model for WPF-only checks | Existing `upgrade-rebase-summary.json` `steps` + `requiredEvidence` fields | Already consumed by CI/release/VM workflows and docs |
| Packaging evidence linkage | Bespoke manifest wiring for new proof files | Existing `Invoke-PackageBuild.ps1` release gate catalog linkage | Keeps reproducibility/reporting contract stable |
| Recovery semantics proof | New independent parser for UI text | Existing WPF summary artifacts + contract tests + verification mapping | Reduces drift and keeps WP-03 evidence tied to implemented behavior |

**Key insight:** Phase 12 is primarily evidence topology work (verification + promotion wiring), not behavior invention.

## Common Pitfalls

### Pitfall 1: Treating existing parity contract as full WP coverage
**What goes wrong:** `upgrade-rebase-parity-contract` currently validates mission summary semantics but does not explicitly represent end-to-end WPF diff/rebase flow contract or recovery guidance contract.
**Why it happens:** Name suggests full parity; underlying test selection is narrower.
**How to avoid:** Add explicit WPF workflow/recovery contract signals in release gate `steps`/`requiredEvidence` and validate them in CI/release/VM workflows.
**Warning signs:** Promotion checks only look for one parity step while WP requirements remain orphaned.

### Pitfall 2: Closing WP requirements without Phase 09 verification artifact
**What goes wrong:** `REQUIREMENTS.md` can be updated without canonical requirement-to-evidence mapping for WP scope.
**Why it happens:** Phase 09 has summaries but no `09-VERIFICATION.md`.
**How to avoid:** Create `09-VERIFICATION.md` with WP mapping and three-source cross-check before setting `WP-*` to completed.
**Warning signs:** Traceability row says completed but no phase verification artifact exists.

### Pitfall 3: Evidence present but not promoted into package/release manifests
**What goes wrong:** Gate emits evidence, but release package manifest/reproducibility linkage does not require or expose new WPF evidence explicitly.
**Why it happens:** Catalog keys in `Invoke-PackageBuild.ps1` are static and easy to forget when adding contract areas.
**How to avoid:** Extend release gate catalog + docs/workflow validators together in one change set.
**Warning signs:** New evidence appears in logs but not in manifest evidence listing.

### Pitfall 4: Fail-open optionalization in promotion paths
**What goes wrong:** Workflow conditions or optional flags allow packaging/promotion without explicit WPF evidence checks.
**Why it happens:** Existing patterns include input-based conditionals and per-workflow drift.
**How to avoid:** Keep WPF parity evidence checks mandatory wherever upgrade/rebase evidence is mandatory.
**Warning signs:** Build/package steps can succeed when WPF evidence key is absent.

### Pitfall 5: Breaking PowerShell 5.1 compatibility while extending checks
**What goes wrong:** New script constructs may work on `pwsh` but fail on Windows PowerShell 5.1 environments.
**Why it happens:** CI primarily runs `pwsh` in workflows, while project constraint remains PS 5.1 target.
**How to avoid:** Keep script syntax/features 5.1-compatible and validate command paths/options accordingly.
**Warning signs:** Local 5.1 runs fail while GitHub-hosted checks pass.

## Code Examples

Verified patterns from repository sources:

### Requirement mapping + fail-closed cross-check
```markdown
<!-- Source: .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md -->
## Requirement Evidence Mapping
| Requirement | Requirement statement | Evidence from Phase deliverables | Evidence status |

## Three-Source Cross-Check
| Requirement | REQUIREMENTS.md traceability row | Summary metadata (`requirements-completed`) | Verification evidence mapping | Verdict |
```

### Release-gate contract step emission
```powershell
# Source: tools/release/Invoke-ReleaseGate.ps1
$steps += New-Step -Name "upgrade-rebase-diff-contract" -Command $upgradeRebaseDiffCommand -LogPath (...)
$steps += New-Step -Name "upgrade-rebase-overlay-contract" -Command $upgradeRebaseOverlayCommand -LogPath (...)
$steps += New-Step -Name "upgrade-rebase-parity-contract" -Command $upgradeRebaseParityCommand -LogPath (...)
$steps += New-Step -Name "upgrade-rebase-cli-contract" -Command $upgradeRebaseCliCommand -LogPath (...)
$steps += New-Step -Name "upgrade-rebase-rollback-safety" -Command $upgradeRebaseRollbackCommand -LogPath (...)
```

### Workflow fail-closed evidence assertion
```powershell
# Source: .github/workflows/ci.yml
if (-not (Test-Path -LiteralPath $summaryPath)) {
  throw "Upgrade/rebase summary artifact missing at $summaryPath"
}

$summary = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
if ($summary.status -ne 'passed') {
  throw "Upgrade/rebase validation status is '$($summary.status)': $($summary.message)"
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| WP requirements implemented in Phase 09 but not machine-closed | Phase 11 established three-source closure model for UR requirements | 2026-02-16 (Phase 11) | Provides direct template to close WP orphaning safely |
| Parity evidence check focused on one step (`upgrade-rebase-parity-contract`) | Promotion flows validate explicit summary step presence/success (CI/release/VM) | 2026-02-09 (Phase 10) | Enables deterministic gating by evidence fields rather than narrative proof |
| Missing phase verification artifacts for 08-13 in audit snapshot | Phase 08 verification backfilled and UR closure reconciled | 2026-02-16 | Demonstrates exact closure mechanics Phase 12 should replicate for WP |

**Deprecated/outdated for this phase:**
- Manual-only closure interpretation for `WP-*` requirements.
- Treating plan summaries as sufficient closure evidence without verification artifact + metadata cross-check.

## Open Questions

1. **What exact contract granularity should be added for WPF parity promotion evidence?**
   - What we know: Current release-gate contract set has one parity step tied to mission summary semantics.
   - What's unclear: Whether to split into dedicated `wpf-diff-rebase-contract` and `wpf-recovery-guidance-contract` steps, or extend one parity step with explicit sub-signals.
   - Recommendation: Prefer separate explicit steps for diagnosability and direct mapping to `WP-01` and `WP-03`.

2. **Should Phase 12 create only Phase 09 verification artifact, or also a Phase 12 verification report?**
   - What we know: Roadmap exit criteria explicitly require a Phase 09 verification artifact and evidence flow wiring.
   - What's unclear: Whether project convention expects both source-phase (`09-VERIFICATION.md`) and closure-phase (`12-VERIFICATION.md`) artifacts.
   - Recommendation: Create both; use `09-VERIFICATION.md` for requirement behavior mapping and `12-VERIFICATION.md` for promotion wiring and closure verification.

3. **What is the minimum deterministic evidence set for WP-03 recovery guidance?**
   - What we know: WPF docs/summaries state required artifacts + next action + rollback guidance are surfaced.
   - What's unclear: Which automated assertion should be gate-critical to avoid brittle UI text coupling.
   - Recommendation: Gate on contract tests that assert structured recovery guidance semantics, then reference docs/screenshots as supplemental evidence.

## Sources

### Primary (HIGH confidence)
- `.planning/ROADMAP.md` - Phase 12 goal, requirements, and exit criteria.
- `.planning/REQUIREMENTS.md` - `WP-01`..`WP-03` statements and traceability status.
- `.planning/v1.1-MILESTONE-AUDIT.md` - orphaning and integration/flow gap evidence for WP requirements.
- `.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-PLAN.md` - WP-01 behavior and artifacts planned.
- `.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-PLAN.md` - WP-02/WP-03 behavior and artifacts planned.
- `.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-01-SUMMARY.md` - implemented WPF diff/rebase parity evidence.
- `.planning/milestones/v1.1-phases/09-wpf-parity-and-recovery-ux/09-wpf-parity-and-recovery-ux-02-SUMMARY.md` - implemented WPF severity/recovery evidence.
- `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md` - canonical three-source cross-check pattern.
- `.planning/phases/11-verification-backfill-for-upgrade-rebase/11-verification-backfill-for-upgrade-rebase-01-PLAN.md` - fail-closed requirement closure implementation model.
- `tools/release/Invoke-ReleaseGate.ps1` - contract step generation and summary/report schema.
- `tools/release/Invoke-PackageBuild.ps1` - release gate evidence catalog linkage into package manifests.
- `.github/workflows/ci.yml` - evidence presence/success enforcement pattern.
- `.github/workflows/release-package.yml` - promotion evidence enforcement for package path.
- `.github/workflows/vm-smoke-matrix.yml` - per-runner promotion evidence enforcement.
- `docs/release/UpgradeAndRebaseValidation.md` - documented contract set and required artifacts.
- `docs/release/ReleaseCandidatePlaybook.md` - release decision evidence expectations.
- `docs/release/ShipReadinessChecklist.md` - mandatory evidence checklist references.
- `docs/WpfGuide.md` and `docs/UserGuide.md` - operator-facing WPF parity and recovery semantics.

### Secondary (MEDIUM confidence)
- `tests/STIGForge.UnitTests/Services/BundleMissionSummaryServiceTests.cs` - current parity contract proof scope for mission summary semantics.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - direct from repository project/workflow/script manifests.
- Architecture: HIGH - based on implemented Phase 09/10/11 artifacts and current promotion wiring.
- Pitfalls: MEDIUM-HIGH - grounded in audit + current wiring, with some forward-looking risk characterization.

**Research date:** 2026-02-16
**Valid until:** 2026-03-18

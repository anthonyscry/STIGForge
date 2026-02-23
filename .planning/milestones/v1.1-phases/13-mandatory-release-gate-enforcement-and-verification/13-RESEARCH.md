# Phase 13: Mandatory Release-Gate Enforcement and Verification - Research

**Researched:** 2026-02-16
**Domain:** Release-gate enforcement, promotion evidence contracts, and fail-closed CI/release/VM workflows
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
### Required evidence set
- Promotion requires a full proof bundle (contract checks, release summary, CI/VM evidence, and verification report).
- Missing any required proof item blocks promotion.
- Evidence locations and names are deterministic (fixed paths/names).
- CI, release, and VM flows share one mandatory core evidence set, with flow-specific extras allowed.

### Failure behavior policy
- Missing required proof must stop execution before packaging artifacts are produced.
- Failures must be explicit fail-closed blockers, not delayed soft warnings.
- Failure output should prioritize actionable recovery guidance.

### Diagnostics and recovery guidance
- Failure output uses a checklist-first format: what is missing, why blocked, and exact next actions.
- Blocked runs include copy-paste recovery commands.
- Failure categories are explicit blocker types (missing-proof, failed-check, disabled-check), not generic failure text.
- Recovery guidance appears both inline in workflow output and in persisted report artifacts.

### Cross-flow consistency
- CI, release, and VM enforce identical blocked/fail semantics.
- All three flows validate the same required contract signals.
- Disabling any required check is treated as a blocker in that flow.
- Contract signal naming is standardized across workflows and reports.

### Claude's Discretion
- Exact wording and formatting of checklist/recovery output, as long as it stays actionable and consistent.
- Exact ordering of contract checks, as long as fail-closed stop-before-package semantics are preserved.
- Additional non-blocking diagnostics fields that improve operator triage without weakening enforcement.

### Deferred Ideas (OUT OF SCOPE)
- Warning-only bypass behavior for required checks was discussed but deferred. This would weaken fail-closed enforcement and belongs in a separate future phase if policy changes.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| QA-01 | CI includes deterministic automated coverage for diff/rebase core workflows and conflict handling paths. | Keep CI `Invoke-ReleaseGate.ps1` + explicit JSON summary assertions for required upgrade/rebase/WPF contract steps; enforce common blocker taxonomy and fail on missing/failed/disabled checks. |
| QA-02 | VM/release gate evidence includes diff/rebase and WPF parity validation signals for go/no-go review. | Standardize a mandatory evidence contract (summary/report/checksums + upgrade-rebase summary/report + explicit WPF step signals) and enforce identical checks in `ci.yml`, `release-package.yml`, and `vm-smoke-matrix.yml`. |
| QA-03 | Stability and compatibility gates for v1.1 emit trendable artifacts that flag regression drift before promotion. | Preserve mandatory quarterly and stability artifacts (`quarterly-pack-*`, `stability-budget-*`) as required proof items and fail closed on missing/non-passing summaries and reports. |
</phase_requirements>

## Summary

Current implementation already has strong gate signal generation (`Invoke-ReleaseGate.ps1`) and workflow-level validation for upgrade/rebase and quarterly evidence, but promotion is not yet universally fail-closed. The key known gap is in `release-package.yml`: the `run_release_gate` input can disable pre-package gate execution and evidence validation while package build still runs. This violates the locked requirement that disabled required checks are blockers and that missing proof must block packaging.

`Invoke-PackageBuild.ps1` links release-gate evidence into package reproducibility metadata, but currently treats missing required release-gate artifacts as `partial` metadata status rather than a hard block. This is the second major fail-open path. Enforcing mandatory release-gate evidence must happen both at workflow entry and inside package build logic so local/manual invocations cannot bypass policy.

For planning, the safest pattern is a single deterministic evidence contract (fixed artifact names/paths + required step names) shared by CI, release, and VM flows, with one reusable validator implementation that emits blocker categories and checklist-style recovery guidance to console and persisted artifacts. This supports QA-01/02/03 and prevents cross-flow drift.

**Primary recommendation:** Implement a single fail-closed evidence-contract validator consumed by CI, release, VM, and package-build paths, and make any missing/failed/disabled required signal block before packaging.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK | 8.0.x | Build/test execution for gate contracts and trend suites | Already pinned in all workflows and project commands; deterministic with existing test filters |
| PowerShell | 5.1-compatible scripts (`powershell`) with `pwsh` fallback | Gate orchestration, JSON/report generation, deterministic failure handling | Existing release tooling is PowerShell-first and uses strict mode + terminating errors |
| GitHub Actions workflows | YAML + `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4` | Promotion-surface enforcement and artifact retention | Current repo standard and supports explicit fail conditions + artifact policy controls |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `ConvertFrom-Json` | Built-in (Windows PS 5.1 / PS Core) | Parse machine-readable gate summaries for enforcement | Every workflow/script step that validates required summary fields and contract steps |
| `dotnet test` filter-based contracts | .NET 8 behavior | Deterministic contract selection for diff/rebase, parity, E2E/snapshot trend checks | Required for QA-01 and QA-03 enforcement evidence |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| One shared validator module | Duplicated inline checks in each workflow | Easier short-term edits, but high drift risk and inconsistent blocker semantics |
| Fail-closed in both workflow and package script | Workflow-only checks | Leaves fail-open local/scripted packaging path (`Invoke-PackageBuild.ps1`) |

**Installation:**
```bash
# Existing stack; no new package installation required for phase policy enforcement
```

## Architecture Patterns

### Recommended Project Structure
```
tools/
└── release/
    ├── Invoke-ReleaseGate.ps1          # Produces canonical gate evidence
    ├── Invoke-PackageBuild.ps1         # Must hard-block on missing required gate proof
    ├── Run-QuarterlyRegressionPack.ps1 # Compatibility trend signal generator
    └── (new) Test-ReleaseEvidenceContract.ps1 # Shared validator for CI/release/VM/package

.github/workflows/
├── ci.yml                              # Enforces core + CI extras
├── release-package.yml                 # Must enforce pre-package mandatory gate
└── vm-smoke-matrix.yml                 # Enforces core + VM extras

.planning/phases/13-.../
└── 13-VERIFICATION.md                  # Requirement closure evidence (QA-01..03)
```

### Pattern 1: Mandatory Evidence Contract Validator
**What:** One script/function validates deterministic required artifact paths, required summary fields, and required step signals, then emits typed blockers.
**When to use:** In CI/release/VM workflows and package build preconditions.
**Example:**
```powershell
# Source: repository pattern in tools/release/Invoke-PackageBuild.ps1 and workflows
$required = @(
  @{ key='releaseGateSummary'; path='report/release-gate-summary.json' },
  @{ key='upgradeRebaseSummary'; path='upgrade-rebase/upgrade-rebase-summary.json' },
  @{ key='quarterlySummary'; path='quarterly-pack/quarterly-pack-summary.json' }
)

foreach ($item in $required) {
  $full = Join-Path $ReleaseGateRoot $item.path
  if (-not (Test-Path -LiteralPath $full)) {
    throw "[missing-proof] Required artifact missing: $($item.key) at $full"
  }
}
```

### Pattern 2: Stop-Before-Package Guard
**What:** Hard preflight gate in release workflow and package script; package steps cannot run when required proof is missing/failed/disabled.
**When to use:** Any packaging or promotion path.
**Example:**
```yaml
# Source: .github/workflows/release-package.yml pattern (to harden)
- name: Enforce mandatory pre-package gate
  shell: pwsh
  run: |
    if (-not [System.Convert]::ToBoolean('${{ inputs.run_release_gate }}')) {
      throw "[disabled-check] run_release_gate=false is not allowed for promotion packaging."
    }
```

### Pattern 3: Checklist-First Blocker Reporting
**What:** Blocker output includes category, missing/failed item, why blocked, and copy-paste recovery commands; same payload persisted in report artifact.
**When to use:** Any gate rejection in CI/release/VM/package.
**Example:**
```markdown
## Blocked: missing-proof
- Missing: upgrade-rebase/upgrade-rebase-summary.json
- Why blocked: promotion requires full deterministic proof bundle
- Next command: powershell -File .\tools\release\Invoke-ReleaseGate.ps1 -OutputRoot .\.artifacts\release-gate\latest
```

### Anti-Patterns to Avoid
- **Optional gate switch on promotion path:** Allowing `run_release_gate=false` while still building packages creates a fail-open bypass.
- **Soft-link evidence status:** Recording `partial` evidence in metadata without throwing keeps packaging alive after proof failure.
- **Per-workflow bespoke semantics:** Divergent checks/messages across CI/release/VM create drift and audit ambiguity.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON artifact parsing and validation | Ad-hoc string matching in logs | `ConvertFrom-Json` + field/step assertions | Machine-readable contracts are deterministic and less fragile |
| Artifact retention behavior | Custom upload scripts | `actions/upload-artifact@v4` with `if-no-files-found: error` for required bundles | Built-in behavior is standardized and configurable |
| Trend signal computation | New custom drift/stability mini-framework | Existing quarterly pack + stability budget generators | Existing scripts already emit trendable JSON/MD artifacts used by workflows |

**Key insight:** This phase is enforcement and consistency hardening, not new capability. Reuse existing signal generators and centralize contract validation.

## Common Pitfalls

### Pitfall 1: Gate-disable bypass in release workflow
**What goes wrong:** Packaging runs even when release gate execution is disabled.
**Why it happens:** `release-package.yml` conditions gate checks on `inputs.run_release_gate` but always runs package build.
**How to avoid:** Treat `run_release_gate=false` as `[disabled-check]` blocker for promotion path.
**Warning signs:** Successful package artifacts with no fresh release-gate artifact root.

### Pitfall 2: Missing required proof treated as metadata only
**What goes wrong:** `Invoke-PackageBuild.ps1` marks evidence as `partial` but does not fail.
**Why it happens:** Missing required artifact list is computed but never escalated to terminating error.
**How to avoid:** Throw on any missing/failed required artifact/step before creating zips.
**Warning signs:** Manifest says partial evidence while bundle zips still exist.

### Pitfall 3: Inconsistent blocker semantics across flows
**What goes wrong:** CI/release/VM fail for different reasons/messages on same missing signal.
**Why it happens:** Duplicated inline validation logic diverges over time.
**How to avoid:** Shared validator and standardized blocker categories/names.
**Warning signs:** Different required step names or mismatched error categories between workflows.

### Pitfall 4: Artifact upload masks missing required outputs
**What goes wrong:** Pipeline appears healthy because upload step warns instead of failing.
**Why it happens:** `actions/upload-artifact` default `if-no-files-found` is `warn`.
**How to avoid:** Use `if-no-files-found: error` for required proof bundles.
**Warning signs:** Green upload step with warning and absent expected artifacts.

## Code Examples

Verified patterns from repository and official docs:

### Required Step Enforcement from Summary JSON
```powershell
# Source: .github/workflows/ci.yml, release-package.yml, vm-smoke-matrix.yml
$summary = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
if ($summary.status -ne 'passed') {
  throw "Upgrade/rebase validation status is '$($summary.status)': $($summary.message)"
}

$requiredSteps = @(
  'upgrade-rebase-wpf-workflow-contract',
  'upgrade-rebase-wpf-severity-contract',
  'upgrade-rebase-wpf-recovery-contract'
)
```

### Deterministic Quarterly Drift Artifacts
```powershell
# Source: tools/release/Run-QuarterlyRegressionPack.ps1
$summaryPayload = [ordered]@{
  overallPassed = [bool]$overallPassed
  decision = $packDecision
  fixtures = [ordered]@{
    total = $entryArray.Count
    passed = @($entryArray | Where-Object { $_.status -eq "pass" }).Count
    warnings = @($entryArray | Where-Object { $_.status -eq "warning" }).Count
    failed = @($entryArray | Where-Object { $_.status -eq "fail" }).Count
  }
}
```

### Fail-Close Script Baseline
```powershell
# Source: tools/release/Invoke-ReleaseGate.ps1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
...
if (-not $overallPass) {
  exit 1
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Promotion relied on gate command success and partial workflow checks | Explicit JSON summary/report assertions for upgrade/rebase and quarterly signals in CI/release/VM | Phase 10 (2026-02) | Better machine-readable gate enforcement |
| Release-package gate optional via `run_release_gate` input | Still optional today (known gap) | Persisting through 2026-02-16 audit | Primary fail-open risk for QA-02/QA-03 |
| Package build only links evidence status in metadata | Missing required gate artifacts currently produce `partial` status, not hard stop | Current | Must change to fail-closed pre-package blocker |

**Deprecated/outdated:**
- Optional pre-package gate for promotion (`run_release_gate=false`) is incompatible with locked fail-closed policy.
- Warning-only handling for required checks is deferred and explicitly out of scope.

## Open Questions

1. **Canonical location for shared evidence validator**
   - What we know: Reuse is needed to prevent CI/release/VM drift.
   - What's unclear: Whether to host as standalone script (`tools/release/Test-ReleaseEvidenceContract.ps1`) or function module.
   - Recommendation: Start as standalone script with strict typed output to minimize integration friction.

2. **Exact mandatory core evidence list for all flows**
   - What we know: Core must include contract checks, release summary, CI/VM evidence, verification report.
   - What's unclear: Whether stability budget artifacts are required for release-package flow or only CI/VM.
   - Recommendation: Define one core list in Phase 13 plan and explicitly enumerate per-flow extras; fail if any core item missing.

## Sources

### Primary (HIGH confidence)
- Repository source: `tools/release/Invoke-ReleaseGate.ps1` (gate orchestration, summary/report/checksums, fail behavior)
- Repository source: `tools/release/Invoke-PackageBuild.ps1` (release-gate evidence linking and current fail-open gap)
- Repository source: `.github/workflows/ci.yml`, `.github/workflows/release-package.yml`, `.github/workflows/vm-smoke-matrix.yml` (cross-flow enforcement behavior)
- Repository docs: `docs/release/UpgradeAndRebaseValidation.md`, `docs/release/QuarterlyRegressionPack.md`, `docs/testing/StabilityBudget.md`, `docs/release/ShipReadinessChecklist.md`
- Planning audit: `.planning/v1.1-MILESTONE-AUDIT.md` (known gap evidence and blocker framing)

### Secondary (MEDIUM confidence)
- GitHub Docs workflow references: https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax
- GitHub Docs contexts/events references: https://docs.github.com/en/actions/reference/workflows-and-actions/contexts and https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows
- `actions/upload-artifact` README (behavior and `if-no-files-found` semantics): https://github.com/actions/upload-artifact
- .NET CLI docs (`dotnet test`, updated 2026-01-20): https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test
- PowerShell docs (`Set-StrictMode`, `ConvertFrom-Json`, common parameters): https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/set-strictmode?view=powershell-5.1 and related cmdlet docs

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - derived directly from active workflows/scripts plus official docs.
- Architecture: HIGH - based on current repository enforcement paths and explicit audit findings.
- Pitfalls: HIGH - directly evidenced by workflow/script conditions and milestone audit.

**Research date:** 2026-02-16
**Valid until:** 2026-03-18

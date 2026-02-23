# Phase 3: Deterministic Mission Execution Core - Context

**Gathered:** 2026-02-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver the core mission execution loop (build, apply, verify) with deterministic bundle output, strict per-STIG SCAP mapping invariants, and multi-backend apply with reboot-aware convergence. This phase wires existing services (BundleBuilder, ApplyRunner, VerifyOrchestrator, RebootCoordinator) into a complete deterministic pipeline and closes critical gaps in SCAP mapping enforcement and LGPO backend support. Phase 4 (Human Resolution) consumes these execution outputs for manual control resolution.

</domain>

<decisions>
## Implementation Decisions

### Bundle Determinism Contract
- BundleBuilder already outputs Apply/, Verify/, Manual/, Evidence/, Reports/, Manifest/ trees — no structural changes needed
- Add determinism assertion: re-build from identical inputs must produce identical file_hashes.sha256 manifest
- Manifest version field added to manifest.json for forward compatibility (start at schema v1)
- Bundle content validation: preflight check that Apply/ templates are complete before execution starts
- No post-build report index — existing individual report files in Reports/ are sufficient for v1

### Apply Preflight Hardening
- Extend Preflight.ps1 with PowerSTIG module availability check (Import-Module PowerSTIG -ErrorAction SilentlyContinue)
- Add DSC module version check — ensure required DSC resources match what PowerSTIG data references
- Add mutual-exclusion safety: if both DSC and LGPO targets exist for same control, fail with explicit conflict error
- Keep existing preflight checks (admin, OS, disk, PowerShell version, reboot, CLM, execution policy)
- No C# preflight duplication — PowerShell preflight is the single source of truth, C# just invokes and checks exit code

### LGPO/GPO Backend Path
- LGPO runner wraps LGPO.exe (already in tools/) for policy import/export
- GPO backend is secondary to DSC — used only when controls have GPO-specific remediation that DSC doesn't cover
- LGPO apply path: compile .pol files from control data, invoke LGPO.exe /m /u for machine/user policy
- LGPO verify path: export current policy with LGPO.exe /parse, compare against expected state
- ApplyFallbackHandler order: DSC (primary) → LGPO (secondary) → Script (fallback) → Manual
- No adaptive mode switching for v1 — if DSC fails, try next backend once, don't retry N times then switch

### Per-STIG SCAP Mapping Contract (Critical Gap)
- Each STIG bundle gets exactly one SCAP benchmark association — no multi-benchmark fallback
- CanonicalScapSelector already handles benchmark selection — extend it to produce a frozen ScapMappingManifest per build
- ScapMappingManifest: maps each VulnId/RuleId to exactly one SCAP benchmark+rule, with mapping confidence and method
- Mapping methods: "benchmark_overlap" (primary), "strict_tag_match" (secondary), "unmapped" (requires manual)
- No broad cross-STIG fallback — if a control can't map to its STIG's benchmark, it goes to unmapped, not to another STIG's benchmark
- VerifyOrchestrator consumes ScapMappingManifest to associate verification results back to controls per-STIG
- Unmapped controls surface in review_required.csv with reason "no_scap_mapping"

### Convergence Limits and Safety
- Maximum 3 reboots per apply cycle — exceed triggers abort with "max_reboot_exceeded" error
- Post-reboot validation: re-run preflight checks before resuming apply steps
- IdempotencyTracker already prevents re-execution — keep current fingerprint-based approach
- Convergence tracking: ApplyResult includes rebootCount and convergenceStatus (converged/diverged/exceeded)
- No script signing verification for v1 — execution policy bypass is already in preflight (acceptable for offline mission tool)

### Claude's Discretion
- Exact LGPO.exe invocation flags and .pol file compilation format
- ScapMappingManifest JSON schema details
- How to structure the LGPO verify comparison (diff format)
- Whether to add a `bundle validate` CLI command for post-build integrity check
- Test coverage strategy for LGPO backend (mock vs integration)

</decisions>

<specifics>
## Specific Ideas

- The ScapMappingManifest is the cornerstone of MAP-01 — it must be written at build time and consumed at verify time, creating a deterministic closed loop
- LGPO backend should feel like DSC backend to the operator — same CLI flags, same evidence output, same timeline events
- Convergence status should appear in the mission timeline so operators can see reboot cycles at a glance
- Keep the fallback chain simple for v1 — no retry logic, no adaptive switching, just ordered attempt with clear failure reasons
- Existing VerifyOrchestrator merge logic (Manual CKL > Latest timestamp > Fail status) should be preserved and documented in the mapping manifest

</specifics>

<deferred>
## Deferred Ideas

- Adaptive backend switching after N failures — could be Phase 3.1 if needed
- OVAL result parsing for richer SCAP data — future enhancement
- OpenSCAP JSON output format support — future enhancement
- PowerShell script signing enforcement — Phase 6 (Security Hardening)
- Post-reboot system state validation beyond preflight — future enhancement
- Bundle rollback capability — Phase 4 or later
- SCC-specific adapter (vs generic XCCDF) — future enhancement if SCC output diverges from standard XCCDF

</deferred>

---

*Phase: 03-deterministic-mission-execution-core*
*Context gathered: 2026-02-22*

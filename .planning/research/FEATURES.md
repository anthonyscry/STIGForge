# Feature Landscape

**Domain:** Offline-first Windows compliance hardening product
**Researched:** 2026-02-19

## Table Stakes

Features users expect. Missing = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Content ingestion and normalization (STIG/SCAP/GPO/LGPO/ADMX) | Teams already pull from multiple DISA and policy sources; a hardening platform must unify inputs | High | Foundation feature; requires artifact classification, dedupe, metadata/hash manifests, and import diagnostics |
| Canonical control model | Operators need one authoritative control view across build/apply/verify/export | High | `ControlRecord`-style normalization is required before deterministic pipelines work |
| Profile and overlay policy model | Compliance teams need reusable baselines with environment-specific overrides | High | Must support deterministic precedence, policy thresholds, waivers, manual answers, and conflict resolution |
| Deterministic build pipeline | "Build -> Apply -> Verify -> Prove" starts with reproducible bundles | High | Requires stable ordering, deterministic naming/layout, and explicit non-deterministic field isolation |
| Apply orchestration with preflight checks | Production Windows hardening requires guardrails before enforcement | High | Must gate on elevation, compatibility, reboot state, PowerShell readiness, and constrained language mode |
| Verify pipeline with scanner normalization | Compliance evidence depends on scanner outputs being mapped back to controls | High | Must wrap SCAP/SCC and Evaluate-STIG, preserve raw artifacts, and emit parser diagnostics |
| Manual check wizard | Not all controls can be automated; operators expect guided manual flow | Medium | Focus on unresolved controls only, with status/reason capture and repeatable answer files |
| Export pipeline (CKL, POA&M, eMASS package) | Submission-ready outputs are mandatory in this domain | High | Must include manifests, scans, answers, evidence tree, attestations, checksums, and deterministic indices |
| Audit trail and integrity proofing | Auditors expect provenance and tamper evidence | Medium | Needs critical-action logging, hash-chain validation, and package-wide SHA-256 manifests |
| WPF and CLI parity for core workflows | Mixed operator and automation usage is standard | Medium | CLI enables scripted operations; WPF supports guided workflows for human operators |

## Differentiators

Features that set product apart. Not expected, but valued.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Strict per-STIG SCAP association contract | Prevents broad false matches that corrupt findings and undermine trust | High | Strong differentiator: per-selected-STIG mapping, benchmark overlap first, strict fallback tags, review-required on uncertainty |
| Release-age automation gate | Reduces risk from fresh DISA changes by forcing early human review | Medium | Uses grace window and trusted mapping requirements; blocks auto-apply on missing/ambiguous release dates |
| Classification scope filtering with confidence and NA report | Makes classified/unclassified/mixed behavior explicit and auditable | Medium | Auto-NA only above policy threshold; ambiguous scope enters review queue; emits traceable `na_scope_filter_report.csv` |
| Evidence autopilot with control-level recipes | Cuts manual packaging effort while improving consistency | High | Captures command/registry/policy/event/file evidence and auto-indexes with metadata + checksums |
| Diff and rebase engine for quarterly updates | Turns recurring DISA update churn into managed workload | High | Must detect control/text/mapping deltas and carry overlays/answers with confidence scoring and review workload reports |
| Reboot-aware convergence plus rollback snapshots | Improves safety and reliability in real hardening runs | High | Multiple enforcement passes with limits; snapshot/rollback of critical policy state to reduce blast radius |
| v1-lite fleet operations (WinRM) | Enables multi-host execution without full enterprise platform scope | Medium | Status/apply/verify on host lists with concurrency controls and host-separated artifacts |

## Anti-Features

Features to explicitly NOT build.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Direct eMASS API sync/upload in v1 | Out of stated scope; adds external dependency and credential/security complexity that conflicts with offline-first operations | Export deterministic eMASS-ready package artifacts for manual transfer/submission |
| Enterprise GPO platform replacement | Expands into a different product category with major operational burden | Keep GPO/LGPO as enforcement backend within the hardening pipeline only |
| Universal perfect scanner mapping across all vendors | Unrealistic for v1; creates brittle heuristics and high false-confidence risk | Enforce strict per-STIG SCAP mapping rules and explicit review-required paths for uncertainty |
| Internet-dependent critical workflows | Violates mandatory offline-first principle and air-gap usability | Ensure core ingest/build/apply/verify/prove loop runs fully offline |
| Silent auto-apply/auto-match on ambiguous decisions | Conflicts with safety-first and explainability principles | Route ambiguity to review queue with rationale, confidence, and operator action |

## Feature Dependencies

```text
Content ingestion/normalization -> Canonical control model
Canonical control model -> Profile/overlay policy engine
Profile/overlay policy engine -> Classification scope filtering
Profile/overlay policy engine -> Release-age automation gate
Canonical control model + policy engine -> Deterministic build pipeline
Deterministic build pipeline -> Apply orchestration
Deterministic build pipeline -> Verify pipeline
Apply orchestration + Verify pipeline -> Manual check wizard
Manual check wizard -> Evidence autopilot
Build/Apply/Verify/Manual/Evidence outputs -> Export pipeline
All pipelines -> Audit trail + integrity proofing
Canonical model + historical packs/overlays -> Diff/rebase engine
Apply/Verify orchestration -> Fleet operations
WPF/CLI parity depends on stable core contracts across all above features
```

## MVP Recommendation

Prioritize:
1. Content ingestion + canonical control model
2. Profile/overlay policy engine + deterministic build/apply/verify core
3. Manual wizard + evidence autopilot + deterministic export package

Defer: v1-lite fleet operations and deeper quarterly diff/rebase automation polish until single-host deterministic pipeline is stable and contract-tested.

## Sources

- `PROJECT_SPEC.md` (sections: Product Identity, Scope, Functional Requirements, Acceptance Criteria, Constraints)
- `.planning/PROJECT.md` (sections: Scope, Product Principles, Architecture Baseline, Definition of Done)

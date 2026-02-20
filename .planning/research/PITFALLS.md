# Domain Pitfalls

**Domain:** Offline-first Windows compliance hardening and evidence packaging (STIGForge Next)
**Researched:** 2026-02-19

## Critical Pitfalls

Mistakes that cause rewrites or major issues.

### Pitfall 1: Silent Mapping Ambiguity in STIG-to-SCAP Association
**What goes wrong:** The system auto-selects a SCAP benchmark when multiple candidates are plausible, or falls back broadly across unrelated STIG rows.
**Why it happens:** Teams optimize for "high match rate" instead of strict per-STIG mapping invariants and deterministic tie-breakers.
**Consequences:** False verification confidence, wrong control status propagation, and non-defensible audit outcomes.
**Prevention:** Enforce hard contracts: per-selected-STIG mapping only, benchmark overlap as primary signal, strict feature/OS tag overlap fallback, and mandatory review queue when confidence is insufficient.
**Detection:** Rising count of controls marked verified without benchmark overlap, repeated operator overrides on the same mapping classes, and test failures on strict association invariants.

### Pitfall 2: Non-Deterministic Build/Export Artifacts
**What goes wrong:** Identical inputs produce different bundle trees, ordering, status derivations, or checksums across runs.
**Why it happens:** Hidden nondeterminism (unordered collections, locale/timezone effects, filesystem traversal order, timestamp leakage, tool-version drift).
**Consequences:** Broken reproducibility, failed audit comparisons, noisy diffs during quarterly updates, and impossible root-cause analysis.
**Prevention:** Define deterministic output contracts at module boundaries; canonical sort/order rules everywhere; normalize timestamps by policy; pin and record tool versions in manifests; run repeatability tests as release gates.
**Detection:** Same fixture run twice yields checksum/index deltas; package diffs with no input changes; flaky contract tests for bundle structure.

### Pitfall 3: Audit Chain Breaks and Evidence Integrity Gaps
**What goes wrong:** Critical actions are not fully recorded, hash-chain continuity is broken, or artifact manifests do not reconcile with packaged files.
**Why it happens:** Audit logging is bolted on late; event schemas change without migration discipline; evidence collection paths bypass canonical indexers.
**Consequences:** Tamper-evidence claims are not defensible, submission package trust drops, and remediation requires expensive forensic reconstruction.
**Prevention:** Treat audit/integrity as first-class contracts: append-only event model, chain-validation command, mandatory SHA-256 manifests for all export artifacts, and contract tests that fail on missing provenance fields.
**Detection:** Hash-chain validation failures, orphaned evidence artifacts not present in index, and export validation mismatches between index and filesystem.

### Pitfall 4: PowerShell/DSC State Drift During Apply
**What goes wrong:** Desired state converges initially but drifts due to host policy, execution context variance, DSC resource/version differences, or reboot sequencing.
**Why it happens:** Preflight is shallow; LCM and host readiness assumptions are implicit; fallback script paths diverge from DSC/GPO semantics.
**Consequences:** Inconsistent enforcement results by host, repeated apply loops, and verification disagreement between scanners and local state.
**Prevention:** Strong preflight gates (elevation, policy, constrained language, reboot, host readiness), explicit max-pass convergence loops, snapshot/rollback of critical policy state, and backend parity tests for DSC/GPO/script fallback behavior.
**Detection:** High rate of second-pass changes, oscillating settings between runs, per-host variance spikes in fleet summaries, and frequent pending-reboot blockers.

### Pitfall 5: Packaging Reproducibility and Submission Contract Drift
**What goes wrong:** CKL/POA&M/eMASS exports pass local checks but fail downstream validation due to layout/index/checksum inconsistencies.
**Why it happens:** Export is treated as formatting instead of contract fulfillment; schema/version mismatches and optional-field ambiguity accumulate over milestones.
**Consequences:** Submission delays, manual package surgery, and brittle release cycles near compliance deadlines.
**Prevention:** Versioned export schemas, golden-package fixtures, strict index contracts, and compatibility tests for every release against known validator expectations.
**Detection:** Validator failures concentrated in index/reference paths, last-minute manual ZIP edits, and recurring "works locally" export defects.

### Pitfall 6: Verification Parser Brittleness Across Scanner Variants
**What goes wrong:** Parser/normalizer logic breaks when SCAP/SCC/Evaluate-STIG output format or edge-case content changes.
**Why it happens:** Parsers rely on fragile heuristics and under-specified assumptions; raw artifacts are not retained for replay/debug.
**Consequences:** False fail/pass rates, broken traceability to canonical controls, and blocked quarterly adoption until parser hotfixes ship.
**Prevention:** Build parser contract suites with real fixture corpora, preserve raw scanner artifacts plus parser diagnostics, and separate extraction from normalization to isolate breakage.
**Detection:** Sudden spike in "unmapped" or "unknown" result states, parser exception clusters after tool updates, and normalization confidence regressions.

## Moderate Pitfalls

### Pitfall 1: Overlay Precedence Ambiguity
**What goes wrong:** Overlay merges apply in unexpected order, producing policy conflicts and operator confusion.
**Prevention:** Publish a deterministic precedence matrix, enforce conflict-resolution rules in code, and surface "why this value won" diagnostics.

### Pitfall 2: Manual Answer Reuse Without Provenance Guardrails
**What goes wrong:** Reused manual answers are applied to changed controls or mismatched environments.
**Prevention:** Require provenance anchors (control revision, environment tags, confidence), auto-carry only for high-confidence rebases, and queue uncertain carries for review.

### Pitfall 3: Offline Assumption Leaks
**What goes wrong:** Critical workflows silently depend on network resources (package feeds, certificate checks, metadata fetch).
**Prevention:** Offline-mode test suite in CI, explicit dependency inventory, and startup self-check that blocks network-required code paths in production mode.

## Minor Pitfalls

### Pitfall 1: Diagnostic Noise Overload
**What goes wrong:** Reports flood operators with low-priority warnings, hiding high-severity blockers.
**Prevention:** Severity-tiered reporting, actionable next-step text, and default views that prioritize gating failures.

### Pitfall 2: WPF/CLI Behavior Drift
**What goes wrong:** Same workflow produces slightly different defaults or validation behavior between interfaces.
**Prevention:** Shared command contracts in core modules, parity tests for key workflow paths, and one source of truth for policy defaults.

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| M1 Foundation (schemas/import/canonical model/profile) | Canonical model underspecification causes later parser/export rewrites | Freeze versioned schemas early; add contract tests for required fields, enums, provenance, and migration policy |
| M1 Foundation | Mapping ambiguity accepted to speed ingestion | Implement strict per-STIG association invariants and deterministic candidate selection before broad content support |
| M2 Execution Core (build/apply/verify) | Non-deterministic build artifacts from unordered processing | Introduce deterministic ordering + timestamp normalization and repeat-run checksum tests as quality gates |
| M2 Execution Core | PowerShell/DSC drift hidden by shallow preflight | Enforce full readiness gates, convergence loop metrics, and rollback snapshots from day one |
| M3 Human Loop (manual wizard/evidence) | Evidence collection bypasses canonical metadata/index path | Make evidence indexing mandatory API; reject unindexed artifacts during export |
| M4 Export (CKL/POA&M/eMASS) | Package passes internal checks but fails downstream validators | Maintain golden export fixtures, strict index contract tests, and release-time validator replay |
| M5 Lifecycle (diff/rebase quarterly updates) | Over-aggressive auto-carry of overlays/manual answers across revisions | Confidence-scored rebase with mandatory review queue for uncertain matches and clear workload summaries |
| M6 Fleet + hardening | Host-to-host enforcement variance obscures true compliance state | Capture per-host backend/tool versions and convergence telemetry; add variance dashboards and drift alerts |

## Sources

- `PROJECT_SPEC.md` (product principles, strict mapping contract, deterministic output contract, audit/integrity requirements, apply/verify/export pipelines, milestone plan)
- `.planning/PROJECT.md` (scope, architecture baseline, roadmap-level definition of done)

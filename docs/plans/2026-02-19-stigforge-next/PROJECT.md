# STIGForge Next

## What This Is

STIGForge Next is a full reboot of STIGForge as an offline-first Windows compliance platform that turns fragmented hardening workflows into one deterministic pipeline: Build -> Apply -> Verify -> Prove. It is for operators, ISSO/ISSM teams, and maintainers who need repeatable quarterly DISA update workflows with auditable evidence and export packaging. This reboot treats `PROJECT_SPEC.md` as canonical and preserves only proven contracts and product requirements.

## Core Value

Produce deterministic, defensible compliance outcomes with strict control mapping and complete evidence packaging, without requiring internet access.

## Requirements

### Validated

- ✓ Strict per-STIG SCAP mapping must not broad-match one SCAP benchmark across unrelated STIG rows.
- ✓ Deterministic packaging and hash-indexed outputs are mandatory for audit trust.
- ✓ Offline-first operation is mandatory for mission use.

### Active

- [ ] Build a canonical import-to-export pipeline with explicit invariants.
- [ ] Implement strict STIG-to-SCAP association and review-required behavior for ambiguity.
- [ ] Implement deterministic bundle and export contracts with verification gates.
- [ ] Implement manual answer/evidence workflows that replace STIG Viewer thrash.
- [ ] Implement phased roadmap M1-M6 aligned to reboot scope.

### Out of Scope

- Direct eMASS API sync - export package only in v1.
- Full enterprise GPO management replacement - consume/apply only.
- Best-effort broad auto-matching for ambiguous mappings - explicitly disallowed by invariant.

## Context

The existing codebase contains useful implementation ideas but is not treated as authoritative for reboot architecture decisions. The canonical source of truth is `PROJECT_SPEC.md`, which defines mandatory pipeline behavior, strict mapping invariants, deterministic outputs, and phase-oriented delivery expectations. The reboot must explicitly address critical risks: ingestion correctness, strict mapping correctness, deterministic output stability, and offline execution resilience.

## Constraints

- **Offline-first:** All mission-critical operations must function without internet - required for air-gapped use.
- **Deterministic outputs:** Identical inputs must produce equivalent structure/index/hash behavior - required for audit confidence.
- **Safety defaults:** Ambiguity routes to review-required - prevents unsafe silent automation.
- **Windows domain:** Targeted for Win11 and Server2019 operations - constrains tooling/runtime decisions.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use `PROJECT_SPEC.md` as canonical reboot source | Avoid carrying accidental legacy behavior | ✓ Good |
| Keep milestone structure M1-M6 | Matches required delivery sequence and acceptance gates | ✓ Good |
| Enforce strict STIG-to-SCAP invariant as non-negotiable contract | Mapping errors invalidate compliance trust | ✓ Good |
| Prioritize schema/contracts before broad implementation | Stabilizes boundaries and deterministic behavior | ✓ Good |

---
*Last updated: 2026-02-19 after reboot project initialization*

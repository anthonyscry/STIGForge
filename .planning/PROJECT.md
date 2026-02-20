# STIGForge Next

## What This Is

STIGForge Next is a full reboot of STIGForge as an offline-first Windows compliance platform for operators, ISSO/ISSM teams, and maintainers. It replaces fragmented workflows with one deterministic mission loop: Build -> Apply -> Verify -> Prove. The reboot is driven by `PROJECT_SPEC.md` as the canonical source and rebuilds both UX and backend module contracts.

## Core Value

Produce deterministic, defensible compliance outcomes with strict control mapping and complete evidence packaging, without requiring internet access.

## Requirements

### Validated

(None yet - ship to validate)

### Active

- [ ] Rebuild the product from scratch against `PROJECT_SPEC.md` with side-by-side `STIGForge.Next.*` delivery and phased cutover.
- [ ] Enforce strict per-STIG SCAP mapping invariants at design, implementation, and test layers.
- [ ] Guarantee deterministic bundle/export outputs (ordering, indices, checksums) for identical inputs.
- [ ] Deliver full mission loop parity (Build, Apply, Verify, Manual, Evidence, Export) in WPF and CLI.
- [ ] Deliver mission-console UX overhaul from scratch, backed by new contracts and workflows.

### Out of Scope

- Direct eMASS API sync/upload - export package only in v1.
- Enterprise-wide GPO management replacement - consume/apply workflows only.
- Broad best-effort SCAP auto-matching - explicitly disallowed by strict mapping invariant.

## Context

The repository contains mature v1.1 release-candidate artifacts and planning history in `.planning/` and `docs/release/`, but those are legacy context for this reboot. The team decision for this initiative is a full rewrite including backend, with existing runtime paths treated as reference-only rather than authoritative behavior. Delivery is phased as vertical slices, and architecture/contracts are derived from the reboot planning pack under `docs/plans/2026-02-19-stigforge-next/` and `PROJECT_SPEC.md`.

## Constraints

- **Platform**: Windows 11 and Windows Server 2019 targets - required mission environments.
- **Runtime**: .NET 8 + PowerShell 5.1 interoperability - compatibility and support baseline.
- **Offline-first**: No internet dependency for critical workflows - required for air-gapped operations.
- **Determinism**: Identical inputs must produce reproducible outputs - required for audit trust.
- **Safety**: Ambiguity must route to review-required states - no silent risky automation.
- **Delivery**: Phased vertical slices (M1-M6), not big-bang cutover - controlled risk and measurable gates.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Full rewrite including backend | Existing implementation does not satisfy reboot charter as canonical baseline | ✓ Good |
| Existing code is reference-only | Avoid accidental carry-forward of legacy assumptions | ✓ Good |
| Side-by-side replacement strategy | Enables controlled migration and milestone gating | ✓ Good |
| Phased vertical slice delivery | Keeps execution testable and shippable per milestone | ✓ Good |
| Mission-console visual direction for WPF | Aligns UX with operator workflow and status-heavy operations | — Pending |
| `PROJECT_SPEC.md` is canonical | Single source of truth for scope and acceptance invariants | ✓ Good |

---
*Last updated: 2026-02-20 after `/gsd-new-project --auto` reboot initialization*

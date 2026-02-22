# STIGForge Next

## What This Is

STIGForge Next is an offline-first Windows compliance platform for operators, ISSO/ISSM teams, and maintainers. It provides a deterministic mission loop: Build → Apply → Verify → Prove. The v1.0 release delivers full mission parity across CLI and WPF with strict per-STIG SCAP mapping and complete evidence packaging.

## Core Value

Produce deterministic, defensible compliance outcomes with strict control mapping and complete evidence packaging, without requiring internet access.

## Requirements

### Validated

- ✓ Canonical ingestion pipeline (STIG/SCAP/GPO/LGPO/ADMX with dedupe) — v1.0
- ✓ Profile-based scope filtering and safety gates — v1.0
- ✓ Overlay precedence/conflict resolution with deterministic reports — v1.0
- ✓ Deterministic bundle compiler with strict per-STIG SCAP mapping — v1.0
- ✓ Multi-backend apply (PowerSTIG/DSC/GPO/LGPO) with convergence tracking — v1.0
- ✓ Verify normalization with provenance — v1.0
- ✓ Manual wizard with reusable answer files — v1.0
- ✓ Evidence autopilot with checksums — v1.0
- ✓ Pack diff and answer rebase workflows — v1.0
- ✓ CKL/POA&M/eMASS export packages — v1.0
- ✓ WinRM fleet-lite operations — v1.0
- ✓ Hash-chained integrity verification — v1.0

### Active

(Ready for v1.1 planning)

## Current Milestone: v1.1 Operational Maturity

**Goal:** Harden STIGForge Next with production-grade test coverage, observability, performance optimization, and error ergonomics.

**Target features:**
- Test Coverage: 80% line coverage on critical assemblies
- Performance: Mission speed, startup time, scale testing (10K+ rules), memory profile
- Observability: Structured logging, metrics/counters, mission tracing, debug export
- Error Ergonomics: Better messages, recovery flows, error catalog, unified UX

### Out of Scope

- Direct eMASS API sync/upload — export package only in v1
- Enterprise-wide GPO management replacement — consume/apply workflows only
- Broad best-effort SCAP auto-matching — explicitly disallowed by strict mapping invariant

## Context

**Shipped v1.0** with ~43,800 LOC C# across 7 phases and 30 plans.

**Tech stack:** .NET 8, WPF, PowerShell 5.1 interop, SQLite persistence, WinRM fleet operations.

**Known technical debt:**
- Pre-existing flaky test: `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory`
- Service registration pattern inconsistency between CLI and WPF (intentional architectural choice)

## Constraints

- **Platform**: Windows 11 and Windows Server 2019 targets
- **Runtime**: .NET 8 + PowerShell 5.1 interoperability
- **Offline-first**: No internet dependency for critical workflows
- **Determinism**: Identical inputs must produce reproducible outputs
- **Safety**: Ambiguity must route to review-required states
- **Delivery**: Phased vertical slices (M1-M6)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Full rewrite including backend | Existing implementation does not satisfy reboot charter | ✓ Good |
| Strict per-STIG SCAP mapping | Disallow broad fallback, enforce deterministic mapping | ✓ Good |
| Deterministic bundle/export outputs | Required for audit trust | ✓ Good |
| Side-by-side replacement strategy | Enables controlled migration | ✓ Good |
| SQLite append-only mission ledger | Immutable audit trail | ✓ Good |
| OverlayMergeService last-wins precedence | Deterministic conflict resolution | ✓ Good |
| Pack-derived rule selection UX | Eliminate manual ID entry errors | ✓ Good |
| Directory manifest SHA-256 hashing | Stable import identity | ✓ Good |

---
*Last updated: 2026-02-22 after v1.1 milestone started*

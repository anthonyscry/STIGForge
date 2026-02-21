# Architecture Research

**Domain:** Windows compliance workflow tooling (apply -> multi-scan verify -> normalize -> checklist/package export)
**Researched:** 2026-02-20
**Confidence:** MEDIUM-HIGH

## Standard Architecture

### System Overview

Use a pipeline-centric modular monolith with strict contracts between stages. The dominant pattern in current tooling is: policy/content ingestion, deterministic execution planning, pluggable enforcement/scanners, result normalization into one canonical graph, then export.

```text
┌──────────────────────────────────────────────────────────────────────┐
│                  UX + Orchestration Layer                           │
├──────────────────────────────────────────────────────────────────────┤
│  WPF/GUI       CLI/Automation       Run Coordinator                 │
│  (operator)    (batch/CI)           (state machine + retries)       │
└───────────────┬──────────────────────────────────────────────────────┘
                │ typed workflow contracts
┌───────────────▼──────────────────────────────────────────────────────┐
│                   Domain Workflow Layer                             │
├──────────────────────────────────────────────────────────────────────┤
│ Content+Policy  Build Planner  Apply Engine  Verify Hub            │
│ Mapping Engine  Evidence Index  Export Assembler  Reporting         │
└───────────────┬──────────────────────────────────────────────────────┘
                │ adapter interfaces
┌───────────────▼──────────────────────────────────────────────────────┐
│                    Integration Adapter Layer                        │
├──────────────────────────────────────────────────────────────────────┤
│ DSC/PowerSTIG  GPO/LGPO  SCAP/SCC wrappers  Script runner           │
│ CKL/POAM emitters  Hashing/signing  WinRM/Fleet executor            │
└───────────────┬──────────────────────────────────────────────────────┘
                │ persisted artifacts + metadata
┌───────────────▼──────────────────────────────────────────────────────┐
│                      Data/Artifact Layer                            │
├──────────────────────────────────────────────────────────────────────┤
│ Canonical control store  Raw scan blob store  Evidence store        │
│ Audit/event log  Deterministic manifest/index store                 │
└──────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| Content + Policy Ingestion | Import STIG/SCAP/checklist content, classify applicability, track revision/provenance | Parsers + validation + canonical model mapper |
| Build Planner | Freeze profile/overlay/policy inputs and compile deterministic `Apply/Verify/Manual/Evidence/Manifest` bundle | Pure planning service with stable sorting/hashing |
| Apply Engine | Execute hardening actions with preflight checks and reboot-aware convergence | DSC/PowerSTIG first, GPO/LGPO and script adapters as fallback |
| Verify Hub | Run multiple verification tools and collect raw outputs | Scanner adapter registry with uniform run envelope |
| Normalization Engine | Convert heterogeneous scanner/checklist outputs into one canonical result schema | ID mapping + status crosswalk + provenance retention |
| Evidence + Export Assembler | Attach evidence to findings and generate CKL/POA&M/package outputs | Deterministic file tree + index/checksum manifest builder |
| Run Coordinator | Orchestrate phase ordering, retries, cancellation, resumability | State machine with durable checkpoints |
| Audit/Integrity Service | Record who/what/when and prove package integrity | Append-only audit log + SHA-256 manifest generation |

## Recommended Project Structure

```text
src/
├── app/                     # WPF + CLI entry points only
│   ├── wpf/
│   └── cli/
├── workflows/               # Build/Apply/Verify/Export orchestration
│   ├── contracts/
│   └── coordinators/
├── domain/                  # Canonical models and rule engines
│   ├── controls/
│   ├── mapping/
│   └── normalization/
├── adapters/                # External tool integrations
│   ├── apply/
│   ├── verify/
│   └── export/
├── infrastructure/          # Filesystem, db, process, winrm, crypto
├── storage/                 # Artifact and metadata persistence
└── tests/                   # Contract/integration/e2e determinism tests
```

### Structure Rationale

- **`workflows/`:** keeps run sequencing and retries separate from domain logic, so UI and CLI stay behavior-identical.
- **`domain/`:** owns canonical semantics; adapters can transform into domain contracts but cannot redefine them.
- **`adapters/`:** isolates tool churn (SCC/SCAP/PowerSTIG updates) from the mission pipeline.
- **`storage/`:** separates raw artifacts from normalized records to preserve forensic traceability.

## Architectural Patterns

### Pattern 1: Canonical Result Graph

**What:** Every scan/manual/apply outcome is translated into one canonical per-control record linked to source artifacts.
**When to use:** Always, especially when combining 2+ scanners and manual reviews.
**Trade-offs:** Upfront mapping cost; massive downstream simplification for export/reporting.

**Example:**
```typescript
type CanonicalResult = {
  controlId: string;
  status: "pass" | "fail" | "not_applicable" | "not_reviewed" | "error";
  source: { tool: string; runId: string; artifactPath: string };
  observedAt: string;
};
```

### Pattern 2: Adapter-Registry Execution

**What:** Apply and verify backends implement shared interfaces and are selected by policy + host context.
**When to use:** Mixed enforcement and scanner ecosystems (DSC, GPO, SCC, scripts).
**Trade-offs:** Slight interface boilerplate; prevents vendor/tool lock-in.

**Example:**
```typescript
interface VerifierAdapter {
  id: string;
  canRun(ctx: HostContext): boolean;
  run(plan: VerifyPlan): Promise<RawScanArtifact[]>;
}
```

### Pattern 3: Deterministic Export Assembly

**What:** Export is compiled from normalized records + evidence with stable ordering and fixed naming rules.
**When to use:** Any audit-bound package output (CKL/POA&M/eMASS bundle).
**Trade-offs:** Requires strict timestamp/randomness controls; critical for repeatability.

## Data Flow

### Request Flow

```text
[Operator/CI run request]
    ↓
[Run Coordinator]
    ↓
[Build Planner]
    ↓
[Apply Engine] -> [Apply artifacts + state deltas]
    ↓
[Verify Hub (N scanners)] -> [Raw scan artifacts]
    ↓
[Normalization Engine]
    ↓
[Evidence Index]
    ↓
[Export Assembler]
    ↓
[CKL/POA&M/eMASS package + manifests]
```

### State Management

```text
[Run State Store]
    ↓ checkpoint/read
[Coordinator] <-> [Phase Workers] -> [Event/Audit Log]
                               ↓
                        [Artifact Store]
```

### Key Data Flows

1. **Policy to execution flow:** Profile/overlay/policy inputs are resolved into a frozen build manifest before any apply/verify action is allowed.
2. **Raw to canonical flow:** Each scanner output is preserved raw, then normalized into canonical statuses with source-pointer links.
3. **Canonical to export flow:** Export consumes only canonical records + evidence index, never direct scanner-native formats.

## Suggested Build Order (Roadmap Implications)

1. **Canonical contracts first** - define control, run, artifact, and normalized-result schemas before adapters.
2. **Run coordinator + manifest planner** - enforce phase order and deterministic planning early.
3. **Apply path (single backend first)** - deliver DSC/PowerSTIG path plus preflight/reboot contracts.
4. **Verify hub with two adapters** - prove multi-scan collection and adapter registry pattern.
5. **Normalization engine** - implement status crosswalk and provenance linkage; this is the architecture keystone.
6. **Evidence index + export assembler** - generate deterministic checklist/package outputs from canonical state.
7. **Secondary adapters and fleet fan-out** - add GPO/LGPO, extra scanners, and WinRM concurrency after canonical pipeline is stable.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 0-1k hosts/assets | Single-node modular monolith; local/embedded metadata DB acceptable |
| 1k-100k hosts/assets | Split run workers from API/UI; move artifact storage to content-addressed/object store |
| 100k+ hosts/assets | Queue-based distributed orchestration, partitioned normalization workers, separate read models for reporting |

### Scaling Priorities

1. **First bottleneck:** Verify throughput (scanner runtime), solved by parallel worker pools with host-level locks.
2. **Second bottleneck:** Artifact I/O and export packaging, solved by content-addressed storage and streaming exporters.

## Anti-Patterns

### Anti-Pattern 1: Scanner-Centric Data Model

**What people do:** Keep SCC/SCAP/other result formats as primary records and join them ad hoc at report time.
**Why it's wrong:** Normalization moves to every consumer and consistency collapses.
**Do this instead:** Normalize once into canonical result graph; keep scanner outputs as immutable evidence.

### Anti-Pattern 2: Apply/Verify Tight Coupling

**What people do:** Assume verify parser is tied to a specific apply backend.
**Why it's wrong:** Prevents independent adapter evolution and multi-tool verification.
**Do this instead:** Bind both to canonical control IDs and run manifests, not to each other.

### Anti-Pattern 3: Non-Deterministic Packaging

**What people do:** Use filesystem iteration order, wall-clock timestamps, or random IDs in export generation.
**Why it's wrong:** Identical inputs produce non-identical evidence packages, undermining audit trust.
**Do this instead:** Stable sort, deterministic IDs, and policy-controlled timestamp normalization.

## Integration Points

### External Services/Tools

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| DSC / PowerSTIG | Apply adapter | PowerSTIG currently ships frequent STIG content updates (latest release visible Dec 2025) |
| SCAP/SCC style scanners | Verify adapters | Favor wrapper pattern that captures command, version, stdout/stderr, and raw output files |
| STIG checklist ecosystem (CKL/XCCDF) | Import/export adapters | STIG Manager and OpenSCAP workflows show multi-source import/export is a common pattern |
| WinRM/fleet execution | Remote executor adapter | Keep host concurrency and retry policy outside domain logic |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Workflow coordinator <-> domain services | Typed request/response contracts | No direct adapter calls from UI |
| Domain services <-> adapters | Port interfaces | Enables tool substitution without schema changes |
| Normalization <-> export | Canonical result schema only | Export must be scanner-agnostic |

## Sources

- NIST SCAP overview and releases (updated Dec 22, 2025): https://csrc.nist.gov/projects/security-content-automation-protocol
- NIST SCAP 1.4 release page (IPD + tooling details): https://csrc.nist.gov/projects/security-content-automation-protocol/scap-releases/scap-1-4
- NIST ARF specification page: https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/arf
- OpenSCAP User Manual (scan, ARF output, remediation, STIG Viewer compatibility sections): https://static.open-scap.org/openscap-1.3/oscap_user_manual.html
- Microsoft DSC overview (dsc-3.0, updated Jun 9, 2025): https://learn.microsoft.com/en-us/powershell/dsc/overview?view=dsc-3.0
- Microsoft PSDesiredStateConfiguration v1.1 overview (Windows PowerShell 5.1 baseline): https://learn.microsoft.com/en-us/powershell/dsc/overview?view=dsc-1.1
- PowerSTIG repository and releases (latest shown 4.28.0, Dec 2025): https://github.com/microsoft/PowerStig
- STIG Manager documentation and README (API-first, multi-source review integration): https://stig-manager.readthedocs.io/en/latest/ and https://github.com/NUWCDIVNPT/stig-manager
- Project context: `/mnt/c/projects/STIGForge/.planning/PROJECT.md` and `/mnt/c/projects/STIGForge/PROJECT_SPEC.md`

---
*Architecture research for: Windows compliance workflow tooling*
*Researched: 2026-02-20*

# Architecture Patterns

**Domain:** Offline-first Windows compliance hardening platform (WPF + CLI, .NET 8)
**Researched:** 2026-02-19

## Recommended Architecture

Use a modular monolith in .NET 8 with strict module contracts and a shared canonical domain model. Keep orchestration in `STIGForge.App` (WPF) and `STIGForge.Cli`, while all business workflows run through application services exposed by domain modules. This preserves offline determinism, avoids distributed-system complexity in v1, and still allows extraction to services later if fleet scale requires it.

High-level structure:

```text
Presentation (WPF, CLI)
    -> Application Facades per capability (Import, Build, Apply, Verify, Export)
        -> Domain Modules (Content/Core/Build/Apply/Verify/Evidence/Export/Reporting)
            -> Infrastructure Adapters (FS, DB, Process, WinRM, Scheduler)
                -> Local OS resources, scanner binaries, artifact storage
```

### Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| STIGForge.App | Operator UX, workflow orchestration, review queues, diagnostics display | Application facades in Core/Build/Apply/Verify/Export/Reporting |
| STIGForge.Cli | Non-interactive commands, automation/CI entrypoint, scriptable execution | Same facades as WPF; no direct infrastructure calls |
| STIGForge.Content | Import pipelines, parser registry, content classification, dedupe, pack metadata | Core (canonical models), Infrastructure (file/process) |
| STIGForge.Core | Canonical contracts (`ControlRecord`, `Profile`, `Overlay`, policies), rule engine, mapping invariants | All domain modules; Shared for constants |
| STIGForge.Build | Deterministic bundle compiler (`Apply/Verify/Manual/Evidence/Reports/Manifest`) | Core, Content, Infrastructure (filesystem/hashing), Reporting |
| STIGForge.Apply | Preflight checks, backend selection (DSC/GPO/script), convergence loop, rollback metadata | Core policies, Build manifests, Infrastructure (PowerShell/process/reboot state) |
| STIGForge.Verify | SCAP/SCC + Evaluate-STIG wrappers, parser normalization to canonical result model | Core mapping rules, Infrastructure process/files, Reporting |
| STIGForge.Evidence | Evidence recipe execution, artifact capture, metadata/checksum indexing | Core control metadata, Infrastructure file/process adapters |
| STIGForge.Export | CKL/POA&M/eMASS package assembly, deterministic indices/manifests/checksums | Build outputs, Verify results, Evidence index, Reporting |
| STIGForge.Reporting | Human and machine readable diagnostics, diffs/rebase reports, confidence summaries | All modules (read-only aggregation) |
| STIGForge.Infrastructure | Ports/adapters for filesystem, local DB, process runner, task scheduling, WinRM, clock abstraction | Consumed by all modules through interfaces |
| STIGForge.Shared | Cross-cutting constants, primitive contracts, error codes, version markers | Referenced by all modules (no business logic) |

### Data Flow

Primary mission flow (`Build -> Apply -> Verify -> Prove`):

```text
1) Content Import
   Raw DISA/SCAP/GPO/LGPO/ADMX -> Content parsers/classifier
   -> Core canonical records + provenance -> persisted pack index

2) Profile + Overlay Resolution
   Operator/automation selects profile + overlays
   -> Core policy engine resolves precedence/conflicts
   -> frozen execution context (versioned inputs)

3) Deterministic Build
   Build module compiles execution context
   -> bundle tree (Apply, Verify, Manual, Evidence, Reports, Manifest)
   -> deterministic ordering + hash manifest

4) Apply
   Apply module runs preflight gates
   -> executes backend plan with reboot-aware convergence
   -> emits action log + state deltas + rollback artifacts

5) Verify
   Verify wrappers run scanners and parse outputs
   -> normalized `VerificationResult` linked to canonical control IDs
   -> raw artifacts preserved

6) Manual + Evidence
   Unresolved manual controls -> wizard answers
   -> Evidence module executes recipes and indexes artifacts/checksums

7) Export/Prove
   Export module assembles CKL/POA&M/eMASS package
   -> deterministic package index + integrity manifests + attestations
```

Data ownership rules:
- Core owns canonical schemas and policy decisions.
- Other modules may transform but not redefine canonical semantics.
- Reporting reads from contracts/events, never mutates workflow state.

## Patterns to Follow

### Pattern 1: Contract-First Module APIs
**What:** Define DTO/schema contracts in Core/Shared first, then implement module behavior behind interfaces.
**When:** For every cross-module interaction, especially Build, Verify normalization, and Export packaging.
**Example:**
```csharp
public interface IBuildCompiler
{
    Task<BundleManifest> CompileAsync(BuildRequest request, CancellationToken ct);
}

public sealed record BuildRequest(
    string ProfileId,
    string[] PackIds,
    string[] OverlayIds,
    PolicySnapshot Policy);
```

### Pattern 2: Hexagonal Adapters for OS/Tooling Dependencies
**What:** Keep process execution, filesystem IO, WinRM, and clock access behind Infrastructure ports.
**When:** Any operation that touches OS state, scanner binaries, timestamps, or host connectivity.
**Example:**
```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct);
}

public sealed class ScapVerifier : IVerifier
{
    private readonly IProcessRunner _runner;
    public ScapVerifier(IProcessRunner runner) => _runner = runner;
}
```

### Pattern 3: Deterministic Pipeline Snapshots
**What:** Freeze all effective inputs before execution and stamp every output with input/version fingerprints.
**When:** Build, Apply plan generation, Verify normalization, and Export packaging.
**Example:**
```csharp
public sealed record ExecutionSnapshot(
    string SnapshotId,
    PolicySnapshot Policy,
    ToolVersionSet Tools,
    string InputHash);
```

### Pattern 4: Dual Frontend, Single Workflow Engine
**What:** WPF and CLI call the same application facades and use identical command/result contracts.
**When:** All v1 workflows requiring parity.
**Example:**
```csharp
public interface IApplyWorkflow
{
    Task<ApplyRunResult> ExecuteAsync(ApplyRunRequest request, CancellationToken ct);
}
```

## Anti-Patterns to Avoid

### Anti-Pattern 1: UI-Embedded Business Rules
**What:** Putting applicability, mapping, or export logic directly inside WPF view models or CLI command handlers.
**Why bad:** Breaks App/CLI parity and creates inconsistent policy behavior.
**Instead:** Route all decisions through Core/Application facades and return explainable decision artifacts.

### Anti-Pattern 2: Shared "Utility" God Library
**What:** Moving domain logic into `Shared` to bypass module boundaries.
**Why bad:** Hidden coupling, unclear ownership, impossible contract evolution.
**Instead:** Keep `Shared` primitive-only; put business rules in explicit owning module.

### Anti-Pattern 3: Non-Deterministic Build/Export Metadata
**What:** Injecting wall-clock timestamps, unsorted directory iteration, or random IDs into package outputs.
**Why bad:** Violates deterministic output contract and undermines auditability.
**Instead:** Normalize timestamps by policy, stable sort all indices, and derive IDs from deterministic inputs.

### Anti-Pattern 4: Broad SCAP Fallback Across STIGs
**What:** Reusing a single SCAP artifact for unrelated STIG rows when matching is ambiguous.
**Why bad:** Directly violates strict per-STIG SCAP association contract.
**Instead:** Enforce benchmark/tag overlap and send ambiguous cases to review-required state.

## Suggested Build Order

1. **Shared + Core contracts**
   - Define versioned schemas and policy/value objects first (`ControlRecord`, `Profile`, `Overlay`, `BundleManifest`, `VerificationResult`, `EvidenceRecord`, export index contracts).
2. **Infrastructure ports/adapters baseline**
   - File store, local DB, process runner, hashing, clock abstraction, logging/audit sink.
3. **Content module**
   - Parser registry, classifier, provenance capture, dedupe; output canonical records only.
4. **Build module**
   - Deterministic bundle compiler and manifest contracts; contract tests for repeatable output.
5. **Apply + Verify modules**
   - Preflight/enforcement orchestration and scanner normalization with strict mapping invariants.
6. **Evidence module**
   - Recipe execution and artifact indexing linked to canonical control IDs.
7. **Export + Reporting modules**
   - Deterministic CKL/POA&M/eMASS packaging plus diagnostics/diff/confidence outputs.
8. **WPF + CLI shells (parity layer)**
   - Implement UX/command surfaces over finished workflows; enforce identical behavior via shared acceptance tests.

## Scalability Considerations

| Concern | At 100 users | At 10K users | At 1M users |
|---------|--------------|--------------|-------------|
| Execution model | Single-machine local runs, serialized by workspace | Parallel job queue per host/profile, bounded worker pool | Distributed execution coordinators, partitioned job scheduling |
| Artifact storage | Local filesystem + manifest index | Content-addressed store, dedupe by hash | Tiered object storage + retention policies + immutable archives |
| Verification throughput | Single scanner process per run | Concurrent scanner workers with resource caps | Dedicated verification farm with queue isolation |
| Data/query layer | Embedded/local DB acceptable | Centralized relational store for metadata + search indices | Sharded metadata and read replicas for reporting |
| Fleet operations | Basic WinRM fan-out | Host batching with retries/backoff | Multi-region fleet orchestration and fault domains |

## Sources

- `PROJECT_SPEC.md` (internal source of truth, read 2026-02-19)
- `.planning/PROJECT.md` (project constraints and baseline architecture, read 2026-02-19)

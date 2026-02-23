# Phase 1: Mission Orchestration and Apply Evidence - Research

**Researched:** 2026-02-20
**Domain:** .NET mission orchestration (CLI + WPF), deterministic reruns, evidence capture, import staging
**Confidence:** MEDIUM-HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
No `*-CONTEXT.md` file exists for this phase. Use provided objective/requirements as the lock source:
- Implement guided mission execution with reruns.
- Add timeline visibility.
- Capture apply-run evidence.
- Support import staging.
- Address requirement IDs: FLOW-01, FLOW-02, FLOW-03, IMPT-01, APLY-01, APLY-02.

### Claude's Discretion
No explicit discretion section exists. Recommended discretion boundaries for planning:
- Reuse existing STIGForge stack and patterns (Generic Host + System.CommandLine + existing services) instead of introducing orchestration frameworks.
- Prefer deterministic file artifacts and existing SQLite repositories for mission history/state.
- Keep implementation scoped to mission orchestration and apply evidence behavior (no broad redesign of scanner internals).

### Deferred Ideas (OUT OF SCOPE)
No deferred ideas were provided in CONTEXT.md.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| FLOW-01 | Guided mission execution flow | Use existing `BundleOrchestrator` step pattern with explicit phase markers and blocking-failure semantics; add persisted run-state records per step. |
| FLOW-02 | Deterministic reruns | Keep snapshot-style orchestration outputs + stable ordering/fingerprints; support idempotent rerun by run-id and append-only timeline events. |
| FLOW-03 | Timeline visibility | Persist mission event timeline (start/finish/failure/skip/retry) and expose via CLI + WPF summary projections. |
| IMPT-01 | Import staging | Reuse import queue/staging pattern (`ImportInboxScanner` + `ImportQueuePlanner`) and add explicit staged state transitions before commit. |
| APLY-01 | Apply-run evidence capture | Extend apply-run to emit evidence metadata and checksums tied to step outcomes and generated logs. |
| APLY-02 | Apply evidence continuity for reruns | Store evidence by run + control/step key, dedupe by SHA-256, and preserve lineage between reruns (supersedes/retained markers). |
</phase_requirements>

## Summary

This phase should be planned as an extension of existing STIGForge orchestration patterns, not a greenfield workflow engine. The repository already has strong primitives: `BundleOrchestrator` for ordered mission flow, `ApplyRunner` for resumable/reboot-aware apply, deterministic import planning (`ImportSelectionOrchestrator`, `ImportQueuePlanner`), and evidence write/checksum behavior (`EvidenceCollector`). Planning should focus on integrating these into one explicit mission-run model with deterministic rerun semantics and a first-class timeline.

The highest-value architectural move is to introduce a mission run ledger (run ID, phase events, status transitions, artifact references, checksums) that is append-only and queryable by both CLI and WPF. This directly enables FLOW-01/02/03 and avoids ad hoc status logic split across UI strings and log text. Existing `bundle-summary` and `BundleMissionSummaryService` can then read from both consolidated verify outputs and the new run timeline for operator visibility.

For apply evidence, do not treat evidence as only manual-control artifacts. Plan to capture apply step outputs (`apply_run.json`, per-step stdout/stderr logs, snapshot/rollback artifacts, break-glass and audit integrity outcomes) as structured evidence records with SHA-256 and provenance. This aligns with current integrity gates and minimizes rework in later export/proof phases.

**Primary recommendation:** Implement a deterministic `MissionRun` + `MissionTimelineEvent` contract first, then wire orchestrate/apply/import staging and evidence capture to emit into it.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK / runtime | 8.0 (`net8.0`, `net8.0-windows`) | Runtime and language platform | Already project baseline; stable LTS target and current repo convention. |
| Microsoft.Extensions.Hosting | 10.0.2 | DI/lifecycle/logging composition for CLI/App | Existing host factory pattern already used across commands and app startup. |
| System.CommandLine | 2.0.0-beta4.22272.1 | Command graph/options/handlers for orchestration commands | Existing CLI command registration and handler model is built on this. |
| Microsoft.Data.Sqlite | 10.0.2 | Local mission state/event persistence | Already in infrastructure; supports transactional append-only ledger patterns. |
| System.Text.Json | 10.0.2 | Deterministic artifact and summary serialization | Existing manifest/report/evidence metadata format is JSON via STJ. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Dapper | 2.1.66 | Lightweight query/materialization for run/event tables | Use for repository queries over mission timeline and evidence index. |
| Serilog (+ host/file sinks) | 4.3.0 / 10.0.0 / 7.0.0 | Structured runtime diagnostics | Use for operator and support diagnostics, not as canonical mission state. |
| CommunityToolkit.Mvvm | 8.4.0 | WPF command/property binding | Use to project timeline/rerun state into UI without custom MVVM plumbing. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Internal orchestration services | Workflow engine (Elsa/Hangfire/etc.) | Adds external dependency and model mismatch for offline deterministic bundles. |
| SQLite + Dapper append-only tables | Flat JSON-only timeline files | Simpler write path but weaker query/filtering for timeline views and rerun analytics. |
| Existing CLI + WPF surfaces | New orchestration UI layer | Higher scope/risk for this phase; delays requirement delivery. |

**Installation:**
```bash
dotnet restore STIGForge.sln
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── STIGForge.Build/                # Mission orchestrator + run contracts
├── STIGForge.Apply/                # Apply execution, reboot resume, apply evidence emitters
├── STIGForge.Content/Import/       # Import staging, dedupe, planned operations
├── STIGForge.Infrastructure/       # SQLite repositories for run timeline/evidence index
├── STIGForge.Core/Services/        # Summary projections and deterministic orchestration helpers
├── STIGForge.Cli/Commands/         # orchestration/apply/import-stage commands
└── STIGForge.App/                  # timeline/rerun visibility in WPF
```

### Pattern 1: Snapshot-Oriented Deterministic Orchestration
**What:** Build complete plan/snapshot objects from canonical inputs, then atomically publish.
**When to use:** Import staging plans, rerun plans, and timeline projections.
**Example:**
```csharp
// Source: src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs
var plan = orchestrator.BuildPlan(candidates);
// plan has stable ordering, warning lines, counts, and fingerprint
```

### Pattern 2: Ordered Phase Execution with Explicit Markers
**What:** Execute apply->verify in fixed order and emit durable completion markers/artifacts.
**When to use:** Mission flow and rerun boundaries.
**Example:**
```csharp
// Source: src/STIGForge.Build/BundleOrchestrator.cs
WritePhaseMarker(Path.Combine(root, "Apply", "apply.complete"), applyResult.LogPath);
```

### Pattern 3: Integrity-Gated Completion
**What:** Block mission completion when integrity-critical conditions fail.
**When to use:** Apply-run exit criteria and evidence confidence.
**Example:**
```csharp
// Source: src/STIGForge.Apply/ApplyRunner.cs
integrityVerified = await _audit.VerifyIntegrityAsync(ct).ConfigureAwait(false);
if (!integrityVerified)
  blockingFailures.Add("Audit trail integrity verification failed.");
```

### Anti-Patterns to Avoid
- **State from log parsing:** never derive canonical timeline state from free-form console/log text.
- **In-place mutable rerun state:** avoid mutating prior run records; use append-only event records with run IDs.
- **Best-effort evidence only:** do not mark mission complete if apply evidence/integrity artifacts are missing.
- **Relative-path storage ambiguity:** avoid storing unresolved relative paths for evidence artifacts.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CLI parsing/validation | Custom argument parser | `System.CommandLine` | Existing command graph already standardized; built-in help/completion/validation. |
| Run lifecycle/DI wiring | Ad hoc service locator | `Microsoft.Extensions.Hosting` | Consistent startup/lifetime and DI across CLI/WPF composition roots. |
| Hashing/checksum math | Custom hash implementation | `SHA256.Create()` (`System.Security.Cryptography`) | Proven primitives; avoid correctness/security defects. |
| Import dependency closure | UI-only imperative toggles | `ImportSelectionOrchestrator`/`ImportQueuePlanner` patterns | Existing deterministic ordering + warning/fingerprint behavior. |
| Mission summary calculations | One-off counters per view | `BundleMissionSummaryService` style projection | Keeps status interpretation consistent across surfaces. |

**Key insight:** This phase succeeds by composing existing deterministic services into a mission-run contract, not by inventing new foundational mechanisms.

## Common Pitfalls

### Pitfall 1: Rerun Produces Divergent Timeline for Equivalent Inputs
**What goes wrong:** Same bundle/profile/tools produce different event ordering or status across reruns.
**Why it happens:** Event writes depend on async arrival order or mutable shared state.
**How to avoid:** Emit ordered phase events from one orchestrator authority; store sequence index + timestamp + run ID.
**Warning signs:** Flaky timeline tests, non-repeatable fingerprints, UI order changes without input changes.

### Pitfall 2: Partial Run State After Failure/Cancel
**What goes wrong:** UI shows mixed old/new step status and unclear recovery path.
**Why it happens:** Status is updated incrementally in multiple places without atomic projection.
**How to avoid:** Write event log append-only; compute view model from snapshot projection at refresh boundaries.
**Warning signs:** Contradictory guidance strings, stale badges, missing completion markers.

### Pitfall 3: Apply Evidence Not Linked to Run/Step Identity
**What goes wrong:** Evidence files exist but cannot prove which apply run/step created them.
**Why it happens:** Evidence capture lacks run-scoped metadata contract.
**How to avoid:** Include `runId`, `stepName`, `timestamp`, `sha256`, `sourcePath`, and `bundleRoot` in metadata rows/files.
**Warning signs:** Duplicate artifacts with no lineage; weak export readiness checks.

### Pitfall 4: SQLite Lock Contention During Timeline Writes
**What goes wrong:** Intermittent timeout or failed writes under concurrent command activity.
**Why it happens:** Long transactions or shared-cache misuse.
**How to avoid:** Keep transactions small, set explicit timeouts, prefer WAL mode, retry whole unit when deferred write upgrades fail.
**Warning signs:** `database is locked` timeouts, inconsistent event counts.

## Code Examples

Verified patterns from official sources and repository:

### Mission host composition
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
IHost host = builder.Build();
await host.StartAsync();
// resolve command/orchestrator services
await host.StopAsync();
```

### Transactional event append (SQLite)
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions
using var connection = new SqliteConnection(connectionString);
connection.Open();
using var tx = connection.BeginTransaction();

using var cmd = connection.CreateCommand();
cmd.Transaction = tx;
cmd.CommandText = "INSERT INTO mission_timeline(run_id, seq, phase, status) VALUES($r,$s,$p,$st)";
cmd.Parameters.AddWithValue("$r", runId);
cmd.Parameters.AddWithValue("$s", seq);
cmd.Parameters.AddWithValue("$p", phase);
cmd.Parameters.AddWithValue("$st", status);
cmd.ExecuteNonQuery();

tx.Commit();
```

### Deterministic plan fingerprinting
```csharp
// Source: src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs
var payload = JsonSerializer.Serialize(canonical);
using var sha = SHA256.Create();
var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
var fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual/implicit run flow in UI/CLI | Explicit orchestrator-led phase execution (`BundleOrchestrator`) | Existing current codebase | Better sequencing and integration seam for reruns/timeline. |
| Incremental UI mutation for import state | Snapshot plan with deterministic fingerprint | 2026-02 import orchestration design | Stable reruns, easier deterministic tests. |
| Evidence mainly manual artifact focus | Structured evidence metadata + SHA-256 and host/user stamp (`EvidenceCollector`) | Existing current codebase | Enables stronger auditability and export readiness checks. |

**Deprecated/outdated:**
- Inferring canonical mission state from text logs alone: insufficient for deterministic reruns and timeline requirements.

## Open Questions

1. **Requirement text for FLOW-01/02/03, IMPT-01, APLY-01/02 is not present in `.planning/REQUIREMENTS.md`.**
   - What we know: IDs were provided for this phase in user context.
   - What's unclear: Exact acceptance wording per ID.
   - Recommendation: Confirm authoritative requirement text before locking PLAN task acceptance tests.

2. **Timeline storage contract location (bundle file vs SQLite table vs both).**
   - What we know: Existing system already uses bundle artifacts and SQLite-backed infrastructure.
   - What's unclear: Single source of truth expected by later phases.
   - Recommendation: Plan dual-write only if explicitly needed; otherwise pick one canonical store and one projection/export path.

3. **Apply evidence granularity (per step vs per control mapping).**
   - What we know: Current `ApplyRunner` produces step outcomes/logs; `EvidenceCollector` is control-oriented.
   - What's unclear: Whether phase requires control-level mapping immediately.
   - Recommendation: Implement step-level evidence now with extensible metadata schema for control linkage later.

## Sources

### Primary (HIGH confidence)
- Repository code: `src/STIGForge.Build/BundleOrchestrator.cs`, `src/STIGForge.Apply/ApplyRunner.cs`, `src/STIGForge.Evidence/EvidenceCollector.cs`, `src/STIGForge.Core/Services/ImportSelectionOrchestrator.cs`, `src/STIGForge.Content/Import/ImportQueuePlanner.cs`, `src/STIGForge.Core/Services/BundleMissionSummaryService.cs`
- Repository tests: `tests/STIGForge.UnitTests/Apply/ApplyRunnerTests.cs`, `tests/STIGForge.UnitTests/Evidence/EvidenceCollectorTests.cs`, `tests/STIGForge.UnitTests/Services/ImportSelectionOrchestratorTests.cs`, `tests/STIGForge.UnitTests/Content/ImportQueuePlannerTests.cs`
- Microsoft Learn: .NET Generic Host (updated 2026-02-04) — https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- Microsoft Learn: System.CommandLine overview (updated 2025-12-04) — https://learn.microsoft.com/en-us/dotnet/standard/commandline/
- Microsoft Learn: Microsoft.Data.Sqlite connection strings (updated 2025-07-31) — https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings
- Microsoft Learn: Microsoft.Data.Sqlite transactions — https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions

### Secondary (MEDIUM confidence)
- Microsoft Learn: System.Text.Json serialization guidance (updated 2025-11-20) — https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/how-to
- Microsoft Learn: Windows path normalization and fully-qualified path behavior (updated 2025-10-28) — https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
- Docs/design context: `docs/plans/2026-02-15-import-ux-orchestration-design.md`, `docs/plans/2026-02-15-import-auto-classification-workspace-design.md`

### Tertiary (LOW confidence)
- Microsoft Learn: .NET cryptographic services overview (last major update 2022-03-11) — https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-services
- Microsoft Learn: Microsoft.Data.Sqlite async limitations (older 2023 doc, still relevant) — https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - directly verified from project `.csproj` files plus current Microsoft docs.
- Architecture: MEDIUM-HIGH - strong repo evidence, but final shape depends on missing exact requirement text for FLOW/APLY IDs.
- Pitfalls: MEDIUM-HIGH - verified by current code paths/tests and SQLite/hosting docs; some future-scope assumptions remain.

**Research date:** 2026-02-20
**Valid until:** 2026-03-22 (30 days; mostly stable stack)

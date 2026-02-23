# Phase 06: Security and Operational Hardening - Research

**Researched:** 2026-02-08
**Domain:** Security posture hardening, tamper-evident operations, rollback safety rails
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
### High-risk action protection policy
- Keep safe defaults enabled: snapshot-on, release-age gate-on, and integrity checks-on.
- Any destructive or safety-bypass operation must require explicit operator acknowledgment and write an audit entry with reason.
- UI and CLI must enforce equivalent guard semantics; no silent CLI-only bypass for core safety rails.
- Break-glass overrides are allowed for mission continuity, but they must be clearly marked high risk and never silent.

### Failure handling posture
- Fail closed for integrity-critical failures (audit chain invalid, hash/tamper mismatch, required artifact missing, export package invalid).
- Treat optional capability gaps as warnings only when mission integrity is not compromised.
- Final run summaries must separate blocking failures, recoverable warnings, and optional skipped steps.

### Rollback and recovery contract
- Pre-apply snapshot is mandatory in normal operation; skip-snapshot is break-glass only.
- Rollback remains operator-initiated (not automatic), with clear guided recovery steps and explicit rollback artifact pointers.
- Reboot/resume remains first-class; if resume context is invalid or exhausted, stop and require explicit operator decision before continuation.

### Audit and evidence strictness
- Tamper-evident audit chain integrity is required evidence; integrity failure blocks mission completion.
- Export validation (eMASS/CKL/POA&M linkage + hash checks) is blocking for "ready for submission" status.
- Support/diagnostic bundles default to least disclosure: include troubleshooting essentials while excluding secrets and credential material by default.

### Offline security gate behavior
- Security/hardening gates must execute deterministically without runtime internet dependency in default mode.
- If external intelligence is unavailable, use local policy data and mark uncertain findings for review instead of silently passing.
- Provide a strict mode option where unresolved security findings become blocking.

### Claude's Discretion
- Exact UX wording and placement for warnings and confirmations.
- Visual presentation of risk tiers and run-summary diagnostics in the WPF app.
- Internal thresholds for retry/warning aggregation, provided they preserve the policies above.

### Deferred Ideas (OUT OF SCOPE)
- Multi-party approval workflow for destructive actions (future governance-focused phase).
- Central SIEM/event-stream export for audit data (future integration phase).
</user_constraints>

## Summary

Phase 06 should be implemented as **hardening of existing execution paths**, not new mission capability. The codebase already has core pieces (snapshot/rollback, audit hash chain, export integrity validator, support bundle tooling, security gate scripts), but several paths are still permissive/fail-open and do not yet enforce the locked policy contract.

The highest-impact work is concentrated in four areas: (1) strict guardrails for destructive/bypass actions in CLI and WPF parity, (2) mandatory integrity gates at mission completion checkpoints, (3) offline-deterministic security gate behavior in release tooling, and (4) input/process/file hardening for XML parsing, ZIP extraction, process invocation, and support bundle disclosure controls.

**Primary recommendation:** Implement Phase 06 as policy-enforcement overlays on existing services/commands, prioritizing `ApplyRunner`, `BuildCommands`, `MainViewModel.ApplyVerify`, `AuditTrailService` consumers, `EmassPackageValidator`, `ContentPackImporter`, and `tools/release/*Gate.ps1`.

## Standard Stack

### Core
| Library/Component | Version | Purpose | Why Standard Here |
|---|---:|---|---|
| .NET SDK | 8.0.0 (`global.json`) | Build/runtime baseline | Existing repo baseline; deterministic builds already configured |
| System.CommandLine | 2.0.0-beta4.22272.1 | CLI command/option surface | Existing command layer; no new parser needed |
| Microsoft.Data.Sqlite + Dapper | 10.0.2 + 2.1.66 | Audit trail/storage | Existing persistent model for tamper-evident chain |
| Serilog.Sinks.File | 7.0.0 | Operational logs | Existing logging substrate for audit/forensics correlation |
| PowerShell scripts in `tools/release` | N/A | Release/security gates | Already used by CI and release workflows |

### Supporting
| Library/Component | Version | Purpose | When to Use |
|---|---:|---|---|
| `EmassPackageValidator` | in-repo | Hash/tamper + cross-artifact validation | Block "ready for submission" and export completion |
| `SnapshotService` + `RollbackScriptGenerator` | in-repo | Pre-apply rollback rails | Mandatory normal apply flow |
| `AuditTrailService.VerifyIntegrityAsync` | in-repo | Tamper chain verification | Mandatory pre-completion integrity gate |
| `Invoke-SecurityGate.ps1` policies | in-repo JSON policies | Dependency/license/secrets gate | CI and offline gate execution modes |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|---|---|---|
| New policy engine dependency | Existing service/command checks | Faster, lower-risk, aligns with no-scope-creep |
| New audit backend | Existing SQLite chained hash trail | Avoid schema/platform churn in Phase 06 |
| New UI framework behavior layer | Existing WPF ViewModel command gating | Preserves current app architecture |

**Installation/restore baseline:**
```bash
dotnet restore STIGForge.sln --nologo -p:EnableWindowsTargeting=true
```

## Architecture Patterns

### Recommended Project Structure (for this phase)
```
src/
├── STIGForge.Cli/Commands/      # destructive/bypass acknowledgments + reason capture
├── STIGForge.App/               # WPF parity for guard semantics and summaries
├── STIGForge.Apply/             # snapshot mandate, break-glass rails, resume validation
├── STIGForge.Infrastructure/    # audit integrity checks and storage behavior
├── STIGForge.Content/Import/    # zip/xml input hardening
├── STIGForge.Verify/Adapters/   # secure XML reader usage for verify inputs
└── STIGForge.Export/            # blocking package integrity/consistency gate

tools/release/
├── Invoke-SecurityGate.ps1      # offline-deterministic security gate modes
├── Invoke-ReleaseGate.ps1       # gate orchestration
└── security-*.json              # policy sources (vuln/license/secrets)
```

### Pattern 1: Break-glass command contract (CLI + WPF parity)
**What:** Any bypass/destructive action must require explicit acknowledgment and reason; persist audit record.
**When to use:** `--skip-snapshot`, `--force-auto-apply`, future bypass switches.
**Implementation guidance:**
- Add required paired options for bypass paths in CLI (e.g., `--break-glass`, `--reason`).
- Reject bypass when reason missing/short/placeholder.
- Emit explicit high-risk warning in CLI and WPF before execution.
- Record dedicated audit action (`break-glass`) with target + reason.

### Pattern 2: Fail-closed integrity checkpoints
**What:** Block mission completion when integrity evidence fails.
**When to use:** End of apply/orchestrate/export, and readiness status generation.
**Implementation guidance:**
- Call `IAuditTrailService.VerifyIntegrityAsync()` at completion checkpoints.
- Treat invalid chain as blocking failure (non-zero exit / blocking UI status).
- Treat `EmassPackageValidator` errors as blocking, warnings as recoverable.
- Preserve warning-only behavior for non-integrity optional capability gaps.

### Pattern 3: Input canonicalization + bounded process execution
**What:** Normalize paths/inputs, prevent unsafe extraction/parse/command edge cases.
**When to use:** ZIP import, XML parse, external tool/process invocation.
**Implementation guidance:**
- Replace direct `ZipFile.ExtractToDirectory` with entry-by-entry extraction validated against destination root.
- Enforce secure XML parser settings uniformly (`DtdProcessing = Prohibit/Ignore`, `XmlResolver = null`).
- Validate all file/process arguments before execution; disallow unsafe path traversal and enforce existence/whitelist expectations.
- Add process timeout and explicit cancellation handling for long-running external calls.

### Anti-patterns to avoid
- Treating audit write failures as always non-blocking for high-risk actions.
- Allowing CLI-only bypass paths without equivalent WPF semantics.
- Running release/security gate logic that silently depends on live internet in default mode.
- Logging secrets/credentials in support bundle metadata or copied diagnostics.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---|---|---|---|
| Tamper verification | New hash-chain format | Existing `AuditTrailService` chain + `VerifyIntegrityAsync` | Already implemented and tested (`AuditTrailServiceTests`) |
| Export integrity checks | Custom ad-hoc export verifier | Existing `EmassPackageValidator` as blocking gate | Already validates structure, hashes, cross-artifact links |
| Rollback scripts | New rollback scripting subsystem | Existing `SnapshotService` + `RollbackScriptGenerator` | Already integrated in apply path |
| Security policy catalogs | New schema for vuln/license/secrets policy | Existing `tools/release/security-*.json` | Keeps CI/release gate behavior consistent |

**Key insight:** The phase is primarily policy enforcement and fail-closed orchestration over existing components, not replacement of those components.

## Common Pitfalls

### Pitfall 1: Break-glass exists but is silent/non-audited
**What goes wrong:** `--skip-snapshot` and `--force-auto-apply` currently bypass safeguards without mandatory reason + explicit high-risk acknowledgment semantics.
**Where observed:** `src/STIGForge.Cli/Commands/BuildCommands.cs`, `src/STIGForge.Apply/ApplyModels.cs`, `src/STIGForge.App/MainViewModel.ApplyVerify.cs`.
**How to avoid:** Add required reasoned break-glass contract and audit entry for every bypass path.

### Pitfall 2: Integrity-critical paths can remain fail-open
**What goes wrong:** Several callers swallow audit failures (`catch { /* audit failure should not block ... */ }`), violating fail-closed policy for integrity-critical completion.
**Where observed:** `src/STIGForge.Build/BundleOrchestrator.cs`, `src/STIGForge.Content/Import/ContentPackImporter.cs`, `src/STIGForge.Core/Services/ManualAnswerService.cs`, `src/STIGForge.Export/EmassExporter.cs`.
**How to avoid:** Keep non-critical audit writes warning-only, but add explicit blocking integrity check (`audit-verify`) before mission completion status.

### Pitfall 3: XML parsing hardening is inconsistent
**What goes wrong:** Some parsers use hardened `XmlReaderSettings`, others use direct `XDocument.Load(...)` defaults.
**Where observed:** Hardened in `src/STIGForge.Content/Import/XccdfParser.cs`; direct loads in `src/STIGForge.Content/Import/OvalParser.cs`, `src/STIGForge.Verify/Adapters/CklAdapter.cs`, `src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs`, `src/STIGForge.Verify/Adapters/ScapResultAdapter.cs`.
**How to avoid:** Centralize secure XML creation and apply in all XML parse surfaces.

### Pitfall 4: Offline determinism gap in security gate
**What goes wrong:** `Invoke-SecurityGate.ps1` fetches NuGet license metadata over network and uses vulnerability data that may require online feeds.
**Where observed:** `tools/release/Invoke-SecurityGate.ps1` (`Get-NuGetLicenseInfo` -> `https://api.nuget.org/...`).
**How to avoid:** Add offline mode with local cached policy/intel inputs; mark unresolveds for review, and strict mode to block unresolved.

### Pitfall 5: PowerShell 5.1 compatibility risk in release scripts
**What goes wrong:** `Invoke-ReleaseGate.ps1` and `Invoke-SecurityGate.ps1` use `[IO.Path]::GetRelativePath`, which can be runtime-dependent in Windows PowerShell environments.
**Where observed:** `tools/release/Invoke-ReleaseGate.ps1`, `tools/release/Invoke-SecurityGate.ps1`.
**How to avoid:** Replace with URI-based relative path helper (already used in `Invoke-PackageBuild.ps1`) for consistent PS 5.1 behavior.

### Pitfall 6: Support bundle least-disclosure is partial
**What goes wrong:** Extension filtering exists, but metadata captures command line/user/machine and optional DB inclusion can leak sensitive context.
**Where observed:** `src/STIGForge.Cli/Commands/SupportBundleBuilder.cs`.
**How to avoid:** Default-redact sensitive fields, add explicit `--include-sensitive` break-glass switch + reason, and audit capture.

## Code Examples

### Guarded break-glass contract (target insertion points)
```csharp
// BuildCommands.RegisterApplyRun / RegisterBuildBundle
if (skipSnapshot || forceAutoApply)
{
  if (!breakGlass || string.IsNullOrWhiteSpace(reason))
    throw new ArgumentException("Bypass requires --break-glass and --reason.");

  await audit.RecordAsync(new AuditEntry
  {
    Action = "break-glass",
    Target = bundle,
    Result = "acknowledged",
    Detail = $"Bypass=skip-snapshot; Reason={reason}",
    User = Environment.UserName,
    Machine = Environment.MachineName,
    Timestamp = DateTimeOffset.Now
  }, ct);
}
```

### Fail-closed completion gate
```csharp
// End of orchestrate/apply/export completion path
var integrityOk = await audit.VerifyIntegrityAsync(ct);
if (!integrityOk)
  throw new InvalidOperationException("Audit chain integrity invalid. Mission completion blocked.");
```

### Secure XML reader baseline
```csharp
var settings = new XmlReaderSettings
{
  DtdProcessing = DtdProcessing.Ignore,
  XmlResolver = null,
  IgnoreWhitespace = true
};
using var reader = XmlReader.Create(path, settings);
var doc = XDocument.Load(reader, LoadOptions.None);
```

## Concrete Implementation Plan by File

### 1) Input/process/file hardening
- `src/STIGForge.Content/Import/ContentPackImporter.cs`
  - Replace raw `ZipFile.ExtractToDirectory(zipPath, rawRoot)` with validated extraction that rejects entries escaping `rawRoot`.
  - Add max file count / total extracted size guards to prevent extraction abuse.
- `src/STIGForge.Content/Import/OvalParser.cs`
- `src/STIGForge.Verify/Adapters/CklAdapter.cs`
- `src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs`
- `src/STIGForge.Verify/Adapters/ScapResultAdapter.cs`
  - Switch to centralized secure XML reader pattern.
- `src/STIGForge.Verify/EvaluateStigRunner.cs`
- `src/STIGForge.Verify/ScapRunner.cs`
- `src/STIGForge.Apply/ApplyRunner.cs`
- `src/STIGForge.Evidence/EvidenceAutopilot.cs`
  - Add process timeout + cancellation policy and explicit exit-code handling.

### 2) Audit robustness + tamper verification coverage
- `src/STIGForge.Cli/Commands/AuditCommands.cs`
- `src/STIGForge.Infrastructure/Storage/AuditTrailService.cs`
- `src/STIGForge.Build/BundleOrchestrator.cs`
- `src/STIGForge.Apply/ApplyRunner.cs`
- `src/STIGForge.Export/EmassExporter.cs`
- `src/STIGForge.Core/Services/BundleMissionSummaryService.cs`
  - Add mandatory integrity verification checkpoints before completion/readiness status.
  - Expand summary model to classify blocking failures vs warnings vs skipped steps.

### 3) Rollback rails + destructive-action guards
- `src/STIGForge.Cli/Commands/BuildCommands.cs`
- `src/STIGForge.Apply/ApplyModels.cs`
- `src/STIGForge.Apply/ApplyRunner.cs`
- `src/STIGForge.App/MainViewModel.ApplyVerify.cs`
  - Require break-glass acknowledgment + reason for `skip-snapshot` and other bypasses.
  - Keep snapshot mandatory by default; block silent bypass.
  - Ensure invalid resume marker forces explicit operator decision before continuation.

### 4) Offline deterministic security gate behavior
- `tools/release/Invoke-SecurityGate.ps1`
- `tools/release/Invoke-ReleaseGate.ps1`
- `tools/release/security-license-policy.json`
- `tools/release/security-vulnerability-exceptions.json`
- `tools/release/security-secrets-policy.json`
- `.github/workflows/ci.yml`
- `.github/workflows/release-package.yml`
  - Add offline/default mode with local policy/intel-only operation.
  - Add strict mode where unresolved findings are blocking.
  - Remove/replace runtime-dependent path helper for PowerShell 5.1 compatibility.

## Verification Commands

### Unit/integration suites for hardening changes
```bash
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~AuditTrailServiceTests|FullyQualifiedName~SnapshotServiceTests|FullyQualifiedName~RollbackScriptGeneratorTests|FullyQualifiedName~RebootCoordinatorTests|FullyQualifiedName~EmassPackageValidatorTests|FullyQualifiedName~SupportBundleBuilderTests"

dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~AuditTrailIntegrationTests|FullyQualifiedName~SnapshotIntegrationTests"
```

### Security/release gate execution
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot .\.artifacts\security-gate\phase06

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\phase06
```

### Tamper and integrity behavior checks
```bash
# audit chain verify should return 0 valid, 1 invalid
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- audit-verify

# export validation should fail on hash mismatch/missing required artifacts
# (use existing EmassPackageValidator tests and integration fixtures)
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~EmassPackageValidatorTests"
```

### Destructive guard checks (after implementing break-glass contract)
```bash
# expected: non-zero, reason required
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- apply-run --bundle C:\bundle --skip-snapshot

# expected: succeeds only with explicit break-glass + reason and emits audit entry
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- apply-run --bundle C:\bundle --skip-snapshot --break-glass --reason "Emergency rollback baseline unavailable"
```

## State of the Art (in-repo)

| Existing Capability | Current State | Phase 06 Hardening Direction | Impact |
|---|---|---|---|
| Snapshot + rollback script generation | Implemented, default on | Enforce break-glass semantics for bypass | Prevent accidental irreversible apply |
| Audit hash-chain | Implemented + tests | Make integrity check a blocking completion gate | Tamper evidence becomes required mission proof |
| Export package validator | Implemented + tests | Promote from informational to readiness-blocking | Submission integrity confidence |
| Security gate scripts | Implemented in CI | Add offline deterministic and strict unresolved modes | Air-gapped enterprise viability |
| Support bundle | Implemented with extension filtering | Tighten least-disclosure defaults and sensitive toggles | Reduced data leakage risk |

## Open Questions

1. **How should unresolved offline security intelligence be represented in run summaries?**
   - What we know: policy requires uncertain findings be marked for review; strict mode should block.
   - What's unclear: exact summary schema for warnings vs blocking unresolveds across CLI/WPF.
   - Recommendation: add a shared summary contract in Core models and renderers in CLI/WPF.

2. **What minimum reason quality is required for break-glass?**
   - What we know: reason is mandatory for bypass/destructive operations.
   - What's unclear: enforce free-text minimum vs enumerated reason codes.
   - Recommendation: start with minimum length + non-placeholder validation to stay in scope.

## Sources

### Primary (HIGH confidence)
- Repository source code under `/mnt/projects/STIGForge/src`, `/mnt/projects/STIGForge/tools/release`, `/mnt/projects/STIGForge/tests`.
- Phase constraints: `/mnt/projects/STIGForge/.planning/phases/06-security-and-operational-hardening/06-CONTEXT.md`.

### Secondary (MEDIUM confidence)
- Microsoft Docs: `System.IO.Path.GetRelativePath` API page (used for PowerShell runtime compatibility risk assessment):
  - https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getrelativepath?view=net-8.0
- Microsoft Docs: `XmlReaderSettings.DtdProcessing` and `XmlReaderSettings.XmlResolver` behavior:
  - https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-8.0
  - https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.xmlresolver?view=net-8.0

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - directly from csproj/workflow/tooling files.
- Architecture patterns: HIGH - directly from current code paths and command flow.
- Pitfalls: HIGH - supported by concrete file-level observations; MEDIUM where runtime-specific behavior depends on environment (PowerShell 5.1 runtime nuances).

**Research date:** 2026-02-08
**Valid until:** 2026-03-10

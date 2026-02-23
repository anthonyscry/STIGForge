# Phase 3: Deterministic Mission Execution Core - Research

**Researched:** 2026-02-22
**Requirements:** BLD-01, APL-01, APL-02, VER-01, MAP-01

## Existing Architecture

### Bundle Builder (`STIGForge.Build.BundleBuilder`)
- Already creates Apply/, Verify/, Manual/, Evidence/, Reports/, Manifest/ directory trees
- Generates `manifest.json`, `pack_controls.json`, `overlays.json`, `run_log.txt`, `file_hashes.sha256`
- Integrates scope service (`_scope.Compile()`), release gate, and overlay conflict detector
- Writes `na_scope_filter_report.csv`, `review_required.csv`, `overlay_conflict_report.csv`, `automation_gate.json`
- Hash manifest uses `IHashingService.Sha256FileAsync()` — already sorted by path for determinism
- **Gap (BLD-01):** No manifest schema version field. No determinism assertion (re-build same inputs = same hashes). No bundle content validation (Apply/ template completeness check).

### Bundle Orchestrator (`STIGForge.Build.BundleOrchestrator`)
- Wires BundleBuilder, ApplyRunner, VerificationWorkflowService, and ArtifactAggregation
- Creates MissionRun, emits timeline events for Apply/Verify/Evidence phases
- Generates PowerSTIG data files and resolves apply scripts
- Supports break-glass for skip-snapshot with audit trail
- **Gap:** Does not invoke preflight as a C# step — relies on PowerShell script via apply args. No LGPO backend path.

### Apply Runner (`STIGForge.Apply.ApplyRunner`)
- Supports three step types: `powerstig_compile`, `apply_script`, `apply_dsc`
- Reboot-aware via `RebootCoordinator` — resume markers, completed step tracking
- Evidence collection with SHA-256 continuity markers (retained/superseded)
- LCM state capture and reset workflow
- **Gap (APL-01):** Preflight is external PowerShell only — no C# invocation and exit code validation. No PowerSTIG module availability check. No DSC resource version check. No mutual-exclusion safety for DSC/LGPO conflicts.
- **Gap (APL-02):** No LGPO backend. ApplyFallbackHandler exists but is per-control granularity, not wired into the step-level pipeline. No convergence tracking (rebootCount, convergenceStatus) on ApplyResult.

### Apply Fallback Handler (`STIGForge.Apply.ApplyFallbackHandler`)
- Per-control fallback: Primary -> Secondary -> Manual
- Not integrated into step-level ApplyRunner pipeline
- **Gap:** Needs LGPO as a named backend in the fallback chain.

### Preflight Script (`tools/apply/Preflight/Preflight.ps1`)
- Checks: admin rights, OS version, disk space, PowerShell 5.1+, constrained language mode, pending reboot, module path, execution policy
- Returns PSCustomObject with Ok/Issues/Timestamp
- **Gap (APL-01):** Missing PowerSTIG module import check. Missing DSC resource version verification. Missing mutual-exclusion safety check for DSC/LGPO target overlap.

### Reboot Coordinator (`STIGForge.Apply.Reboot.RebootCoordinator`)
- Detects: DSC reboot status, pending file renames, Windows Update reboot flag
- Schedule: writes `.resume_marker.json`, invokes `shutdown.exe`
- Resume: reads marker, validates, deletes after read
- Max reboot check is NOT enforced — **Gap:** No max reboot counter (context says max 3). No post-reboot preflight re-run.

### Verify Orchestrator (`STIGForge.Verify.VerifyOrchestrator`)
- Merges results from SCAP, Evaluate-STIG, and CKL adapters
- Precedence: Manual CKL > Latest timestamp > Fail status
- Groups by ControlId/VulnId/RuleId with deterministic fallback
- **Gap (VER-01):** No provenance link back to raw tool artifact files in NormalizedVerifyResult. Adapter outputs don't include raw artifact paths.

### SCAP Selector (`STIGForge.Core.Services.CanonicalScapSelector`)
- Selects best SCAP candidate per STIG: version alignment -> benchmark overlap -> NIWC enhanced -> date fallback
- Returns single winner with reasons
- **Gap (MAP-01):** Does not produce a frozen ScapMappingManifest at build time. No per-VulnId/RuleId mapping to specific benchmark+rule. No mapping methods (benchmark_overlap, strict_tag_match, unmapped). VerifyOrchestrator doesn't consume a mapping manifest to associate results per-STIG.

### Verification Adapters
- `IVerifyResultAdapter` interface: ToolName, CanHandle(path), ParseResults(path)
- `ScapResultAdapter`: XCCDF 1.2 XML parsing, maps rule-results to NormalizedVerifyResult
- `EvaluateStigAdapter`: Handles Evaluate-STIG output
- `CklAdapter`: Handles CKL XML
- All produce `NormalizedVerifyReport` with Results, Summary, DiagnosticMessages

### Verification Workflow Service
- Runs Evaluate-STIG and SCAP via external processes
- Writes consolidated JSON/CSV and coverage summary
- Uses `VerifyReportWriter.BuildFromCkls()` for report aggregation

## Key Models

### ApplyRequest
- BundleRoot, ModeOverride, ScriptPath, DscMofPath, PowerStigModulePath, RunId, PriorRunId
- **No LGPO fields exist yet**

### ApplyResult
- Mode, Steps, SnapshotId, IsMissionComplete, IntegrityVerified, BlockingFailures
- RunId, PriorRunId for continuity
- **Missing:** rebootCount, convergenceStatus

### BundleManifest
- BundleId, BundleRoot, Run, Pack, Profile, TotalControls, AutoNaCount, ReviewQueueCount
- **Missing:** SchemaVersion, ScapMappingManifest

### NormalizedVerifyResult
- ControlId, VulnId, RuleId, Title, Severity, Status, FindingDetails, Comments
- Tool, SourceFile, VerifiedAt, EvidencePaths, Metadata
- **Missing:** RawArtifactPath, BenchmarkId (for SCAP mapping provenance)

## Dependencies and Test Infrastructure

### Test Projects
- `STIGForge.UnitTests`: xUnit + FluentAssertions pattern
- `STIGForge.IntegrationTests`: E2E pipeline tests exist
- Relevant existing tests: BundleOrchestratorTimelineTests, ApplyRunnerTests, RebootCoordinatorTests, VerifyOrchestratorTests, CanonicalScapSelectorTests

### Project Structure
- `STIGForge.Build` — BundleBuilder, BundleOrchestrator, models
- `STIGForge.Apply` — ApplyRunner, ApplyFallbackHandler, RebootCoordinator, LcmService, PowerSTIG, Snapshot
- `STIGForge.Verify` — VerifyOrchestrator, Adapters, VerificationWorkflowService, ScapRunner, EvaluateStigRunner
- `STIGForge.Core` — CanonicalScapSelector, services, models, abstractions
- `STIGForge.Evidence` — EvidenceCollector, EvidenceAutopilot
- `STIGForge.Cli` — CLI commands
- `STIGForge.App` — WPF application

## Implementation Strategy

### Wave 1 (No cross-dependencies)
1. **BLD-01 (Bundle Determinism)** — Add manifest schema version, determinism assertion, bundle validation. Pure BundleBuilder changes.
2. **APL-01 (Preflight Hardening)** — Extend Preflight.ps1, add C# preflight invocation wrapper, add new checks. Mostly isolated to Apply project + Preflight script.
3. **MAP-01 (SCAP Mapping Manifest)** — Extend CanonicalScapSelector to produce ScapMappingManifest, freeze at build time, write to Manifest/. Core + Build changes.

### Wave 2 (Depends on Wave 1)
4. **APL-02 (Multi-Backend Apply)** — Add LGPO backend, wire into ApplyRunner/ApplyFallbackHandler, add convergence tracking. Depends on preflight (APL-01) being complete for mutual-exclusion safety.
5. **VER-01 (Verify Normalization)** — Extend adapters with provenance, wire ScapMappingManifest consumption into VerifyOrchestrator. Depends on MAP-01 for mapping manifest availability.

## Risk Areas

- LGPO.exe integration requires understanding of .pol file format — may need to defer complex compilation to a dedicated service
- ScapMappingManifest per-VulnId mapping requires control-to-SCAP-rule correlation data that may not be fully available from current XccdfParser output
- Convergence tracking adds complexity to the already complex RebootCoordinator flow
- Determinism assertion test requires careful handling of timestamps and GUIDs in manifest

---

*Phase: 03-deterministic-mission-execution-core*
*Research completed: 2026-02-22*

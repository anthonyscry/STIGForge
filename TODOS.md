# STIGForge — Prioritized TODO Backlog

Generated from CEO-mode codebase health audit (2026-03-15).

---

## Phase 1: Foundation Refactoring (Current)

### P0 — Must Do Now

- [x] **Extract IApplyStep interface** — Create `IApplyStep` with `CanExecute()`/`ExecuteAsync()`/`Name`. Convert 5 existing handlers (PowerStig, Script, DSC, Policy, GPO) to implementations. ApplyRunner iterates collection instead of 240-line if-chain. Add per-step timing via Stopwatch + MissionTimelineEvent.
  - **Why:** 240 lines of duplicated dispatch logic. New step types require modifying ApplyRunner. DRY + testability.
  - **Effort:** L (~4 hours)
  - **Depends on:** Nothing

- [x] **Extract IApplyRunner + IBundleBuilder interfaces** — BundleOrchestrator depends on concrete types. Extract interfaces, register in DI. Enables orchestrator-level mocking.
  - **Why:** Can't unit test orchestrator without real ApplyRunner + BundleBuilder. Testability gate.
  - **Effort:** M (~2 hours)
  - **Depends on:** IApplyStep extraction

- [x] **Convert PowerStigDataGenerator to DI** — Replace static mutable fields (`_releaseAgeGate`, `_scopeService`) with constructor-injected instance. Register in DI container. Thread-safe, testable, no silent failure.
  - **Why:** CRITICAL — static mutable state, thread-unsafe, silently skips filtering when uninitialized.
  - **Effort:** S (~1 hour)
  - **Depends on:** Nothing

- [x] **Inject IProcessRunner into LcmService + FleetService** — Both bypass the centralized ProcessRunner abstraction with direct `Process.Start()`. Fix both.
  - **Why:** HIGH security finding. Un-auditable, un-mockable process execution. Only bypasses in entire codebase.
  - **Effort:** M (~2 hours)
  - **Depends on:** Nothing

- [x] **Fix ScapBundleParser ZIP extraction** — Refactor to delegate to ImportZipHandler instead of duplicating extraction with weaker validation (trusts header size, not actual bytes).
  - **Why:** MEDIUM security — ZIP bomb bypass. DRY — correct implementation already exists.
  - **Effort:** S (~1 hour)
  - **Depends on:** Nothing

- [x] **Fix EvaluateStigRunner argument quoting** — Arguments parameter appended raw to PowerShell command line. Split and quote using `ToPowerShellSingleQuoted()`.
  - **Why:** MEDIUM security — most directly exploitable injection vector in codebase.
  - **Effort:** S (~30 min)
  - **Depends on:** Nothing

- [x] **Fix BundleOrchestrator overlay parse** — Throw `BundleBuildException` when overlay_decisions.json exists but fails to parse. Currently silently returns empty list.
  - **Why:** Zero silent failures. Corrupt overlay = hardening without operator customizations = dangerous.
  - **Effort:** S (~30 min)
  - **Depends on:** Nothing

- [x] **Audit ViewModel CanExecute guards** — During WorkflowViewModel refactoring, verify every RelayCommand has CanExecute tied to IsBusy/step state. Add guards where missing.
  - **Why:** Double-click or concurrent apply = catastrophic for hardening tool. Edge case handling.
  - **Effort:** S (~1 hour)
  - **Depends on:** ViewModel refactoring

- [x] **Add FleetService total timeout + progress callback** — `TimeSpan totalTimeout` parameter and `IProgress<FleetProgress>` callback. Cancel remaining hosts when deadline hit.
  - **Why:** 100 hosts at 600s/host = 3+ hours unbounded. Operator has no visibility or control.
  - **Effort:** M (~2 hours)
  - **Depends on:** Nothing

---

## Phase 2: Platform Primitives

### P1 — High Priority

- [x] **CI/CD Pipeline (GitHub Actions)** — Build → test → coverage gate → publish artifacts. Replace manual `dotnet publish` on Hyper-V host.
  - **Why:** No automated quality gates. Manual builds don't scale. Prerequisite for confident feature iteration.
  - **Effort:** M (~4 hours)

- [x] **Fleet inventory database** — SQLite table for host, role, OS, STIG mapping, last compliance state. Repository interface + DI registration.
  - **Why:** Prerequisite for multi-STIG fleet execution, role-based targeting, compliance dashboards.
  - **Effort:** L

- [x] **NuGet lock files + SBOM** — Enable `RestorePackagesWithLockFile` in Directory.Build.props. Commit lock files. Generate SBOM.
  - **Why:** Prevents silent transitive dependency drift between builds. Supply chain hygiene.
  - **Effort:** S (~30 min)

### P2 — Medium Priority

- [x] **Syslog TLS support** — Add `SslStream` wrapping for TCP mode in SyslogForwarder. Encrypted audit data in transit.
  - **Why:** MEDIUM security finding. Required for classified/regulated environments.
  - **Effort:** M (~3 hours)

- [x] **Audit trail external trust anchor** — Periodically write latest chain hash to Windows Event Log on mission completion. Prevents full-chain recomputation attack.
  - **Why:** MEDIUM security. Current chain is self-verifying — attacker with DB access can recompute.
  - **Effort:** L (~2 hours)

- [x] **DPAPI entropy bytes** — Add application-specific entropy to `ProtectedData.Protect()` calls. Prevents other same-user applications from decrypting fleet credentials.
  - **Why:** LOW security. Defense in depth for credential storage.
  - **Effort:** S (~30 min)

---

## Phase 3: Enterprise Features

### P2 — Vision Items

- [ ] **`stigforge status` command** — One-line fleet health summary. Requires fleet inventory DB.
- [ ] **`stigforge import --dry-run`** — Preview import contents without committing. FormatDetector + parser already extract metadata.
- [ ] **`stigforge diff <pack1> <pack2>`** — Show control changes between STIG versions. Extends ComplianceDiffGenerator pattern.
- [ ] **Exception countdown widget** — Dashboard widget for expiring POA&M exceptions. Backend already supports `GetExpiringAsync()`.
- [ ] **Compliance sparkline in CLI** — ASCII trend `▁▂▃▅▇█ 99.6%` using last 10 snapshots. IComplianceTrendRepository stores history.
- [ ] **MSI installer + code signing** — WiX packaging for Group Policy deployment. SmartScreen/AppLocker compatibility.
- [ ] **Multi-STIG bundle execution** — Bundle polymorphism, fleet-level compliance aggregation, cross-STIG dependency ordering.
- [ ] **Continuous compliance Windows Service** — Persistent scheduling, severity-based gating, auto-remediation with rollback.
- [ ] **Executive dashboards** — Compliance trending, risk heat maps, per-team accountability, SLA tracking.
- [ ] **Forensic drift analysis** — Raw state capture, baseline versioning, drift clustering, evidence linking.

---

## Integration Fixes

### P1 — High Priority

- [ ] **Consolidate Evaluate-STIG output to app's scan folder** — Evaluate-STIG always writes CKL output to `%TEMP%\Evaluate-STIG\<timestamp>\`, ignoring WorkingDirectory. The app looks for results in its configured output folder. After Evaluate-STIG completes, copy/move CKL files from the temp directory to the app's output folder. Without this, every scan shows "No CKL output detected" even when Evaluate-STIG runs successfully.
  - **Why:** Scan workflow is broken end-to-end on real machines. Users see error recovery card even on successful scans.
  - **Effort:** S (~1 hour)
  - **Depends on:** Nothing

- [ ] **Extract shared `StatusNormalizer` to STIGForge.Core** — `NormalizeStatus` / `NormalizeToken` logic is duplicated in 5 places: `CommentTemplateEngine`, `ManualAnswerService`, `DriftDetectionService`, `BundleMissionSummaryService`, `CklMergeService`. Extract to a single static utility.
  - **Why:** DRY — same strip-punctuation-then-switch pattern copied 5 times.
  - **Effort:** S (~30 min)

- [ ] **Extract `JsonElementExtensions` to STIGForge.Core** — `TryGetPropertyCaseInsensitive` + `ReadStringProperty` duplicated in 4 services. The dict-index variant in `DriftDetectionService` can stay for perf, but the old `TryGetPropertyCaseInsensitive` in the same file is now dead code.
  - **Why:** DRY — 4 copies of identical JSON property lookup code.
  - **Effort:** S (~30 min)

- [ ] **Upgrade Scriban to patched version** — Scriban 6.6.0 has 4 known CVEs (GHSA-v66j, GHSA-x6m9, GHSA-xcx6, GHSA-xw6w). NuGet audit blocks builds with `WarningsAsErrors`.
  - **Why:** Security — 3 high + 1 moderate severity vulnerabilities.
  - **Effort:** S (~15 min)

---

## Cleanup

- [x] **Remove dead project directories** — `STIGForge.Shared/` and `STIGForge.Reporting/` were emptied 2026-03-06 but directories remain.
- [ ] **Clean stale git stashes** — 10 stashes from Feb 2026, all pre-merge preserves. Review and drop.
- [ ] **Refactor WorkflowViewModel.Scan.cs** — 1068 lines with 10+ duplicated card factory methods. Extract parameterized `BuildFailureCard()`.
- [ ] **Refactor BuildCommands.cs** — 1007 lines with copy-pasted command registration. Extract shared `CommandBuilder` helper.
- [x] **Extract SafeAuditAsync helper** — Same `try { _audit?.RecordAsync() } catch { Trace.TraceWarning }` pattern repeated in 6+ files.
- [x] **Validate LcmService ConfigurationMode** — Allow-list (`ApplyAndMonitor`, `ApplyAndAutoCorrect`, `ApplyOnly`) instead of raw interpolation.
- [x] **Add path traversal check to PolicyStepHandler.CopyTemplateFile** — `Path.GetFullPath` + `StartsWith` validation consistent with ImportZipHandler.
- [x] **Add path traversal check to BundleIntegrityVerifier manifest** — Validate resolved path starts with bundle root.

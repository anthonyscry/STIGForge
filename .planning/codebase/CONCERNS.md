# Codebase Concerns

**Analysis Date:** 2026-02-21

## Tech Debt

**Large Monolithic ViewModel:**
- Issue: `MainViewModel` is fragmented across 10 partial files with total 6,731 lines. `MainViewModel.Import.cs` alone is 2,638 lines with complex multi-stage import orchestration.
- Files: `src/STIGForge.App/MainViewModel*.cs`
- Impact: Difficult to test, high risk of regression when modifying import/dashboard/export flows. Changes to one concern may have unintended side effects.
- Fix approach: Extract concerns into separate services (ImportOrchestrator, DashboardAggregator, ExportOrchestrator). Inject into ViewModel as dependencies instead of embedding logic.

**Sparse Exception Context:**
- Issue: Many catch blocks only capture `ex.Message` and display to user without logging full exception details or stack traces. Swallowed exceptions like `catch { /* Silently ignore on older Windows versions */ }` in `MainViewModel.cs:92` hide actual failures.
- Files: `src/STIGForge.App/App.xaml.cs`, `src/STIGForge.App/MainViewModel*.cs`, `src/STIGForge.Export/EmassExporter.cs`
- Impact: Difficult to diagnose real failures in production. Users see vague messages ("Scan import folder failed: Object reference not set") without root cause. Audit trail misses critical failure context.
- Fix approach: Log full exception (`_logger.LogError(ex, "Message")`) before handling. Preserve inner exception chain. Only suppress exceptions at presentation layer after logging.

**Hardcoded Paths and Magic Strings:**
- Issue: DllImport paths, resource URIs, and directory layouts are hardcoded (e.g., `"Themes/DarkTheme.xaml"` in `MainViewModel.cs:70`, `"dwmapi.dll"` in `MainViewModel.cs:79`). Windows-only assumptions embedded in core paths.
- Files: `src/STIGForge.App/MainViewModel.cs`, `src/STIGForge.Infrastructure/System/FleetService.cs`
- Impact: Code is tightly coupled to deployment environment assumptions. Cross-platform deployments or alternative configurations require code changes, not config changes.
- Fix approach: Move all paths and identifiers to configuration layer. Use `IPathBuilder` consistently. Create platform abstraction for OS-specific features.

**Audit Trail Integration is Optional and Inconsistent:**
- Issue: `IAuditTrailService? _audit` is nullable in multiple services (`ApplyRunner`, `EmassExporter`, `FleetService`). Some places check for null before audit (`if (_audit != null)`), others don't. Audit failures are silently caught and converted to warnings.
- Files: `src/STIGForge.Apply/ApplyRunner.cs`, `src/STIGForge.Export/EmassExporter.cs`, `src/STIGForge.Infrastructure/System/FleetService.cs`
- Impact: Audit trail is unreliable. Critical operations may not be recorded if service is null or if recording fails. Compliance documentation is incomplete.
- Fix approach: Make audit a required dependency. Create a NullAuditTrailService for testing instead of optional parameter. Add audit checkpoints at service boundaries before operations complete.

---

## Known Bugs

**Import Scan Summary Persistence May Lose Data:**
- Symptoms: If exception occurs during `ScanImportFolderAsync`, the summary is written with partial/zero counts and the list of imported packs is lost.
- Files: `src/STIGForge.App/MainViewModel.Import.cs:205-221`
- Trigger: Exception during import (e.g., network failure, permissions issue) after some packs have been imported but before summary is finalized.
- Workaround: Check recent import summary in logs (`.planning/logs/`) to see what was actually imported before retry.

**Fleet Service Exception Swallowing:**
- Symptoms: If PowerShell remote execution fails, the exception is caught and converted to a `FleetMachineResult` with `Success=false`. Original error details are lost.
- Files: `src/STIGForge.Infrastructure/System/FleetService.cs:115-128`
- Trigger: Network issues, WinRM connectivity loss, or invalid credentials during fleet operations.
- Workaround: Check machine result `Error` field for exception message, but inner cause is not preserved.

**ManualAnswerService Validation Only on Reason Content:**
- Symptoms: Status and reason pairs may be accepted with placeholder reasons ("test", "unknown") if not caught at UI level.
- Files: `src/STIGForge.Core/Services/ManualAnswerService.cs:74-80`
- Trigger: Direct JSON file creation bypassing the wizard UI validation.
- Workaround: Use wizard UI which has additional validation. Do not manually edit `answers.json`.

---

## Security Considerations

**DPI Credential Store Not Fully Isolated:**
- Risk: Credentials stored via DPAPI are tied to machine and user. If user account is compromised or machine is stolen, stored fleet credentials become accessible.
- Files: `src/STIGForge.Infrastructure/Storage/DpapiCredentialStore.cs` (not read, referenced via `App.xaml.cs:100`)
- Current mitigation: DPAPI provides user-scoped encryption by default. Credentials not stored in plaintext.
- Recommendations: Document DPAPI limitations in README. Add credential expiry/rotation guidance. Consider credential revocation on reboot. Add warning when storing fleet credentials.

**PowerShell DSC Log Paths May Contain Sensitive Data:**
- Risk: DSC application logs may contain configuration values, credentials, or system details if scripts log too verbosely.
- Files: `src/STIGForge.Apply/ApplyRunner.cs:50-55`, `src/STIGForge.Build/` (DSC MOF generation)
- Current mitigation: Logs written to `Apply/Logs` subdirectory within bundle (user-controlled location). Redaction not performed.
- Recommendations: Add log sanitization for known sensitive patterns (passwords, API keys). Default to non-verbose logging. Add security guidance in manual bundle review workflow.

**No Input Validation on Import Zip Files:**
- Risk: ZIP extraction uses `ExtractZipSafely` but file path traversal attacks are theoretically possible if archive contains `../` paths.
- Files: `src/STIGForge.Content/Import/ContentPackImporter.cs:101, 136`
- Current mitigation: Temp directory extraction with unique GUID. Archive entry count limited to 4096 and extracted bytes to 512MB.
- Recommendations: Validate all extracted paths stay within extraction root (no `..` components). Add archive signature verification option. Document trusted source requirements for imports.

**Break-Glass Acknowledge Not Cryptographically Signed:**
- Risk: `--break-glass-ack` flag is simple boolean. A user could modify CLI code or arguments to bypass safety gates without proper audit trail.
- Files: `src/STIGForge.Cli/Commands/BuildCommands.cs:20-21, 60-66`
- Current mitigation: Acknowledged break-glass reason must be meaningful (8+ chars). Audit trail records action.
- Recommendations: Require explicit written consent with operator signature/hash. Log break-glass operations with enhanced audit detail. Consider mandatory review workflow before execution.

---

## Performance Bottlenecks

**ContentPackImporter Parallel Semaphore is Small (4):**
- Problem: When importing NIWC-style bundles with multiple benchmarks, parallelism is capped at 4 concurrent imports via `SemaphoreSlim(4)` in `ImportConsolidatedZipAsync`.
- Files: `src/STIGForge.Content/Import/ContentPackImporter.cs:136`
- Cause: Conservative default chosen to avoid overwhelming I/O. No tuning based on system resources or user preference.
- Improvement path: Make concurrency configurable via import request. Add adaptive concurrency based on available disk I/O bandwidth. Profile actual import throughput to establish baseline.

**EmassExporter Copies Files without Streaming:**
- Problem: Large evidence and scan artifacts copied via `File.Copy` (entire file in memory buffer). On systems with many control results (5000+), this can cause heap pressure.
- Files: `src/STIGForge.Export/EmassExporter.cs:160-197`
- Cause: Simple file copy implementation. No streaming or chunking for large files.
- Improvement path: Use `File.Copy` with buffer hints or streaming for files >100MB. Profile export times with large bundles (10,000+ controls).

**Consolidated Results Loaded Into Memory Entirely:**
- Problem: `LoadConsolidatedResults` reads all verification reports into a single list. For large missions (5000+ controls across multiple sources), this consumes significant memory.
- Files: `src/STIGForge.Export/EmassExporter.cs:201-227`
- Cause: No streaming or pagination. All results must fit in memory before processing.
- Improvement path: Implement streaming JSON reader or pagination for very large result sets. Add memory profiling test with 10,000+ control results.

**No Caching of Format Detection Results:**
- Problem: `_formatCache` and `_benchmarkIdCache` in `MainViewModel.Import.cs` are populated but scope is single operation. Between multiple imports in same session, format detection is repeated.
- Files: `src/STIGForge.App/MainViewModel.Import.cs:18-19`
- Cause: Caches are instance variables, not persisted. If user imports multiple similar packs, format detection repeats.
- Improvement path: Consider persistent format cache keyed by file hash. Add cache hit metrics to telemetry.

---

## Fragile Areas

**Apply Step Resume Logic:**
- Files: `src/STIGForge.Apply/ApplyRunner.cs:62-94`
- Why fragile: Step resume depends on exact step name matching (`completedSteps.Contains(PowerStigStepName)`) and `.resume_marker.json` file state. If marker is corrupted or step names change, resume fails catastrophically.
- Safe modification: Add validation of resume context completeness before use. Implement step name versioning. Add integrity check on resume marker (CRC or signature). Test recovery paths thoroughly.
- Test coverage: No integration tests visible for partial apply recovery. Reboot scenarios are not covered in public test suite.

**VerifyOrchestrator Conflict Resolution with Manual Overrides:**
- Files: `src/STIGForge.Verify/VerifyOrchestrator.cs:70-100` (not fully read, but pattern evident)
- Why fragile: Merging reports from multiple sources (SCAP, Evaluate-STIG, manual CKL) uses precedence rules. If rule precedence changes or new adapter is added, conflict resolution may produce incorrect results silently.
- Safe modification: Add comprehensive conflict logging showing which source won and why. Add test cases for each precedence scenario (manual vs SCAP, Evaluate-STIG vs SCAP, etc.). Document precedence rules explicitly.
- Test coverage: Conflict handling not visible in test names reviewed.

**ManualAnswerService Status Normalization:**
- Files: `src/STIGForge.Core/Services/ManualAnswerService.cs:31-50`
- Why fragile: String token matching for status normalization uses multiple alias variations ("pass", "notafinding", "compliant", "closed"). Adding new variants or typos may silently fall through to "Open" instead of failing validation.
- Safe modification: Use enum instead of string status. Validate during deserialization. Add explicit failure for unrecognized status instead of defaulting to "Open". Add logging of normalization decisions.
- Test coverage: Normalization patterns not visible in test suite.

**ContentPackImporter FindIncompleteImports Logic:**
- Files: `src/STIGForge.Content/Import/ContentPackImporter.cs:63-86`
- Why fragile: Incomplete import detection relies on `ImportCheckpoint.Load()` and stage comparison. If checkpoint file is corrupted or stage enum changes, incomplete imports may not be detected and cleanup may be skipped.
- Safe modification: Add checkpoint validation before stage check. Implement checksum on checkpoint file. Add fallback for unrecognizable stage values (assume incomplete). Add warning logging.
- Test coverage: Incomplete import recovery not visible as tested scenario.

**Multiple Interdependent Reboot Markers:**
- Files: `src/STIGForge.Apply/ApplyRunner.cs:167-190, 207-230`, `src/STIGForge.Apply/Reboot/RebootCoordinator.cs`
- Why fragile: Reboot handling creates resume context and schedules reboot separately. If scheduling fails but context is created, or if context is created but scheduling doesn't happen, state is inconsistent.
- Safe modification: Atomically create and persist context together with reboot schedule. Use transactional file operations (write to temp, then move). Verify context and scheduled reboot exist together in recovery path.
- Test coverage: Reboot edge cases (scheduling failure, marker corruption, reboot timeout) not visible in public test suite.

---

## Scaling Limits

**SQLite Database for Content Packs:**
- Current capacity: Handles quarterly STIG import (hundreds of packs, thousands of controls).
- Limit: SQLite is single-writer. Under concurrent CLI operations, writes will serialize. Large control updates (10,000+ controls) may lock database for seconds.
- Scaling path: Profile concurrent write performance. If bottleneck appears, migrate to PostgreSQL or add read-replica SQLite for CLI operations. Implement transaction batching.

**Import Staging Capacity:**
- Current capacity: Supports 4,096 ZIP entries, 512MB extracted content (see `ContentPackImporter.cs:41-42`).
- Limit: Large consolidated bundles or updated content packs may exceed entry count or extraction size.
- Scaling path: Monitor import failures due to size limits. Allow configuration of limits. Consider ZIP streaming instead of full extraction.

**Fleet Concurrency:**
- Current capacity: Default max concurrency is 5 machines (see `FleetService.cs:32`).
- Limit: Large enterprise fleets (100+ machines) require sequential batching.
- Scaling path: Implement machine batching with configurable wave size. Add progress tracking for large fleets.

---

## Dependencies at Risk

**CommunityToolkit.Mvvm 8.4.0:**
- Risk: MVVM toolkit is used for observable properties and relay commands. Future versions may change property change notification semantics.
- Impact: UI binding failures if property notifications break.
- Migration plan: Pin version in `.csproj`. Create tests for observable property change events. Monitor toolkit releases for breaking changes.

**Serilog with File Sink:**
- Risk: Log file rolling can fail if directory permissions are insufficient or disk is full.
- Impact: Application continues but logs are lost, audit trail becomes incomplete.
- Migration plan: Add fallback console logging if file sink fails. Monitor log directory size. Implement log rotation/cleanup.

---

## Missing Critical Features

**No Deterministic Build Verification:**
- Problem: No built-in verification that repeated bundle builds from same inputs produce byte-identical outputs.
- Blocks: Cannot guarantee reproducible hardening missions or audit compliance.
- Gap: Need manifest comparison tool and determinism test suite.

**No Rollback Automation:**
- Problem: Rollback scripts are generated but not automatically applied or tested.
- Blocks: Operators cannot easily undo failed apply operations.
- Gap: Need automated rollback testing and optional automatic rollback on critical errors.

**No Scope Conflict Review Queue:**
- Problem: Ambiguous classification decisions are written to `na_scope_filter_report.csv` but no UI exists to review and override them.
- Blocks: Operators cannot intervene on ambiguous scope decisions before build.
- Gap: Need manual review workflow for scope decisions, similar to manual control review.

**No Multi-Pack Diff or Migration Guidance:**
- Problem: Rebase handles answer/overlay migration, but no tool shows operators which controls are new/changed/removed across multiple packs in a mission.
- Blocks: Operators cannot easily understand compliance impact of content updates.
- Gap: Need diff visualization and migration checklist.

---

## Test Coverage Gaps

**Import Deduplication Logic:**
- What's not tested: Dedup service resolution of candidates with identical hashes but different routes (e.g., same benchmark in both SCAP and STIG ZIP).
- Files: `src/STIGForge.Content/Import/` dedup service
- Risk: Silent data loss or incorrect winner selection if dedup fails.
- Priority: High

**LCM State Capture and Restore:**
- What's not tested: Full LCM lifecycle (capture original state, apply new config, restore old state) especially error paths.
- Files: `src/STIGForge.Apply/Dsc/LcmService.cs`
- Risk: LCM left in incorrect configuration if restore fails, breaking subsequent apply runs.
- Priority: High

**Verification Result Merging with Conflicts:**
- What's not tested: Complex scenarios like same control with different status from SCAP and Evaluate-STIG, with manual override precedence.
- Files: `src/STIGForge.Verify/VerifyOrchestrator.cs`
- Risk: Incorrect results reported if precedence logic is wrong.
- Priority: High

**Export Package Validation Integrity:**
- What's not tested: Validation of checksums, manifest integrity, and CKL structure in generated export packages.
- Files: `src/STIGForge.Export/`, `src/STIGForge.Export/EmassPackageValidator.cs`
- Risk: Invalid packages submitted to eMASS if validator has gaps.
- Priority: High

**Fleet WinRM Communication Under Network Stress:**
- What's not tested: Fleet operations with machine timeouts, partial failures, and recovery behavior.
- Files: `src/STIGForge.Infrastructure/System/FleetService.cs`
- Risk: Incomplete or incorrect fleet results if error handling is insufficient.
- Priority: Medium

**Snapshot and Rollback Script Generation:**
- What's not tested: Actual rollback execution from generated scripts on Windows systems with various configurations.
- Files: `src/STIGForge.Apply/RollbackScriptGenerator.cs`, `src/STIGForge.Apply/Snapshot/SnapshotService.cs`
- Risk: Rollback fails when operator needs recovery.
- Priority: Medium

---

*Concerns audit: 2026-02-21*

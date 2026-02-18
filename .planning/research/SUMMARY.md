# Project Research Summary

**Project:** STIGForge v1.2 — Verify Accuracy, Export Expansion, Workflow Polish
**Domain:** Windows STIG compliance tooling (offline-first, operator-in-field, DoD environment)
**Researched:** 2026-02-18
**Confidence:** HIGH (stack and architecture from direct codebase inspection; SCC internals MEDIUM)

## Executive Summary

STIGForge is an offline-first Windows STIG compliance tool targeting ISSOs, ISSMs, and sysadmins operating in air-gapped DoD environments. The v1.2 milestone addresses three compounding gaps identified at the close of v1.1: (1) the SCC/SCAP verify workflow returns 0 results due to a hardcoded 30-second process timeout and a file-scanning mismatch — `VerifyReportWriter.BuildFromCkls()` looks for `.ckl` files, but SCC writes XCCDF XML to timestamped session subdirectories; (2) the export layer lacks XCCDF and Excel output formats that downstream tools (Tenable/ACAS, eMASS, STIG Viewer) require; and (3) the verify UX provides no meaningful progress feedback, leaving operators unable to determine whether a scan is running, stalled, or complete. All three gaps reinforce each other: broken verify produces empty exports, empty exports surface in a UI that provides no diagnostic context.

The recommended approach builds in strict dependency order. The SCC verify fix is a hard prerequisite — every export format reads `consolidated-results.json`, and if that file contains zero results, all downstream export formats produce misleading compliant-looking artifacts. Before implementing any export adapter, the workflow must be corrected to scan for both `.xml` (SCAP/XCCDF) and `.ckl` files using the existing `VerifyOrchestrator` adapter chain, the 30-second timeout must be made configurable (default: 1800 seconds), and the `ControlResult`/`NormalizedVerifyResult` model duality must be resolved. Once verify is accurate, the pluggable `IExportAdapter` interface should be defined before any format-specific adapters are built — the codebase already has a proven template in `IVerifyResultAdapter`.

The primary risk is building export features against a broken verify pipeline. Export adapters that work correctly against an empty result set will appear green in CI while producing files that falsely imply 100% compliance. The secondary risk is the existing architectural disconnect: `VerifyOrchestrator` (the correct, adapter-based parse path) is not called by `VerificationWorkflowService` (the actual workflow), which instead calls the legacy `CklParser`/`BuildFromCkls` path. The v1.2 architecture work must close this disconnect — not work around it — or the two paths will continue to diverge. Only one new NuGet package is required for the entire milestone: ClosedXML 0.105.0 (MIT license) for Excel export, which satisfies both net48 and net8.0 targets already used by the project.

## Key Findings

### Recommended Stack

The existing stack (.NET 8/net48 dual-target, WPF + CommunityToolkit.Mvvm 8.4.0, Serilog 4.3.0, Dapper + SQLite, System.Text.Json 10.0.2, System.Xml.Linq BCL) requires exactly one new dependency for v1.2. All other features are achievable with what is already installed.

**Core technologies:**
- **.NET 8/net48 dual-target:** Already in place across all projects; ClosedXML's netstandard2.0 target satisfies both TFMs automatically without conditional `ItemGroup` blocks
- **ClosedXML 0.105.0 (MIT):** Excel (.xlsx) export — MIT license is the only viable choice for offline DoD deployment; EPPlus v5+ requires a commercial license key that cannot be provisioned in air-gapped environments; DocumentFormat.OpenXml raw is too verbose for report generation
- **System.Xml.Linq (BCL — already present):** XCCDF 1.2 export — no new package needed; `XccdfExportAdapter` mirrors the `ScapResultAdapter` parse logic in reverse using `XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"`
- **CommunityToolkit.Mvvm 8.4.0 (already present):** WPF UX polish — `[ObservableProperty] bool _isBusy` pattern required because `IAsyncRelayCommand.IsRunning` does not trigger `INotifyPropertyChanged` reliably in WPF (documented issue #4266)
- **IExportAdapter interface (hand-rolled, no package):** Mirrors `IVerifyResultAdapter`; DI-registered; returning `ExportAdapterResult` (not `void`) is mandatory for testability and fail-closed behavior

See `.planning/research/STACK.md` for full version compatibility matrix and alternatives considered.

### Expected Features

**Must have (table stakes) — v1.2 scope:**
- **SCC verify returns real findings** — the workflow is broken without this; all other v1.2 features depend on it; requires: fix process timeout (30s → configurable 1800s), fix output directory scanning (add XCCDF XML path alongside CKL scan), wire `VerifyOrchestrator` into `VerificationWorkflowService`
- **XCCDF result file export** — operators need XCCDF XML to feed Tenable/ACAS and eMASS; `ScapResultAdapter` already parses XCCDF, so `XccdfExportAdapter` is the inverse
- **CSV compliance report (management-facing)** — richer than the existing internal CSV; includes system name, STIG title, CAT level, status, finding detail, remediation priority
- **Pluggable `IExportAdapter` interface** — defines the contract before any format adapter is built; unlocks the WPF format picker and CLI export commands
- **Error messages with recovery guidance** — pattern: `[ERROR-VERIFY-001] SCC found 0 results. Check: output directory matches --output-root, scan profile selected, SCAP content imported.`
- **Verify status progress display** — `VerifyStatus` model with `ToolName`, `State` (Pending/Running/Complete/Failed), `ExitCode`, `FindingCount`, `ElapsedSeconds` bound to WPF verify view

**Should have (differentiators) — v1.2 stretch:**
- **Excel (.xlsx) compliance report** — management/auditor audience expects Excel, not CSV; multi-tab layout (Summary, All Controls, Open Findings, Coverage); ClosedXML; implement after CSV report validates the data model
- **SCC output directory auto-discovery** — enumerate `<output-root>/Sessions/` for newest directory, scan for `*XCCDF*.xml`; eliminates the most common operator error; implement after core SCC fix ships

**Defer (v2+):**
- Mission status dashboard (four-step Build/Apply/Verify/Export pipeline view) — requires lifecycle state model across all workflow stages
- Inline retry on verify failure with preserved state — requires verify state machine changes
- ARF (Asset Reporting Format) export — not achievable without SCC OVAL outputs; document raw SCC XML as the ARF artifact

See `.planning/research/FEATURES.md` for full feature dependency graph and prioritization matrix.

### Architecture Approach

The existing architecture has a critical disconnect: `VerifyOrchestrator` (the correct, adapter-based result aggregation path) is not called by `VerificationWorkflowService` (which instead uses the legacy `BuildFromCkls`/`CklParser` path). Fixing this disconnect is the architectural foundation for all v1.2 work. The `NormalizedVerifyResult`/`ControlResult` model duality must be resolved at the same time — `ControlResult` should become an internal serialization DTO while `NormalizedVerifyResult` (with typed `VerifyStatus`) becomes the canonical pipeline model. No new model should be introduced for export.

**Major components:**
1. **`VerificationWorkflowService` (MODIFY)** — wire `ScapResultAdapter` for XCCDF XML output after SCC runs; call `VerifyOrchestrator.ParseAndMergeResults()` instead of `BuildFromCkls`; make timeout configurable
2. **`VerifyReportWriter.BuildFromOutputDirectory()` (NEW overload)** — try adapters in priority order (`ScapResultAdapter` for `.xml`, `CklAdapter` for `.ckl`); replaces the hardcoded `.ckl`-only scan
3. **`IExportAdapter` / `ExportOrchestrator` / `ExportAdapterRegistry` (NEW in STIGForge.Export)** — pluggable export contract; adapters return `ExportAdapterResult` (paths + warnings + error); registry resolves by format name; orchestrator dispatches
4. **`XccdfExportAdapter` (NEW)** — writes XCCDF 1.2 TestResult XML; uses `XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"` on every element (not just root); round-trip validated via `ScapResultAdapter`
5. **`ExcelExportAdapter` + `ReportGenerator` (PROMOTE from stub)** — ClosedXML multi-sheet workbook; `STIGForge.Reporting.ReportGenerator` is currently a one-line stub returning `Task.CompletedTask`
6. **`MainViewModel.Export.cs` (MODIFY)** — replace individual export buttons with a ComboBox bound to `ExportAdapterRegistry`; single Export command dispatching through `ExportOrchestrator`

See `.planning/research/ARCHITECTURE.md` for full component boundary diagram, data flow per feature, and anti-patterns to avoid.

### Critical Pitfalls

1. **SCC output directory mismatch causing 0 results** — `ScapRunner` does not inject or validate the `-od <outputRoot>` argument; SCC writes to its default directory, STIGForge scans the wrong path, exit code is 0, result count is 0. Prevention: inject the output directory argument, emit a specific diagnostic when `ConsolidatedResultCount == 0` with exit code 0, never treat 0-count as "clean scan."

2. **30-second process timeout kills real SCC scans** — real scans take 5–20 minutes; `WaitForExit(30000)` silently kills the process; STIGForge reports 0 results with no operator-visible error. Prevention: make timeout configurable (default 1800s in `VerificationWorkflowRequest`); surface timeout fires as explicit error `VerificationToolRunResult`, not as a normal exit.

3. **Model duality deferred to export phase** — if `ControlResult` / `NormalizedVerifyResult` duplication is not resolved before export adapters are built, every adapter must choose a model and a conversion bridge; status comparisons use strings instead of typed `VerifyStatus`; the duplication multiplies. Prevention: unify models in the SCC correctness phase, not the export phase.

4. **XCCDF namespace mismatch breaks downstream tool import** — LINQ to XML requires the namespace on every `new XElement(...)` call; elements created without `XccdfNs + "rule-result"` produce `<result>` instead of `<xccdf:result>`, which STIG Viewer, ACAS, and OpenRMF silently reject. Prevention: define `static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"` in the exporter; validate with a round-trip test: export → `ScapResultAdapter.CanHandle()` → parse → assert count matches.

5. **CklParser uses unsecured `XDocument.Load()`** — the legacy `CklParser` (called by `BuildFromCkls`) does not use the `LoadSecureXml()` hardening established in v1.1; the adapter-based `CklAdapter` does. Reusing `CklParser` in export test code re-opens the XXE attack surface. Prevention: migrate `CklParser.ParseFile()` to `LoadSecureXml()` during the verify phase model unification; add a unit test asserting DTD rejection.

See `.planning/research/PITFALLS.md` for full pitfall list including CSV malformed multi-line fields, export adapter interface design, performance traps, and security checklist.

## Implications for Roadmap

Based on research, all four research files converge on the same dependency-driven build order. The phase structure below is unambiguous — deviating from it produces artifacts that falsely indicate correctness.

### Phase 1: SCC Verify Correctness + Model Unification

**Rationale:** Every downstream feature (export, UX, CLI) reads `consolidated-results.json`. If this file contains 0 results because of the SCC timeout/path bug, every export format produces empty-but-valid files. This is not a quality issue — it is a correctness prerequisite. Model unification must happen here (not deferred) because the export adapter interface design in Phase 2 depends on a single canonical model.

**Delivers:** Verify workflow that correctly produces findings when SCC and Evaluate-STIG run against a real system; `consolidated-results.json` with non-zero result count for SCC-scanned systems; configurable process timeout; specific diagnostics for 0-result conditions; `NormalizedVerifyResult` as the single canonical pipeline model; `CklParser` hardened to `LoadSecureXml()`.

**Addresses (from FEATURES.md):** SCC verify correctness (P1 table stakes); error messages with recovery guidance (P1 table stakes — partial).

**Avoids (from PITFALLS.md):** Pitfall 1 (SCC output directory mismatch), Pitfall 4 (model duplication deferred), Pitfall 5 (30-second timeout), Pitfall 2 (CklParser XXE surface).

**Modified files (from ARCHITECTURE.md):** `VerificationWorkflowService.cs`, `VerifyReportWriter.cs`, `ScapRunner.cs`, `EvaluateStigRunner.cs`, `VerificationWorkflowModels.cs`, `CklParser.cs`.

**Research flag:** MEDIUM — SCC CLI argument form (`-od` vs. `--output` vs. `-u`) varies by SCC version and requires live system validation or SCC manual confirmation before the argument injection code is finalized.

---

### Phase 2: Pluggable Export Adapter Interface

**Rationale:** The `IExportAdapter` interface is a blocker for all format-specific adapters and for the WPF format picker. Defining it before implementing XCCDF, CSV, or Excel prevents the common mistake of wiring each format independently and then having to refactor when the fourth format is added. The interface design — particularly returning `ExportAdapterResult` instead of `void` — must be locked before any adapter code is written.

**Delivers:** `IExportAdapter` interface with `ExportAdapterRequest` / `ExportAdapterResult` models; `ExportAdapterRegistry`; `ExportOrchestrator`; `EmassExporter` and `CklExporter` refactored to implement `IExportAdapter` (backward-compatible — existing call sites preserved via wrapper methods).

**Addresses (from FEATURES.md):** Pluggable IExportAdapter interface (P1 table stakes).

**Avoids (from PITFALLS.md):** Pitfall 6 (leaky interface with void return / path-only signature); Anti-Pattern 5 (WPF export tab accumulation); Anti-Pattern 3 (growing VerifyReportWriter with static format methods).

**Modified files (from ARCHITECTURE.md):** `STIGForge.Export/ExportModels.cs` (new models), new `IExportAdapter.cs`, new `ExportAdapterRegistry.cs`, new `ExportOrchestrator.cs`, `EmassExporter.cs`, `CklExporter.cs`.

**Research flag:** LOW (skip research-phase) — pattern is a direct mirror of `IVerifyResultAdapter`, which is already proven in the codebase.

---

### Phase 3: XCCDF Result Export

**Rationale:** XCCDF export is the highest-value new format (Tenable/ACAS, eMASS, STIG Manager interop) and the least risky to implement once the adapter interface and verify pipeline are correct. The parsing logic already exists in `ScapResultAdapter`; the export is the inverse operation using the same `XNamespace` constants.

**Delivers:** `XccdfExportAdapter` implementing `IExportAdapter`; XCCDF 1.2 TestResult XML writer (XDocument-based, namespace-correct on every element); CLI `export-xccdf` command; round-trip test: export → `ScapResultAdapter.CanHandle()` → parse → count matches.

**Uses (from STACK.md):** System.Xml.Linq (BCL — already present; no new package).

**Addresses (from FEATURES.md):** XCCDF result file export (P1 table stakes).

**Avoids (from PITFALLS.md):** Pitfall 3 (XCCDF namespace mismatch); security mistake: includes absolute path stripping from `SourceFile` fields in export output; fail-closed: partial output file deleted on adapter throw.

**Research flag:** LOW (skip research-phase) — XCCDF 1.2 schema is published by NIST; `ScapResultAdapter` provides the field mapping; implementation is well-understood.

---

### Phase 4: CSV Compliance Report (Management-Facing)

**Rationale:** CSV export already exists internally (`VerifyReportWriter.WriteCsv`), but it is minimal (8 columns, no auditor-facing metadata). Promoting it to a full `CsvExportAdapter` with management-facing columns and a property-based escape test is low-risk and unlocks the WPF format picker for non-Excel environments.

**Delivers:** `CsvExportAdapter` implementing `IExportAdapter`; management-facing columns (system name, STIG title, CAT level, status, finding detail, remediation priority, due date); dedicated `CsvWriter` class that applies escaping automatically to all values; property-based test with random strings including `\n`, `"`, `,`; CLI `export-csv` command.

**Addresses (from FEATURES.md):** CSV compliance report for management/auditors (P1 table stakes).

**Avoids (from PITFALLS.md):** Pitfall 7 (CSV malformed multi-line fields); warning sign: any `sb.Append(r.SomeField)` without `Csv()` wrapper.

**Research flag:** LOW (skip research-phase) — well-understood CSV escaping; `VerifyReportWriter.WriteCsv` provides the starting point.

---

### Phase 5: Excel (.xlsx) Compliance Report

**Rationale:** Excel is the management/auditor preference over CSV; the multi-tab layout (Summary, All Controls, Open Findings, Coverage) saves hours of manual formatting. This phase requires the only new NuGet package in the milestone: ClosedXML 0.105.0. It is placed after CSV because it shares the same data model — the CSV adapter validates that model before the more complex Excel workbook generation begins. `STIGForge.Reporting.ReportGenerator` is currently a one-line stub; this phase promotes it to full implementation.

**Delivers:** `ExcelExportAdapter` implementing `IExportAdapter`; `ReportGenerator` implementation using ClosedXML (4-sheet workbook: Summary, All Controls, Open Findings, Coverage); CLI `export-excel` command; round-trip tests that open the workbook programmatically and assert column values (not visual inspection).

**Uses (from STACK.md):** ClosedXML 0.105.0 (MIT, netstandard2.0 — compatible with net48 and net8.0); add to `STIGForge.Export.csproj` or `STIGForge.Reporting.csproj`.

**Addresses (from FEATURES.md):** Excel (.xlsx) compliance report (P2 differentiator / v1.2 stretch).

**Avoids (from PITFALLS.md):** Pitfall 8 (EPPlus license risk — use ClosedXML, document license choice in adapter source header); performance trap: stream rows during write, do not accumulate full result list twice in memory.

**Research flag:** LOW (skip research-phase) — ClosedXML MIT license confirmed on NuGet; API is well-documented; scale is well within ClosedXML's envelope for STIG report sizes.

---

### Phase 6: WPF Workflow UX Polish + Export Format Picker

**Rationale:** UX polish depends on verify correctness (Phase 1) and the export adapter registry (Phase 2) being complete — the format picker iterates registered adapters, and the progress display is meaningless if verify returns 0 results. Consolidating this into one phase avoids incremental half-finished UI states.

**Delivers:** `VerifyStatus` model bound to WPF verify view progress area (ToolName, State, ExitCode, FindingCount, ElapsedSeconds); ComboBox in Export view bound to `ExportAdapterRegistry`; single Export command dispatching through `ExportOrchestrator`; format-specific description `TextBlock` below picker; `[ObservableProperty] bool _isBusy` pattern (not `IAsyncRelayCommand.IsRunning` — known WPF binding issue #4266); error messages with recovery guidance per error code surfaced in verify and export views; export button disabled while export is running.

**Addresses (from FEATURES.md):** Verify status progress display (P2); export format picker with help text (P2); error messages with recovery guidance (P1 — completes what Phase 1 started in CLI/diagnostics); SCC output directory auto-discovery (P2 stretch — can land here or after Phase 1).

**Avoids (from PITFALLS.md):** UX pitfall: "Verify complete: 0 results" with no diagnostic; UX pitfall: export button with no progress indicator; UX pitfall: one file dialog per format; Anti-Pattern 5 (WPF export tab accumulation — use single adapter-driven ComboBox).

**Research flag:** LOW (skip research-phase) — WPF MVVM patterns are established in the codebase; CommunityToolkit.Mvvm 8.4.0 is already present; the `_isBusy` workaround for `IsRunning` is documented and straightforward.

---

### Phase Ordering Rationale

- **Verify before export:** This is the single most important sequencing constraint from research. All four research files independently reach the same conclusion: exporting from a broken verify pipeline produces misleading artifacts that falsely indicate compliance.
- **Interface before adapters:** `IExportAdapter` must be defined and `EmassExporter`/`CklExporter` must implement it before XCCDF, CSV, or Excel adapters are built. This prevents format-specific coupling and enables the WPF format picker to work dynamically against the registry.
- **Model unification in Phase 1:** Deferring `ControlResult`/`NormalizedVerifyResult` unification to the export phase is listed as a "never acceptable" technical debt shortcut in PITFALLS.md. The unification is a Phase 1 task, not a Phase 3 or 4 concern.
- **CSV before Excel:** CSV validates the data model with minimal dependency risk; Excel builds on the same model with one new NuGet dependency. If CSV data is wrong, the Excel workbook will also be wrong — but CSV failures are easier to diagnose.
- **UX last:** UX polish is the only phase with no upstream blockers except verify correctness. Placing it last ensures operators see accurate data in the polished UI, not the previous 0-result state.

### Research Flags

Phases needing deeper research during planning:
- **Phase 1 (SCC verify correctness):** SCC CLI argument form for output directory (`-od` vs. `--output` vs. `-u`) varies by SCC version (5.x); requires live system investigation or official SCC 5.x manual confirmation before argument injection code is finalized. MEDIUM confidence on this specific sub-task.

Phases with standard patterns (skip `/gsd:research-phase`):
- **Phase 2 (IExportAdapter interface):** Direct mirror of `IVerifyResultAdapter`; pattern is proven in codebase.
- **Phase 3 (XCCDF export):** XCCDF 1.2 schema published by NIST; `ScapResultAdapter` provides the field mapping template.
- **Phase 4 (CSV export):** Established CSV escaping pattern; `VerifyReportWriter.WriteCsv` provides the base.
- **Phase 5 (Excel export):** ClosedXML MIT license confirmed; API well-documented; no offline/licensing complexity.
- **Phase 6 (WPF UX polish):** CommunityToolkit.Mvvm 8.4.0 already present; `_isBusy` workaround documented.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Existing stack verified from source; ClosedXML MIT license and netstandard2.0 target confirmed on NuGet; EPPlus commercial license confirmed; one new package total |
| Features | HIGH (codebase) / MEDIUM (SCC) | Codebase features confirmed by direct inspection; SCC CLI flags and output directory structure corroborated by multiple community sources but not official 5.x manual |
| Architecture | HIGH | All findings from direct code inspection; `VerifyOrchestrator` disconnection from `VerificationWorkflowService` confirmed; `ReportGenerator` stub confirmed; model duality confirmed |
| Pitfalls | HIGH | Root causes confirmed by code inspection; XCCDF namespace issue confirmed by OpenRMF issue tracker; SCC timeout hardcode confirmed in source |

**Overall confidence:** HIGH for architecture and stack decisions; MEDIUM for SCC-specific operational behavior (output directory structure, CLI argument names) pending live system validation.

### Gaps to Address

- **SCC CLI argument for output directory:** Research confirms `-od`, `-u`, and `--setOpt` as candidate flags from community sources. The exact flag required by the SCC version deployed in the target environment must be validated against `cscc.exe -?` output or the SCC 5.x User Manual before the argument injection code in Phase 1 is finalized. Handle during Phase 1 implementation by making the output-directory argument configurable in `VerificationWorkflowRequest` and documenting the flag in the UI.
- **SCC session subdirectory naming pattern:** Multiple sources agree on `Sessions/<date-time>/` or `SCC/Results/SCAP/<hostname>_<timestamp>/` structure; not confirmed against a live SCC 5.x installation. Handle during Phase 1 by making the subdirectory scan recursive (`SearchOption.AllDirectories`) with a diagnostic that logs the actual paths found, so operators can report the real pattern if it differs.
- **XCCDF 1.2 schema validation in offline environments:** NIST publishes `xccdf_1.2.xsd` but offline environments cannot fetch it at runtime. Validation during export should be structural (via `ScapResultAdapter` round-trip test) rather than schema-validation against a fetched XSD. Document this constraint — do not add a runtime schema-fetch step.

## Sources

### Primary (HIGH confidence)

- **STIGForge codebase — direct inspection:** `src/STIGForge.Verify/VerificationWorkflowService.cs`, `ScapRunner.cs`, `EvaluateStigRunner.cs`, `VerifyOrchestrator.cs`, `VerifyReportWriter.cs`, `Adapters/ScapResultAdapter.cs`, `Adapters/CklAdapter.cs`, `CklParser.cs`; `src/STIGForge.Export/EmassExporter.cs`, `CklExporter.cs`, `ExportModels.cs`; `src/STIGForge.Reporting/ReportGenerator.cs`; `src/STIGForge.App/MainViewModel.Export.cs` — all findings from direct read (HIGH confidence)
- **NuGet Gallery — ClosedXML 0.105.0** — version, MIT license, and netstandard2.0 target confirmed
- **GitHub — ClosedXML/ClosedXML** — MIT license confirmed
- **NIST CSRC — XCCDF 1.2 Specification** — namespace `http://checklists.nist.gov/xccdf/1.2` and TestResult element structure confirmed
- **CommunityToolkit.Mvvm `AsyncRelayCommand.IsRunning` GitHub issue #4266** — WPF binding limitation confirmed
- **NuGet Gallery — EPPlus 8.4.2** — commercial license requirement confirmed

### Secondary (MEDIUM confidence)

- **SCC command-line automation (multiple community sources):** `-u` output folder flag, `dirXMLEnabled` option, Sessions subdirectory structure — corroborated across multiple sources including DCSA SCAP/STIG Viewer Job Aid and SCC 5.9 User Manual (Scribd)
- **STIG Manager feature documentation** — XCCDF import/export capabilities confirmed for competitor comparison
- **OpenRMF SCAP scan documentation** — XCCDF import confirmed; namespace mismatch failure mode documented in issue #216

### Tertiary (LOW confidence)

- **SCC 5.x exact CLI argument for output directory** — confirmed candidates (`-od`, `-u`, `--setOpt`) from community sources; not verified against official SCC 5.x documentation obtained directly; must validate during Phase 1 implementation against live system or official manual
- **SCC session directory exact naming convention** — `Sessions/<datestamp>/` pattern agreed across sources but not confirmed against live SCC 5.x installation

---
*Research completed: 2026-02-18*
*Ready for roadmap: yes*

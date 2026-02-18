# Architecture Patterns

**Domain:** STIG Compliance Tooling — Export adapters, XCCDF/SCAP export, reporting, SCC verify fix, UX polish
**Researched:** 2026-02-18
**Confidence:** HIGH — based on direct codebase inspection of all affected files

---

## Existing Architecture (Confirmed by Code Inspection)

### Project Layer Map

```
STIGForge.Shared        (primitives, no dependencies)
  └── STIGForge.Core    (abstractions, models, services — depends on Shared)
        ├── STIGForge.Infrastructure  (SQLite repos, path builder, scheduled tasks)
        ├── STIGForge.Content         (import, content pack parsing)
        ├── STIGForge.Apply           (DSC apply, PowerSTIG, snapshot, reboot)
        ├── STIGForge.Verify          (adapters, runners, report writer)
        │     └── STIGForge.Export    (depends on Verify + Core + Infrastructure + Reporting)
        │           └── STIGForge.Reporting  (stub — ReportGenerator.GenerateAsync() is empty)
        ├── STIGForge.Build           (bundle builder)
        ├── STIGForge.Evidence        (evidence collector)
        ├── STIGForge.Cli             (all projects above)
        └── STIGForge.App (WPF)       (all projects above)
```

### Verify Adapter Chain (Confirmed Pattern)

```
IVerifyResultAdapter (interface in STIGForge.Verify.Adapters)
  ├── CklAdapter         — parses .ckl XML files (VULN elements)
  ├── EvaluateStigAdapter — parses Evaluate-STIG XML (STIGChecks/Finding/Check)
  └── ScapResultAdapter  — parses XCCDF 1.2 XML (xccdf:TestResult/rule-result)

VerifyOrchestrator — holds List<IVerifyResultAdapter>, FindAdapter() by CanHandle()
VerificationWorkflowService — runs tools, calls VerifyReportWriter.BuildFromCkls()
VerificationArtifactAggregationService — aggregates multiple JSON reports
```

### Export Pipeline (Confirmed)

```
EmassExporter.ExportAsync()
  ├── reads: bundleRoot/Verify/consolidated-results.json  (via VerifyReportReader)
  ├── reads: bundleRoot/Manual/answers.json
  ├── merges results, generates POA&M, attestations, evidence index
  └── writes: 00_Manifest/, 01_Scans/, 02_Checklists/, 03_POAM/, 04_Evidence/, 05_Attestations/, 06_Index/

CklExporter.ExportCkl()
  ├── reads: bundleRoot/Verify/consolidated-results.json  (via VerifyReportReader)
  └── writes: .ckl or .cklb (zipped) + optional .csv companion

StandalonePoamExporter — wraps PoamGenerator for standalone use (not part of eMASS package)
```

### Critical SCC Verify Bug

`VerificationWorkflowService.RunAsync()` calls `VerifyReportWriter.BuildFromCkls()` which scans the `outputRoot` for `*.ckl` files using `Directory.GetFiles()`. SCC (DISA SCAP Compliance Checker) does NOT produce `.ckl` files in its primary mode — it produces XCCDF result XML files in a named subdirectory (e.g., `SCC/Results/SCAP/<timestamp>/XCCDF-Results*.xml`).

The `ScapResultAdapter` exists and correctly parses XCCDF 1.2 XML, but `VerifyReportWriter.BuildFromCkls()` never calls it — it only searches for `.ckl` files. The verify workflow and the adapter layer are architecturally disconnected. `VerifyOrchestrator` (which uses adapters) is not called by `VerificationWorkflowService`.

This is the root cause of SCC returning 0 results.

### VerifyReport vs NormalizedVerifyReport Duality

Two parallel result models exist:

| Model | Owner | Used by |
|-------|-------|---------|
| `ControlResult` / `VerifyReport` | STIGForge.Verify | `CklParser`, `VerifyReportWriter`, `VerifyReportReader`, `EmassExporter`, `CklExporter` |
| `NormalizedVerifyResult` / `NormalizedVerifyReport` | STIGForge.Verify | `IVerifyResultAdapter` implementations, `VerifyOrchestrator` |

The adapter system produces `NormalizedVerifyReport`. The export system consumes `ControlResult`. `EmassExporter` has a `ConvertToNormalizedResults()` bridge going the other direction. This duality exists because the adapter system was added after the original CKL-based pipeline.

---

## Recommended Architecture for v1.2

### Component Boundary Diagram

```
[SCC / Evaluate-STIG / Manual CKL]
         |
         v
[IVerifyResultAdapter implementations]  <- EXISTING (ScapResultAdapter, CklAdapter, EvaluateStigAdapter)
         |  NormalizedVerifyReport
         v
[VerifyOrchestrator.ParseAndMergeResults()]  <- EXISTING (not wired into workflow)
         |  ConsolidatedVerifyReport
         v
[VerificationWorkflowService]  <- MODIFY: wire adapters for XCCDF output, not just .ckl scan
         |  VerificationWorkflowResult (writes consolidated-results.json)
         v
[IExportAdapter interface]  <- NEW in STIGForge.Export
    ├── EmassExportAdapter    <- REFACTOR from EmassExporter
    ├── XccdfExportAdapter    <- NEW (XCCDF/SCAP result re-export)
    ├── CsvExportAdapter      <- REFACTOR from CklExporter.IncludeCsv + VerifyReportWriter.WriteCsv
    └── ExcelExportAdapter    <- NEW in STIGForge.Reporting (or STIGForge.Export)
         |
         v
[ExportOrchestrator]  <- NEW: selects adapters, dispatches, collects results
         |
         v
[MainViewModel.Export / CLI export-* commands]
```

### New Components Needed

| Component | Project | Type | Purpose |
|-----------|---------|------|---------|
| `IExportAdapter` | STIGForge.Export | Interface (NEW) | Pluggable export contract |
| `XccdfExportAdapter` | STIGForge.Export | Class (NEW) | Write XCCDF 1.2 result XML from ControlResults |
| `CsvExportAdapter` | STIGForge.Export | Class (NEW) | Consolidated CSV of all results (wraps VerifyReportWriter.WriteCsv) |
| `ExcelExportAdapter` | STIGForge.Reporting | Class (NEW) | .xlsx via ClosedXML or DocumentFormat.OpenXml |
| `ExportOrchestrator` | STIGForge.Export | Class (NEW) | Dispatch to selected adapters, return paths |
| `ExportAdapterRegistry` | STIGForge.Export | Class (NEW) | Register/resolve adapters by format name |
| SCC output scanner | STIGForge.Verify | Method (MODIFY) | Scan for XCCDF XML in SCC output dirs, not just .ckl |

### Modified Components

| Component | Project | Change | Scope |
|-----------|---------|--------|-------|
| `VerificationWorkflowService.RunAsync()` | STIGForge.Verify | Wire `ScapResultAdapter` for XCCDF output files after SCC runs | Medium |
| `VerifyReportWriter.BuildFromCkls()` | STIGForge.Verify | Add overload or companion `BuildFromXccdf()` that uses adapters | Small |
| `VerificationWorkflowArtifacts` | STIGForge.Verify | Add `ScapXccdfResultPaths` tracking field | Small |
| `VerificationWorkflowDefaults` | STIGForge.Verify | Add SCC output dir name constants | Small |
| `EmassExporter` | STIGForge.Export | Implement `IExportAdapter`, extract adapter interface | Medium |
| `CklExporter` | STIGForge.Export | Implement `IExportAdapter` | Small |
| `ReportGenerator` | STIGForge.Reporting | Implement Excel generation (currently a stub) | Large |
| `MainViewModel.Export.cs` | STIGForge.App | Add export format picker (XCCDF, CSV, Excel), wire ExportOrchestrator | Medium |
| CLI `VerifyCommands.cs` | STIGForge.Cli | Add `export-xccdf`, `export-csv`, `export-excel` subcommands | Medium |
| `MainViewModel.ApplyVerify.cs` | STIGForge.App | Add SCAP output directory scanning configuration | Small |

---

## Data Flow for Each Feature

### SCC Verify Fix (Fix First — Everything Depends on It)

**Current broken flow:**
```
SCC runs → writes XCCDF XML to SCC/Results/SCAP/<ts>/
VerificationWorkflowService → BuildFromCkls(outputRoot) → searches *.ckl → 0 found
```

**Fixed flow:**
```
SCC runs → writes XCCDF XML to SCC/Results/SCAP/<ts>/
VerificationWorkflowService → scan outputRoot for *.xml → ScapResultAdapter.CanHandle() → ParseResults()
  → NormalizedVerifyReport → convert to ControlResult list
  → BuildFromResults(results) → write consolidated-results.json
```

**Key code change in `VerificationWorkflowService.RunScapIfConfigured()`:**
After the runner completes, scan `request.OutputRoot` recursively for `.xml` files, call `ScapResultAdapter.CanHandle()` on each, parse matches, and write the consolidated JSON. The current `BuildFromCkls()` call should remain for Evaluate-STIG compatibility but needs a parallel path for SCAP.

**Alternative (cleaner):** Introduce `VerifyReportWriter.BuildFromOutputDirectory()` that tries adapters in priority order (ScapResultAdapter for .xml, CklAdapter for .ckl) and unifies the result list.

### XCCDF/SCAP Export

XCCDF export means re-exporting STIGForge's consolidated results in XCCDF 1.2 TestResult format. This is distinct from the SCAP input parsing. The export reads `ControlResult` objects and writes an XCCDF XML following the schema:

```xml
<cdf:Benchmark xmlns:cdf="http://checklists.nist.gov/xccdf/1.2">
  <cdf:TestResult id="..." start-time="..." end-time="...">
    <cdf:target>hostname</cdf:target>
    <cdf:rule-result idref="SV-220697r569187_rule" time="...">
      <cdf:result>pass|fail|notapplicable|notchecked</cdf:result>
    </cdf:rule-result>
    ...
  </cdf:TestResult>
</cdf:Benchmark>
```

Status mapping from ControlResult.Status (string) to XCCDF result values:
- `NotAFinding` / `pass` → `pass`
- `Open` / `fail` → `fail`
- `Not_Applicable` / `notapplicable` → `notapplicable`
- `Not_Reviewed` / `notchecked` → `notchecked`

**Source:** `ExportStatusMapper.cs` already maps in the other direction — the XCCDF adapter uses this in reverse.

### CSV/Excel Reporting

**CSV** is already implemented via `VerifyReportWriter.WriteCsv()` and `CklExporter`'s `WriteChecklistCsv()`. The gap is a unified, pluggable entry point and a standalone CLI command.

**Excel** (.xlsx): `STIGForge.Reporting.ReportGenerator` is a stub. The recommended implementation uses `ClosedXML` (MIT license) which targets net48 and net8.0. Add to `STIGForge.Reporting.csproj`:

```xml
<PackageReference Include="ClosedXML" Version="0.102.3" />
```

ClosedXML wraps DocumentFormat.OpenXml and provides a higher-level API suitable for compliance report generation. It supports multiple worksheets (summary tab, control detail tab, open findings tab), cell formatting, and header freezing — all needed for eMASS-style reporting.

**Excel report structure (recommended):**
- Sheet 1: "Summary" — counts by severity/status, compliance %, tool breakdown
- Sheet 2: "All Controls" — one row per ControlResult (VulnId, RuleId, Title, Severity, Status, Tool, Date)
- Sheet 3: "Open Findings" — filtered view of fail/open results for POA&M reference
- Sheet 4: "Coverage" — CoverageSummary data (same as coverage_by_tool.csv)

### Pluggable Export Adapter Interface

```csharp
// In STIGForge.Export
public interface IExportAdapter
{
    string FormatName { get; }        // "XCCDF", "CSV", "Excel", "CKL", "eMASS"
    string[] SupportedExtensions { get; }  // [".xml"], [".csv"], [".xlsx"], [".ckl"]
    bool RequiresVerifyResults { get; }    // false for eMASS (reads bundle dir)

    Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct);
}

public sealed class ExportAdapterRequest
{
    public string BundleRoot { get; set; } = string.Empty;
    public IReadOnlyList<ControlResult> Results { get; set; } = Array.Empty<ControlResult>();
    public string OutputDirectory { get; set; } = string.Empty;
    public string? FileNameStem { get; set; }
    public IReadOnlyDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
}

public sealed class ExportAdapterResult
{
    public bool Success { get; set; }
    public IReadOnlyList<string> OutputPaths { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string? ErrorMessage { get; set; }
}
```

The `ExportAdapterRegistry` registers instances, `ExportOrchestrator` resolves by format name and dispatches. Both `EmassExporter` and `CklExporter` are refactored to implement `IExportAdapter` without breaking their existing direct call sites (backward-compatible wrapper methods remain).

---

## Patterns to Follow

### Pattern 1: Adapter CanHandle() before ParseResults()

**What:** All verify adapters implement `CanHandle(string filePath)` before `ParseResults()`.
**When:** Any new file format parsing.
**Example:** `ScapResultAdapter.CanHandle()` peeks XML root name before full parse. Do not deserialize unless `CanHandle()` returns true.

```csharp
// In VerificationWorkflowService — new SCC output scan:
var xmlFiles = Directory.GetFiles(request.OutputRoot, "*.xml", SearchOption.AllDirectories);
var scapAdapter = new ScapResultAdapter();
foreach (var xml in xmlFiles.Where(f => scapAdapter.CanHandle(f)))
{
    var report = scapAdapter.ParseResults(xml);
    results.AddRange(ConvertToControlResults(report.Results));
}
```

### Pattern 2: Fix-Before-Export Dependency

**What:** Verify correctness must be fixed before export adapters produce meaningful output.
**When:** Planning build order.
**Rationale:** All export adapters read `consolidated-results.json`. If SCC results are 0, every export format will also be empty. The SCC fix is a prerequisite, not an enhancement.

**Build order enforcement:**
1. Fix SCC output directory scanning in `VerificationWorkflowService`
2. Add `BuildFromOutputDirectory()` to `VerifyReportWriter`
3. Validate with `VerifyReportWriterTests` (existing test file)
4. Then build export adapters that consume the fixed data

### Pattern 3: Secure XML Loading

**What:** All XML loading uses hardened `XmlReaderSettings` (DtdProcessing=Prohibit, XmlResolver=null, MaxCharactersInDocument=20MB).
**When:** Any new XML reader.
**Source:** Established in v1.1 hardening phase. All three adapters already follow this pattern. The XCCDF export writer does not parse XML so this only applies to input adapters.

### Pattern 4: Dual-target Projects (net48 + net8.0)

**What:** STIGForge.Export and STIGForge.Verify target both net48 and net8.0.
**When:** Adding any new dependency to Export or Verify.
**Consequence:** ClosedXML must be added to `STIGForge.Reporting` (which currently only has net8.0 implied). If Excel support is needed in the Export project, either keep Excel in Reporting-only or ensure ClosedXML supports net48. ClosedXML 0.102+ supports netstandard2.0 and net462+, so it is safe for net48.

### Pattern 5: ControlResult as the Exchange Type

**What:** `ControlResult` (not `NormalizedVerifyResult`) is the type that flows through the export pipeline.
**When:** Wiring verify output into export.
**Rationale:** All existing exporters (EmassExporter, CklExporter, VerifyReportWriter) consume `ControlResult`. The adapter system produces `NormalizedVerifyResult`. `EmassExporter.ConvertToNormalizedResults()` bridges out. The bridge in should go the other direction: after `ScapResultAdapter.ParseResults()`, convert `NormalizedVerifyResult` → `ControlResult` to feed `VerifyReportWriter`.

```csharp
private static ControlResult ToControlResult(NormalizedVerifyResult r, string toolName) => new ControlResult
{
    VulnId = r.VulnId,
    RuleId = r.RuleId,
    Title = r.Title,
    Severity = r.Severity,
    Status = MapVerifyStatusToString(r.Status),  // NormalizedVerifyResult.Status → string
    FindingDetails = r.FindingDetails,
    Comments = r.Comments,
    Tool = toolName,
    SourceFile = r.SourceFile,
    VerifiedAt = r.VerifiedAt
};
```

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Adding a Fourth Result Model

**What:** Creating a new model type for export (e.g., `ExportControlResult`) instead of using `ControlResult`.
**Why bad:** The codebase already has a duality problem between `ControlResult` and `NormalizedVerifyResult`. A third model would triple the mapping complexity.
**Instead:** Use `ControlResult` as the canonical exchange type. The `NormalizedVerifyResult` → `ControlResult` conversion belongs in `VerificationWorkflowService` right after adapter parsing, as an internal bridge.

### Anti-Pattern 2: Building Export Adapters Before Fixing the Verify Pipeline

**What:** Implementing XCCDF/CSV/Excel export while SCC still returns 0 results.
**Why bad:** Export adapters will appear to work (they produce files) but produce empty outputs for SCC-scanned systems. Test coverage will mask the problem.
**Instead:** Fix `VerificationWorkflowService` SCC output scanning first, write a test that asserts ConsolidatedResultCount > 0 for a real SCC XML fixture, then proceed to export.

### Anti-Pattern 3: Growing VerifyReportWriter with More Static Methods

**What:** Adding `WriteXccdf()`, `WriteExcel()` etc. as additional static methods on `VerifyReportWriter`.
**Why bad:** `VerifyReportWriter` is already an overloaded static class (WriteJson, WriteCsv, WriteCoverageSummary, WriteOverlapSummary, WriteControlSourceMap, BuildFromCkls, BuildCoverageSummary, BuildOverlapSummary, BuildControlSourceMap). Adding export formats here violates single responsibility.
**Instead:** New format writers go in `STIGForge.Export` as `IExportAdapter` implementations. `VerifyReportWriter` stays focused on verify-internal CSV/JSON output.

### Anti-Pattern 4: VerifyOrchestrator Disconnected from Workflow

**What:** `VerifyOrchestrator` (which correctly uses the adapter pattern) continues to be unused by `VerificationWorkflowService`.
**Why bad:** Two merge/reconciliation code paths exist and diverge over time.
**Instead:** `VerificationWorkflowService` should use `VerifyOrchestrator.ParseAndMergeResults()` (or delegate to it) after tool execution, rather than implementing its own CKL scan. This closes the architectural disconnect.

### Anti-Pattern 5: WPF Export Tab Accumulation

**What:** Adding individual controls for every new export format to `MainViewModel.Export.cs` and the corresponding XAML.
**Why bad:** The export tab already has CKL format picker, POA&M, CKL+CSV options. More options without structure creates an unusable UI.
**Instead:** Use a ComboBox bound to `IExportAdapter.FormatName` values from `ExportAdapterRegistry`. The UI shows one picker and one "Export" button. Options panel below filters by selected adapter capabilities.

---

## Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| `IVerifyResultAdapter` / `VerifyOrchestrator` | Parse tool output files to normalized results | `VerificationWorkflowService` |
| `VerificationWorkflowService` | Run tools, collect results, write consolidated JSON | `EvaluateStigRunner`, `ScapRunner`, `VerifyReportWriter`, `VerifyOrchestrator` |
| `VerifyReportWriter` | Write verify-internal CSV/JSON artifacts | File system |
| `IExportAdapter` / `ExportOrchestrator` | Produce final export deliverables | `VerifyReportReader` (reads consolidated JSON), `STIGForge.Reporting` |
| `EmassExporter` | Build complete eMASS submission package | `PoamGenerator`, `AttestationGenerator`, `EmassPackageValidator` |
| `XccdfExportAdapter` | Write XCCDF 1.2 TestResult XML | `ControlResult` list |
| `ExcelExportAdapter` | Write .xlsx workbook with summary + detail | `ClosedXML`, `ControlResult` list |
| `ReportGenerator` | Excel workbook construction (currently stub) | `ClosedXML` |
| `MainViewModel.Export.cs` | WPF export commands wired to ExportOrchestrator | `ExportOrchestrator`, `EmassExporter` |
| CLI `VerifyCommands.cs` | CLI export commands | `ExportOrchestrator`, `EmassExporter` |

---

## SCC Output Directory Structure (Reference)

DISA SCC writes outputs to a configurable directory. Default layout:
```
<OutputRoot>/
  SCC/
    Results/
      SCAP/
        <hostname>_<timestamp>/
          <hostname>_<stigname>_<ts>_XCCDF-Results.xml
          <hostname>_<stigname>_<ts>_XCCDF-ARF.xml
          <hostname>_<stigname>_<ts>.ckl        (optional, only if CKL output enabled)
      OVAL/
        <hostname>_<timestamp>/
          *.xml
```

**Critical observation:** The `VerificationWorkflowService` searches `request.OutputRoot` directly for `*.ckl`. If the user passes the SCC Results directory as `outputRoot`, CKL files may exist but XCCDF files coexist. The correct fix is to scan for both `*.ckl` (via CklAdapter) and `*.xml` (via ScapResultAdapter) rather than hardcoding the extension. The `VerifyOrchestrator.FindAdapter()` method already does this but is not called.

---

## Scalability Considerations

| Concern | At current scale | At fleet scale (100+ nodes) |
|---------|-----------------|----------------------------|
| XCCDF XML size | Single host: ~2-5 MB per STIG | Multi-host: batch per STIG, not one file |
| Excel generation | ClosedXML in-process: fine for <10K rows | >10K rows: stream mode or split worksheets |
| Export adapter dispatch | Sequential: fine for single bundle | Fleet: parallel adapter dispatch needed |
| Result aggregation | VerificationArtifactAggregationService handles N reports | Same pattern scales to fleet |

---

## Suggested Build Order (Dependency-Driven)

The verify fix is a blocker for all export work. The pluggable interface is a blocker for the WPF format picker.

```
Phase 1: Fix SCC Verify (Blocker for Everything)
  1. VerificationWorkflowService — scan outputRoot for .xml via ScapResultAdapter
  2. VerifyReportWriter — BuildFromOutputDirectory() using adapter chain
  3. VerificationWorkflowModels — add ScapXccdfResultPaths field
  4. Tests — verify ConsolidatedResultCount > 0 for SCC XML fixture

Phase 2: Pluggable Export Interface (Blocker for Format Picker UX)
  1. IExportAdapter interface in STIGForge.Export
  2. ExportAdapterRequest/Result models
  3. ExportAdapterRegistry
  4. ExportOrchestrator
  5. Refactor EmassExporter to implement IExportAdapter
  6. Refactor CklExporter to implement IExportAdapter

Phase 3: XCCDF Export Adapter
  1. XccdfExportAdapter implementing IExportAdapter
  2. XCCDF 1.2 writer (XDocument-based, reuse XNamespace from ScapResultAdapter)
  3. CLI: export-xccdf command in VerifyCommands

Phase 4: CSV Export Adapter
  1. CsvExportAdapter implementing IExportAdapter (thin wrapper over VerifyReportWriter.WriteCsv)
  2. CLI: export-csv command

Phase 5: Excel Export (Reporting Stub Promotion)
  1. Add ClosedXML to STIGForge.Reporting.csproj
  2. Implement ReportGenerator — multi-sheet workbook
  3. ExcelExportAdapter implementing IExportAdapter, calls ReportGenerator
  4. CLI: export-excel command

Phase 6: WPF Export UX
  1. ExportAdapterRegistry injected into MainViewModel
  2. Format picker ComboBox bound to registry
  3. Single "Export" command dispatching through ExportOrchestrator
  4. Status reporting in ExportStatus observable property
```

---

## Integration Points for New Features into Existing Code

### Existing touchpoints for SCC fix

| File | Change |
|------|--------|
| `src/STIGForge.Verify/VerificationWorkflowService.cs` | Add XCCDF scan after ScapRunner.Run() |
| `src/STIGForge.Verify/VerifyReportWriter.cs` | Add BuildFromOutputDirectory() overload |
| `src/STIGForge.Verify/VerificationWorkflowModels.cs` | Optional: add diagnostics for XCCDF files found |
| `tests/STIGForge.UnitTests/Verify/VerificationWorkflowServiceTests.cs` | Add SCC XML fixture test |

### Existing touchpoints for Export adapters

| File | Change |
|------|--------|
| `src/STIGForge.Export/EmassExporter.cs` | Implement IExportAdapter (add interface, keep ExportAsync signature) |
| `src/STIGForge.Export/CklExporter.cs` | Implement IExportAdapter |
| `src/STIGForge.Export/ExportModels.cs` | Add ExportAdapterRequest/ExportAdapterResult |
| `src/STIGForge.Reporting/ReportGenerator.cs` | Replace stub with ClosedXML implementation |
| `src/STIGForge.App/MainViewModel.Export.cs` | Replace individual export commands with adapter-driven dispatch |
| `src/STIGForge.App/STIGForge.App.csproj` | No change (already references Export and Reporting) |
| `src/STIGForge.Cli/Commands/VerifyCommands.cs` | Add export-xccdf, export-csv, export-excel subcommands |

---

## Sources

All findings are from direct code inspection at HIGH confidence. No external sources required.

- `src/STIGForge.Verify/VerificationWorkflowService.cs` — confirms CKL-only scan, VerifyOrchestrator disconnection
- `src/STIGForge.Verify/Adapters/ScapResultAdapter.cs` — confirms XCCDF 1.2 parsing logic exists but is unused by workflow
- `src/STIGForge.Verify/VerifyOrchestrator.cs` — confirms adapter registry and merge logic exists
- `src/STIGForge.Export/EmassExporter.cs` — confirms export pipeline reads consolidated-results.json
- `src/STIGForge.Export/CklExporter.cs` — confirms CKL format writer pattern
- `src/STIGForge.Reporting/ReportGenerator.cs` — confirms stub (single line: Task.CompletedTask)
- `src/STIGForge.App/MainViewModel.Export.cs` — confirms existing WPF export command structure
- `src/STIGForge.Cli/Commands/VerifyCommands.cs` — confirms CLI structure and export-emass registration pattern
- `.planning/STATE.md` — confirms "SCC/verify workflow returning 0 results" is a known blocker

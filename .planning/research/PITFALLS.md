# Pitfalls Research

**Domain:** Compliance tool feature expansion — export adapters, verify correctness, workflow UX polish
**Researched:** 2026-02-18
**Confidence:** HIGH (codebase inspected directly; SCC/XCCDF domain validated against official sources)

---

## Critical Pitfalls

### Pitfall 1: SCC Writes Results to a Session Subdirectory, Not the Output Root

**What goes wrong:**
`VerificationWorkflowService` calls `BuildFromCkls(request.OutputRoot, ...)` which scans `outputRoot` for `*.ckl` files after SCC runs. SCC does not write CKL files directly into the directory passed on the command line. It writes them into a timestamped session subdirectory (e.g., `<OutputRoot>\Sessions\<date-time-hostname>\Results\XMLS\`) or similar nested path. `Directory.GetFiles(outputRoot, "*.ckl", SearchOption.AllDirectories)` should find them with `SearchOption.AllDirectories`, but only if SCC is actually told to use `outputRoot` as its results directory. If the operator does not pass `--od <path>` (or the equivalent SCC flag) in the `--args` string, SCC uses its configured default (often inside `C:\Program Files\SCC\`), and STIGForge scans the wrong directory — finding zero CKL files.

**Why it happens:**
The `ScapRunner.Run()` method takes an arbitrary `commandPath` and `arguments` string and passes them directly to the process. It does not inject or validate an output directory argument. Nothing in the workflow verifies that the output directory SCC will write to matches `request.OutputRoot`. The result: SCC runs successfully (exit code 0), writes files elsewhere, and STIGForge reports 0 results.

**How to avoid:**
- Document clearly in the UI and CLI that `--args` must include the SCC output directory flag (`-od <path>` for SCC 5.x, verify exact flag from `cscc.exe -?` since it varies by SCC version).
- Optionally, have `ScapRunner` (or the workflow) inject the output path argument automatically by appending `-od <outputRoot>` when the operator has not provided it.
- After the run, check the actual count of `.ckl` files found. If `ConsolidatedResultCount == 0` and SCC exited with code 0, emit a specific diagnostic: "SCC completed but no CKL files found in `<outputRoot>`. Verify that SCC was configured to write results to this directory."
- The existing `BuildNoResultDiagnostic` diagnostic is a good start but is too generic — it does not distinguish "SCC ran elsewhere" from "SCC genuinely found nothing wrong."

**Warning signs:**
- `ConsolidatedResultCount == 0` with `ExitCode == 0` and SCC `Output` non-empty.
- No `.ckl` files exist under `outputRoot` after the run.
- SCC's own output mentions a results path different from `outputRoot`.

**Phase to address:**
SCC verify correctness phase (the first v1.2 phase). This is the most likely root cause of the current 0-result bug.

---

### Pitfall 2: CklParser Uses Unsecured XDocument.Load — Security Regression on the CKL Write Path

**What goes wrong:**
`CklAdapter` uses a hardened `LoadSecureXml()` with `DtdProcessing.Prohibit` and `XmlResolver = null`. `CklParser` (the older code still used by `VerifyReportWriter.BuildFromCkls`) uses `XDocument.Load(path)` directly — no hardening at all. When exporting XCCDF or producing new CKL-format output, it is tempting to reuse `CklParser` for round-trip testing. Doing so would expose a security regression: XXE attack surface through user-supplied CKL files that reach the unsecured parser.

**Why it happens:**
Two parallel code paths evolved: the adapter layer (`CklAdapter`) which is hardened, and the legacy `CklParser` + `VerifyReportWriter.BuildFromCkls` pipeline which predates the hardening pattern. The workflow service calls `BuildFromCkls` (legacy path), not the adapter. Developers adding new export features may copy from the older pattern because it is simpler.

**How to avoid:**
- Migrate `CklParser.ParseFile()` to use the same `LoadSecureXml()` pattern as `CklAdapter`.
- After migration, delete or internally forward `CklParser` so there is only one CKL parse path.
- Add a unit test that confirms `CklParser` rejects files with DTD declarations.
- When writing XCCDF export tests, always use the adapter path, not `CklParser`.

**Warning signs:**
- Any call to `XDocument.Load(path)` on user-supplied paths without an `XmlReaderSettings` wrapper.
- New export adapter code that calls `CklParser.ParseFile()` instead of `CklAdapter.ParseResults()`.

**Phase to address:**
SCC verify correctness phase (during the unification of the parse paths). Do not defer to a polish phase.

---

### Pitfall 3: XCCDF Export Namespace Mismatch Breaks Downstream Tool Import

**What goes wrong:**
XCCDF 1.2 requires the namespace `http://checklists.nist.gov/xccdf/1.2` on all elements. If the export generates XML without the correct namespace declaration, or mixes namespace prefixes inconsistently, tools like STIG Viewer, Tenable/ACAS, and OpenRMF fail to import the file silently — they either produce 0 rules or error on import. This is a known issue: the OpenRMF issue tracker documents failures specifically caused by namespace handling differences between XCCDF versions.

**Why it happens:**
`ScapResultAdapter` already hardcodes the correct namespace (`XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"`). Developers writing the export side reverse this — they need to emit that same namespace on every element. Using `XElement("result", ...)` without the namespace creates `<result>` instead of `<xccdf:result>`, which is invalid XCCDF. LINQ to XML requires explicit namespace on every `new XElement(...)` call; there is no automatic inheritance.

**How to avoid:**
- Define a single `static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"` constant in the export class, mirroring the adapter.
- Use `new XElement(XccdfNs + "rule-result", ...)` for every element — never `new XElement("rule-result", ...)`.
- Validate the exported XML against the XCCDF 1.2 schema (`xccdf_1.2.xsd` from NIST CSRC) before shipping.
- Include a round-trip test: export → parse with `ScapResultAdapter` → verify result count matches input.

**Warning signs:**
- STIG Viewer or OpenRMF imports the file but shows 0 controls.
- XML output contains elements without namespace prefixes alongside namespaced elements.
- `ScapResultAdapter.CanHandle()` returns false for a file just generated by the export.

**Phase to address:**
XCCDF/SCAP export phase.

---

### Pitfall 4: VerifyReportWriter.BuildFromCkls Is Disconnected from the Adapter Layer

**What goes wrong:**
`VerificationWorkflowService.RunAsync()` calls `VerifyReportWriter.BuildFromCkls()` which uses `CklParser` (returning `ControlResult`), while the `IVerifyResultAdapter` layer produces `NormalizedVerifyResult`. There are now two parallel models for a verification result (`ControlResult` vs. `NormalizedVerifyResult`) and two parallel summary types (`VerifySummary` from `NormalizedVerifyResult.cs` vs. the fields on `VerifyReport`). Adding XCCDF export must choose one model. Choosing the wrong one means the export phase produces a different schema than the verify phase, requiring a later unification rewrite.

**Why it happens:**
The adapter layer and the workflow service were built at different times. The adapters produce richer results (`NormalizedVerifyResult` with typed `VerifyStatus`), but the workflow service still feeds from the older `ControlResult` model with a string `Status` field. No one unified them.

**How to avoid:**
- Before writing export adapters, unify on `NormalizedVerifyResult` / `VerifyReport` backed by typed `VerifyStatus` as the single canonical model.
- Retire `ControlResult` or make it an internal serialization DTO only.
- Have `VerificationWorkflowService` call `VerifyOrchestrator.ParseAndMergeResults()` (adapter path) instead of `BuildFromCkls` (legacy path).
- This unification is also required for XCCDF export: the exporter needs `RuleId`, `VulnId`, and `VerifyStatus` — all absent from `ControlResult`.

**Warning signs:**
- A new export adapter method signature accepts `IReadOnlyList<ControlResult>` — it should accept `IReadOnlyList<NormalizedVerifyResult>`.
- Tests pass `ControlResult` objects to export logic that was designed for the `NormalizedVerifyResult` schema.
- Status comparisons use string matching (`"NotAFinding"`) instead of `VerifyStatus` enum values.

**Phase to address:**
SCC verify correctness phase — unify the models first. Deferring makes XCCDF export harder.

---

### Pitfall 5: ScapRunner 30-Second Timeout Kills Long SCC Scans

**What goes wrong:**
`ScapRunner.Run()` and `EvaluateStigRunner.Run()` both call `process.WaitForExit(30000)` — a 30-second timeout. A real SCC scan of a hardened Windows system against a full STIG baseline takes 5–20 minutes. The runner kills the process after 30 seconds, the scan produces no output, and `VerificationWorkflowService` reports 0 results. This is a likely secondary contributor to the 0-result bug.

**Why it happens:**
The 30-second default was probably set for testing. Nobody noticed because SCC was not being invoked against a real system with a full STIG content pack.

**How to avoid:**
- Make the timeout configurable via `VerificationWorkflowRequest` (e.g., `Scap.TimeoutSeconds` defaulting to 1800).
- Emit a diagnostic when the timeout fires: "SCAP process killed after {timeout} seconds. Increase timeout or check SCC configuration."
- Do not silently swallow the timeout as a normal exit — it should surface as an error `VerificationToolRunResult`.

**Warning signs:**
- `VerifyRunResult.Error` contains "Process did not exit within 30 seconds" but the workflow reports 0 results without surfacing this error to the operator.
- SCC output is empty but exit code is -1 or shows kill signal.

**Phase to address:**
SCC verify correctness phase. Fix before adding export features.

---

### Pitfall 6: Pluggable Export Adapter Interface Leaks File-System Concerns

**What goes wrong:**
`IVerifyResultAdapter.ParseResults(string outputPath)` takes a file path. If the new export adapter interface mirrors this (`IExportAdapter.Export(NormalizedVerifyReport report, string outputPath)`), it binds each adapter to a single output file. XCCDF export writes one `.xml` file. CSV export writes one `.csv`. Excel export writes one `.xlsx`. But the operator may want to export to a stream, a byte array (for in-memory zip packaging), or multiple files. If the interface is `void Export(string path)`, adding streaming later requires breaking the interface — which means updating every registered adapter.

**Why it happens:**
It feels natural to mirror the existing `ParseResults(string path)` pattern. But parsing is input-bound (you always have a file path); export is output-bound (the destination format varies).

**How to avoid:**
- Design the export interface to return the output as a result object containing the bytes or paths produced, not to write to a caller-specified path directly.
- Alternatively: `ExportResult Export(NormalizedVerifyReport report, ExportOptions options)` where `ExportOptions` contains the output root and the adapter decides what files to write within it.
- Do not return `void`. Returning `ExportResult` (with output paths, byte sizes, and success/error) keeps the interface testable and allows the workflow to verify output was actually produced.

**Warning signs:**
- A new `IExportAdapter` interface with a `void Export(string path)` signature.
- Adapters that call `File.WriteAllBytes()` without returning the path to the caller.
- Unit tests that must read the file system to assert export correctness.

**Phase to address:**
Pluggable export adapter architecture phase. Design the interface contract before implementing the first adapter.

---

### Pitfall 7: CSV Export Silently Produces Malformed Output for Multi-Line Compliance Findings

**What goes wrong:**
`VerifyReportWriter.WriteCsv()` already has a `Csv()` helper that wraps values containing commas, quotes, and newlines. But STIG finding details (`FindingDetails`, `Comments`) often contain multi-line text with `\r\n` sequences and embedded quotes from PowerShell or SCAP output. When the finding is truncated or its escaping is incomplete, the resulting CSV file has rows that span multiple lines, breaking import in Excel and eMASS tooling. The current `Csv()` implementation does handle this — but only if every call site uses it. Any new CSV column added without the `Csv()` wrapper silently corrupts the file.

**Why it happens:**
Developers adding columns to the CSV output copy the `sb.AppendLine(string.Join(",", ...))` pattern and forget to wrap each field in `Csv()`, especially for fields that "shouldn't" contain special characters (like `Tool` or `VulnId`).

**How to avoid:**
- Move the CSV serialization to a dedicated `CsvWriter` class that automatically applies escaping to all values.
- Add a property-based test that generates NormalizedVerifyResult objects with random strings (including commas, quotes, newlines) and verifies the CSV round-trips correctly.
- Gate the `WriteCsv` output with a parser that can count columns per row — mismatches mean malformed output.

**Warning signs:**
- A new CSV column added with `Csv(r.SomeField)` omitted — just bare `r.SomeField`.
- Excel opens the CSV and shows fewer rows than expected, or the final rows are merged.
- `FindingDetails` values appear to start a new "row" in the CSV.

**Phase to address:**
CSV/Excel export phase.

---

### Pitfall 8: Excel Export via ClosedXML or EPPlus Introduces a New Dependency with Licensing Risk

**What goes wrong:**
Adding Excel export (`.xlsx`) requires a library: ClosedXML (MIT, free), EPPlus (LGPLv2.1 for free use, commercial license required for commercial products), or DocumentFormat.OpenXml (MIT, Microsoft). EPPlus is commonly chosen but its license changed to a commercial license for non-personal use in v5+. If EPPlus is pulled in as a dependency without evaluating the license against the project's distribution model (government use, internal tool vs. public distribution), a license violation risk is introduced into an air-gapped tool distributed inside DoD environments.

**Why it happens:**
Developers install the most popular NuGet package (`EPPlus`) without checking the license, especially when the tool is internal/air-gapped and there is no public distribution review.

**How to avoid:**
- Use ClosedXML (MIT license, no commercial restrictions) or `DocumentFormat.OpenXml` (MIT, first-party Microsoft) for the Excel adapter.
- Document the license choice explicitly in the adapter source file header.
- For compliance reporting, consider whether `.xlsx` is truly required or whether a `.csv` file opened in Excel satisfies the use case — which avoids the dependency entirely.
- ClosedXML is appropriate for the scale: STIG reports have hundreds to low thousands of rows, well within ClosedXML's performance envelope.

**Warning signs:**
- `EPPlus` or `NPOI` appear in `*.csproj` dependencies without a license justification comment.
- The Excel export tests require the host machine to have Excel installed.

**Phase to address:**
CSV/Excel export phase. Decide library before writing any code.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Keep `ControlResult` parallel to `NormalizedVerifyResult` | No migration work needed now | XCCDF export must map between two models; every new adapter must decide which model to use; status comparison bugs multiply | Never — unify in the SCC correctness phase |
| Hardcode SCC output path scanning to root only | Simpler implementation | Misses SCC session subdirectories; causes 0-result bugs for all operators | Never — SCC writes to subdirectories |
| Use `XDocument.Load(path)` instead of `LoadSecureXml` in new parsers | Two fewer lines | XXE/billion-laughs attack surface on XCCDF files from untrusted sources | Never in production parse paths |
| Return `void` from export adapters | Simpler signature | Cannot detect if export wrote nothing; cannot chain adapters; cannot zip outputs | Never — always return `ExportResult` |
| Skip namespace on XCCDF export elements | Shorter code | STIG Viewer, ACAS, and OpenRMF fail to parse the output | Never |
| Hardcode 30-second process timeout | Simple constant | Kills real SCC scans; produces false 0-result reports | Never for external tool runners |
| Copy `CklParser` string-status pattern into new adapters | Less refactoring | Typed `VerifyStatus` comparisons become impossible; status mapping logic duplicated and diverges | Never after the model unification |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SCC (cscc.exe) | Not passing `-od <outputRoot>` in the arguments; SCC writes results to its configured default directory, not the workflow root | Validate or inject the output directory argument; after run, count `.ckl` files found and emit a specific diagnostic if zero |
| SCC (cscc.exe) | Expecting SCC to write directly to `outputRoot` — SCC always creates a session subdirectory | Use `SearchOption.AllDirectories` when scanning for CKL files; verify this is the code path being called |
| Evaluate-STIG.ps1 | Hardcoding `powershell.exe` — on Windows 10/11 with PowerShell 7 installed, `powershell.exe` still invokes PS 5.1. This is correct for compatibility. Invoking `pwsh.exe` would break PS 5.1 target. | Keep `powershell.exe` and document why |
| Evaluate-STIG.ps1 | 30-second timeout kills the script before it scans the first STIG rule | Increase default timeout to 1800 seconds (30 minutes); make it configurable |
| XCCDF 1.2 result files | Using namespace `http://checklists.nist.gov/xccdf/1.1` (wrong version) or no namespace | Hardcode the 1.2 namespace as a constant; validate against the NIST schema |
| eMASS CKL export | Adding a new export format that changes the CKL schema used in the existing eMASS flow | Export formats must be isolated to their own adapter; never modify the existing `EmassExporter` to add XCCDF logic |
| Excel (xlsx) | Testing Excel output by visually opening the file | Write round-trip tests that open the file programmatically and assert column values |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Loading all `NormalizedVerifyResult` into a `List<>` before writing CSV/Excel | High memory usage; `OutOfMemoryException` on ClosedXML for large result sets | Stream rows during write; do not accumulate the full list in memory twice | STIG reports with 500+ controls from multiple tools (>10k result objects across a fleet scan) |
| Using `XDocument` for XCCDF export of large result sets | `XDocument` builds the entire XML tree in memory | Use `XmlWriter` for streaming XCCDF export | Reports with >5,000 rule-results |
| Scanning `outputRoot` recursively for `.ckl` files after every verify run | Slow on large output directories with many sessions | Scope the search to the specific session subdirectory produced by this run | Directories with years of accumulated SCC sessions |
| Building coverage overlap (`BuildOverlapSummary`) inside the UI thread | UI freeze during verify completion | Coverage build is already on the workflow thread; ensure WPF binding update uses `Dispatcher.Invoke` only for UI property set | Any report with >1,000 controls |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Using `XDocument.Load(path)` on XCCDF export source files without `XmlReaderSettings` | XXE injection if a crafted XCCDF file is fed to the exporter | Always use `LoadSecureXml()` with `DtdProcessing.Prohibit` and `XmlResolver = null` |
| Writing XCCDF export output without sanitizing field values | Control titles/finding details containing `<` or `>` would produce invalid XML if not encoded | Use LINQ to XML (`XElement`) which encodes automatically; never use `StringBuilder` + string concatenation for XML |
| Including full absolute file paths in exported XCCDF result files | Leaks local machine directory structure in compliance artifacts shared with auditors | Strip or relativize `SourceFile` paths in export output |
| Failing open on export validation errors | A partially written XCCDF file is treated as a valid export artifact | Implement fail-closed: if the export adapter throws, delete the partial output file and return a failed `ExportResult` |
| Adding EPPlus v5+ without license review | License violation in DoD distribution | Use ClosedXML (MIT) or DocumentFormat.OpenXml (MIT) |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Showing "Verify complete: 0 results" without explaining why | Operators assume the system is fully compliant when actually SCC misconfiguration caused 0 results | Show specific diagnostic: "0 CKL files found under `<path>`. Check that SCC is configured to write results here." |
| Triggering export from a button that has no progress indicator | Operators click the button multiple times, producing duplicate exports | Disable the export button while export is running; show a spinner |
| Opening an export file dialog per format (one for XCCDF, one for CSV, one for Excel) | Six clicks to export three formats | One export directory picker; all enabled formats write to the same directory |
| Displaying verify status as a raw string from `VerifyStatus.ToString()` ("NotApplicable") | Operators read "NotApplicable" as a code word; expect "N/A" | Map `VerifyStatus` to display strings: `NotApplicable` → "N/A", `NotReviewed` → "Not Reviewed", `Pass` → "Not a Finding" (matching CKL vocabulary) |
| Not surfacing the process timeout error in the UI | Operators see "0 results" and have no way to know SCC was killed | Surface `VerificationToolRunResult.Error` prominently when `Executed == false` or `ExitCode == -1` |
| Showing a single "Verify" button when SCC and Evaluate-STIG are both available | Operators do not understand why two tools are separate | Label buttons by tool name; show last-run timestamp per tool |

---

## "Looks Done But Isn't" Checklist

- [ ] **SCC Verify Fix:** The run exits with code 0 — verify `.ckl` files were actually found and result count is non-zero before declaring fix complete.
- [ ] **XCCDF Export:** The file was written — verify it imports successfully into STIG Viewer and returns the expected rule count.
- [ ] **XCCDF Export Namespace:** No DTD warning in STIG Viewer — verify the namespace on every element, not just the root.
- [ ] **CSV Export:** The file opens in Excel — verify that rows with multi-line `FindingDetails` fields do not split across rows.
- [ ] **Excel Export:** The file opens in Excel — verify that each column has the correct header and that no column shifts due to missing `Csv()`-equivalent encoding.
- [ ] **Pluggable Adapter Registration:** A new adapter class exists — verify it is registered in the DI container or factory, not just instantiated ad-hoc.
- [ ] **Model Unification:** `ControlResult` is retired — verify no new code references `CklParser.ParseFile()` returning `IReadOnlyList<ControlResult>`.
- [ ] **Timeout Fix:** Timeout constant changed — verify the new default is used in both `ScapRunner` and `EvaluateStigRunner`, not just one.
- [ ] **CklParser Hardening:** `LoadSecureXml` applied — verify with a unit test that passes a file with `<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>` and confirms rejection.
- [ ] **Fail-Closed Export:** Export adapter error path tested — verify that a partial output file does not remain on disk after a write failure.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| SCC output directory mismatch causing 0 results | LOW | Add output directory argument injection; add specific diagnostic; re-run verify |
| CklParser security regression discovered in production | MEDIUM | Patch `CklParser.ParseFile()` to use `LoadSecureXml()`; release patch build; run security gate |
| XCCDF namespace wrong — exported files rejected by ACAS | MEDIUM | Fix namespace constant; regenerate all previously exported files; notify operators |
| Model duplication (`ControlResult` vs `NormalizedVerifyResult`) causes data loss in export | HIGH | Unify models; migrate all callers; re-test all verify and export flows; likely requires a 2-3 phase refactor |
| EPPlus license violation discovered post-release | HIGH | Remove EPPlus; replace with ClosedXML; rebuild and re-release; legal review |
| Process timeout causes silent 0-result reports in production | MEDIUM | Increase timeout; add explicit timeout diagnostic; patch release |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| SCC output directory mismatch (Pitfall 1) | SCC verify correctness | Run verify against a real SCC install; assert `ConsolidatedResultCount > 0` |
| CklParser unsecured XML (Pitfall 2) | SCC verify correctness (during model unification) | Unit test: feed DTD-containing CKL; assert `InvalidDataException` |
| XCCDF namespace mismatch (Pitfall 3) | XCCDF/SCAP export phase | Round-trip test: export → parse with `ScapResultAdapter` → count matches |
| Model duplication ControlResult vs NormalizedVerifyResult (Pitfall 4) | SCC verify correctness (first) | Zero references to `ControlResult` in export adapter code |
| 30-second process timeout (Pitfall 5) | SCC verify correctness | Integration test with a mock slow process; assert timeout is configurable |
| Leaky export adapter interface (Pitfall 6) | Export adapter architecture phase (design before first adapter) | `IExportAdapter.Export()` returns `ExportResult`; confirmed in code review |
| CSV malformed multi-line fields (Pitfall 7) | CSV/Excel export phase | Property-based test with random string values including `\n`, `"`, `,` |
| Excel library license risk (Pitfall 8) | Export adapter architecture phase (before adding dependency) | `*.csproj` references only MIT-licensed Excel libraries |

---

## Sources

- Codebase inspection: `ScapRunner.cs`, `EvaluateStigRunner.cs`, `VerificationWorkflowService.cs`, `CklParser.cs`, `CklAdapter.cs`, `ScapResultAdapter.cs`, `VerifyOrchestrator.cs`, `VerifyReportWriter.cs` (direct)
- NIST XCCDF 1.2 specification and schema: [https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/xccdf](https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/xccdf)
- XCCDF 1.2 XSD: [https://csrc.nist.gov/schema/xccdf/1.2/xccdf_1.2.xsd](https://csrc.nist.gov/schema/xccdf/1.2/xccdf_1.2.xsd)
- OpenRMF XCCDF 1.2 import issue (namespace mismatch documented): [https://github.com/Cingulara/openrmf-docs/issues/216](https://github.com/Cingulara/openrmf-docs/issues/216)
- SCC output directory behavior: [DCSA SCAP/STIG Viewer Job Aid](https://www.dcsa.mil/portals/91/documents/ctp/tools/SCAP_Compliance_Checker_and_STIG_Viewer_Job_Aid.pdf); [SCC 5.9 User Manual (Scribd)](https://www.scribd.com/document/736380548/SCC-5-9-UserManual)
- SCC command-line automation: [Career Mentor Group — Automating SCAP with GitLab](https://www.careermentorgroup.com/post/automating-scap-compliance-checks-with-scc-scans-and-gitlab)
- ClosedXML vs EPPlus .NET Excel library comparison: [elmah.io blog](https://blog.elmah.io/working-with-excel-files-in-net-openxml-vs-epplus-vs-closedxml/)
- WPF async MVVM pitfalls: [Microsoft Learn — Async MVVM Commands](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/april/async-programming-patterns-for-asynchronous-mvvm-applications-commands)
- EPPlus license change (v5+ commercial): [EPPlus Software](https://www.epplussoftware.com/)

---
*Pitfalls research for: STIGForge v1.2 — export adapter, verify correctness, and UX polish*
*Researched: 2026-02-18*

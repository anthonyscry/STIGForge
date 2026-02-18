# Feature Research

**Domain:** Windows STIG compliance tool — export expansion, verify accuracy, workflow polish
**Researched:** 2026-02-18
**Confidence:** HIGH (codebase inspection + domain evidence), MEDIUM (SCC internals), LOW (SCC 0-results root cause without live system access)

---

## Existing Feature Inventory (Do Not Rebuild)

Before mapping new features, the following are already shipped and must be treated as
integration points, not build targets:

- eMASS CKL export with cross-artifact validation (EmassExporter, CklExporter)
- CSV export of verify results (VerifyReportWriter.WriteCsv)
- Adapter interface for verify result formats (IVerifyResultAdapter, CklAdapter, EvaluateStigAdapter, ScapResultAdapter)
- Normalized verify result schema (NormalizedVerifyResult, VerifyStatus)
- SCC invocation (ScapRunner) and Evaluate-STIG invocation (EvaluateStigRunner)
- VerificationWorkflowService (orchestrates tool runs, aggregates CKL output, writes JSON/CSV)
- PoamGenerator (JSON + CSV POA&M from failed findings)
- AttestationGenerator (JSON + CSV attestation templates)
- Simple Mode, Manual, Build, Apply, Verify, Export WPF views

The v1.2 work starts from this foundation. Feature descriptions below describe what is NEW
or CHANGED, not what already exists.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that operators at ISSM/ISSO/admin level will assume STIGForge has.
Missing these makes the product feel broken for v1.2.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Verify returns real findings from SCC scan | The verify workflow runs but returns 0 results — core functionality is broken without this | HIGH | ScapRunner has a 30-second hardcoded timeout that is almost certainly too short for SCC; SCC writes output to a `Sessions/<datestamp>/` subdirectory under a configurable `-u` output root, not directly to the path STIGForge watches; cscc.exe CLI arguments (`-f`, `-u`, `--setOpt dirXMLEnabled`) differ from what was assumed. Fix requires: correct args construction, correct output directory watching, and adequate timeout. Confidence: MEDIUM on root cause — needs live system investigation. |
| XCCDF result file export | Operators need XCCDF XML to import into Tenable/ACAS and eMASS — STIG Manager, OpenRMF, and every peer tool support this | MEDIUM | ScapResultAdapter already parses XCCDF; need an XccdfExporter that serializes NormalizedVerifyReport back to XCCDF 1.2 format. XCCDF 1.2 namespace is `http://checklists.nist.gov/xccdf/1.2` — already referenced in ScapResultAdapter. Main complexity: producing a valid TestResult element with correct rule-result children and XCCDF schema compliance. |
| CSV compliance report for management/auditors | Every compliance tool in the ecosystem provides a flat CSV export of findings; the VerifyReportWriter.WriteCsv already exists but has minimal columns and no filtering/sorting for human consumption | LOW | Existing CSV has 8 columns. Management-facing report needs: system name, STIG title, CAT level, status, finding detail, remediation priority, due date. Add a `ComplianceReportCsvExporter` that takes NormalizedVerifyReport + metadata and writes a richer CSV. |
| Pluggable export adapter interface | Engineers and future maintainers expect one interface to implement to add new formats — the verify side already uses IVerifyResultAdapter this way | MEDIUM | Need an `IExportAdapter` interface mirroring IVerifyResultAdapter: `string FormatName`, `string FileExtension`, `void Export(NormalizedVerifyReport, ExportRequest, string outputPath)`. Register adapters in DI. Wire through CLI `export-xccdf`, `export-csv-report` commands and WPF Export view format picker. |
| Status display that shows real verify progress | Current verify view shows minimal feedback; operators cannot tell if SCC is running, stuck, or done; after the SCC bug is fixed, the UX must show per-tool status | MEDIUM | Add a VerifyStatus model with: ToolName, State (Pending/Running/Complete/Failed), ExitCode, FindingCount, ElapsedSeconds. Bind to WPF verify view progress area. |
| Error messages that say what to do, not just what failed | Current diagnostics say "No CKL results were found under output root..." without recovery steps | LOW | Add recovery guidance text per error code. Pattern: "[ERROR-VERIFY-001] SCC found 0 results. Check: SCC output directory matches --output-root, SCC scan profile is selected, SCAP content for this OS is imported." Same pattern for tool-not-found, permission errors. |

### Differentiators (Competitive Advantage)

Features that set STIGForge apart from tools like STIG Manager and OpenRMF for
its specific use case: offline-first, Windows STIG, operator-in-the-field workflow.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Excel (.xlsx) compliance report with pre-built tabs | Management and auditors expect Excel, not CSV — separate Finding Detail, Summary, and POA&M tabs save hours of manual formatting; ClosedXML (MIT license, offline-safe) handles xlsx generation in .NET | MEDIUM | ClosedXML 0.105.0 is MIT-licensed, actively maintained (NuGet confirmed), works offline. Use for: Summary tab (counts by CAT/status), Findings tab (filterable by CAT/status/STIG), POA&M tab (timeline, milestones, remediation dates). ClosedXML preferred over EPPlus (commercial license required since v5). Confidence: HIGH on library choice. |
| SCC output directory auto-discovery | SCC writes to `Sessions/<datestamp>/` subdirectories — most tools require manual path entry; STIGForge can discover the latest session directory automatically | MEDIUM | After SCC exits, enumerate `<output-root>/Sessions/` for the newest directory, then scan for `*XCCDF*.xml` files within it. Eliminates the most common operator error (wrong output root). Requires VerificationWorkflowService change: add post-run discovery logic to ScapRunner/VerificationWorkflowService. |
| Workflow status dashboard showing Build/Apply/Verify/Export state in one view | STIG Manager and OpenRMF are web/multi-user tools; STIGForge's single-operator offline model can show one cohesive mission status (last build, apply result, verify result, export readiness) without tab-switching | MEDIUM | Extend DashboardView or add a MissionStatusPanel to SimpleModeView. Pull status from: bundle manifest (build state), Apply audit trail (apply result), consolidated-results.json (verify count), eMASS export manifest (export state). Visual: four-step pipeline with green/red/pending states per step. |
| Export format picker with format-specific field help | Operators choosing between XCCDF, CKL, CSV-summary, and Excel need format-specific guidance (e.g., "XCCDF for Tenable/ACAS import", "Excel for ISSM review") without reading documentation | LOW | WPF Export view format dropdown + a description TextBlock below that changes per selection. Wire to IExportAdapter.Description property. Zero implementation risk, high operator value. |
| Inline retry on verify failure with preserved state | When SCC fails (bad path, wrong args), operators currently restart the whole workflow; allowing inline retry with editable path/args and preserved output root saves one complete re-run cycle | MEDIUM | Add retry state to VerificationWorkflowResult: LastFailureReason, SuggestedFix, IsRetryable. In WPF VerifyView: show a "Retry with corrected args" workflow that preserves previous output root and only re-runs the failed tool. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem valuable but would create disproportionate cost or risk for STIGForge.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| ARF (Asset Reporting Format) export | SCAP 1.2 specifies ARF as the preferred result container; some tools require it | ARF is a container wrapping XCCDF + OVAL results; generating valid ARF requires OVAL output that STIGForge does not produce (SCC produces it, but STIGForge does not control SCC output). Building partial ARF creates a broken file. | Expose the SCC-generated ARF/XCCDF files directly in the export package as raw scan artifacts (already done via CopyScans in EmassExporter). Document that raw SCC XML in the 01_Scans folder is the ARF artifact. |
| PDF report generation | Management wants PDF instead of Excel | PDF generation requires either a rendering engine (license cost, heavy dependency) or an HTML-to-PDF converter (offline incompatible). Result is brittle formatting that breaks across page boundaries. | Excel with pre-formatted tabs prints cleanly to PDF via Excel itself; document this workflow instead. |
| Real-time progress within SCC scan | Operators want a progress bar during the SCC scan | SCC does not expose a progress API; polling for interim files is unreliable; fake progress bars create false confidence in tool status | Show elapsed time + "SCC is running" status with a spinner. Update count when scan completes. |
| Automatic severity override propagation | Operators want to set a severity override once and have it apply across all future scans | Severity overrides in STIGForge are per-control, per-bundle — propagating across bundles requires a cross-bundle state model that breaks the bundle-as-atomic-artifact design | Allow overlay-level severity overrides (already in Overlay model) and document that overlays carry forward via the rebase workflow |
| Multi-format simultaneous export | "Export all formats at once" | Creates a combinatorial explosion of output paths, naming conflicts, and user confusion about which file to submit | Export one format at a time via format picker; allow sequential re-export from same bundle without re-running verify |

---

## Feature Dependencies

```
SCC 0-results fix (verify correctness)
    └──required-by──> XCCDF export (export of empty/0-result report is meaningless)
    └──required-by──> Excel/CSV compliance report (reports on 0 results confuse auditors)
    └──required-by──> Workflow status dashboard (dashboard shows 0-result state incorrectly)

IExportAdapter interface
    └──required-by──> XCCDF exporter (registers as adapter)
    └──required-by──> CSV compliance report exporter (registers as adapter)
    └──required-by──> Excel exporter (registers as adapter)
    └──required-by──> Export format picker WPF (iterates registered adapters)

ClosedXML dependency (NuGet)
    └──required-by──> Excel exporter

SCC output directory auto-discovery
    └──enhances──> SCC 0-results fix (prevents re-occurrence of path mismatch bug)
    └──requires──> SCC 0-results fix (discovery makes no sense against broken invocation)

Inline retry on verify failure
    └──requires──> Error messages with recovery guidance (retry surfaces the guidance)
    └──enhances──> Workflow status dashboard (retry state feeds into status)
```

### Dependency Notes

- **SCC fix required before export features:** Exporting a report with 0 results from a broken verify run produces a document that falsely implies 100% compliance. Export features must not be built until verify produces accurate findings.
- **IExportAdapter required before any format:** Each export format is an implementation detail. Building XCCDF before the interface is defined means refactoring later. Define the interface first.
- **ClosedXML vs EPPlus:** EPPlus v5+ requires a commercial license — incompatible with offline/classified environments where license servers are unavailable. ClosedXML is MIT, no license validation, fully offline. Use ClosedXML.
- **SCC auto-discovery enhances but does not block fix:** The core SCC fix (correct args, correct timeout, correct output root) should land in one phase. Auto-discovery is an improvement that can follow.

---

## MVP Definition (v1.2 Milestone Scope)

### Launch With (v1.2 = these four areas, sequenced)

The v1.2 milestone has four areas. They are listed in recommended implementation order based on dependencies.

- [ ] **SCC verify correctness** — Fix ScapRunner: (a) increase timeout from 30s to a configurable value (SCC scans take 2-15 minutes), (b) correct output directory pattern (`Sessions/<datestamp>/`), (c) correct cscc.exe CLI argument construction, (d) add post-run XCCDF discovery. This is a prerequisite for everything else.
- [ ] **Pluggable IExportAdapter interface** — Define the interface, register existing CKL export as first adapter, wire CLI and WPF to enumerate adapters. This unlocks the next two items.
- [ ] **XCCDF result export + CSV compliance report** — Implement XccdfExporter and ComplianceReportCsvExporter as IExportAdapter implementations. CLI: `export-xccdf`, `export-csv-report`. WPF: format picker in Export view.
- [ ] **Workflow UX polish** — Error messages with recovery guidance, verify status progress display, export format picker with help text, inline retry state. These are independent of each other but depend on the verify fix being done first.

### Add After Validation (v1.2 stretch)

These are worthwhile if the core four complete ahead of schedule, but are not blockers.

- [ ] **Excel (.xlsx) compliance report** — Adds ClosedXML dependency; implement after CSV report is working since xlsx is an extension of the same data model. Trigger: CSV report is shipped and operators request formatted output.
- [ ] **SCC output directory auto-discovery** — Eliminates the most common verify re-run scenario. Trigger: SCC fix is shipped and operators report residual path confusion.

### Future Consideration (v2+)

Defer these until v1.2 milestone is validated.

- [ ] **Mission status dashboard (four-step pipeline view)** — Architectural change to DashboardView; depends on clean state tracking across Build/Apply/Verify/Export lifecycle. Defer until v1.2 polish is done.
- [ ] **Inline retry on verify failure** — Useful but requires verify state machine changes. Defer to v1.3.
- [ ] **ARF export** — Not practically achievable without SCC OVAL outputs. Defer indefinitely.

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| SCC verify correctness fix | HIGH — tool is broken without it | HIGH — diagnosis + timeout + path discovery + test | P1 |
| IExportAdapter interface | HIGH — unlocks all export formats | LOW — 20 lines of interface + DI wiring | P1 |
| XCCDF result export | HIGH — Tenable/ACAS/eMASS interop | MEDIUM — produce XCCDF 1.2 TestResult XML correctly | P1 |
| CSV compliance report (management) | HIGH — auditor-facing artifact | LOW — extend existing VerifyReportWriter.WriteCsv | P1 |
| Error messages with recovery guidance | HIGH — reduces support burden | LOW — string constants + code lookup | P1 |
| Verify status progress display | MEDIUM — UX improvement | MEDIUM — WPF binding + async status model | P2 |
| Export format picker with help text | MEDIUM — discoverability | LOW — dropdown + TextBlock | P2 |
| Excel (.xlsx) compliance report | HIGH — management prefers Excel | MEDIUM — ClosedXML + multi-tab layout | P2 |
| SCC output directory auto-discovery | MEDIUM — prevents recurrence | MEDIUM — directory enumeration + latest-session logic | P2 |
| Inline retry on verify failure | MEDIUM — saves re-run cycle | HIGH — state machine changes | P3 |
| Mission status dashboard | LOW-MEDIUM — nice UX | HIGH — lifecycle state model | P3 |

---

## Competitor Feature Analysis

Tools analyzed: STIG Manager (NSA/DoD), OpenRMF (open source), STIG Viewer (DISA).

| Feature | STIG Manager | OpenRMF | STIGForge Approach |
|---------|--------------|---------|-------------------|
| XCCDF import | Yes — full XCCDF 1.2 result import | Yes — XCCDF + ARF | Import via ScapResultAdapter (already built); export is new |
| XCCDF export | Yes — generates CKL from review state | Partial | Build XccdfExporter from NormalizedVerifyReport |
| Excel/CSV report | No native Excel; CSV available | Basic CSV | ClosedXML-based Excel as differentiator |
| Offline-first | No — requires network, auth server | No — Docker-based, network | Core STIGForge constraint — all formats must work offline |
| Workflow progress | Yes — collection-level progress bars | Yes — system-level dashboard | Add per-tool verify status; defer full dashboard to v1.3 |
| SCC integration | Import of SCC XCCDF output | Import of SCC XCCDF output | Fix SCC invocation to produce actual output |
| Error recovery | Limited — upload fails silently | Limited | Recovery guidance per error code is a differentiator |
| Format picker | N/A (web UI, implicit format) | N/A | WPF format picker with format-specific help text |

Note: STIG Manager and OpenRMF are multi-user, network-dependent tools. STIGForge's offline-first, single-operator, Windows-native model is the primary differentiator — not feature parity with those tools.

---

## Sources

- STIGForge codebase: `src/STIGForge.Verify/`, `src/STIGForge.Export/`, `src/STIGForge.App/` (direct inspection, HIGH confidence)
- STIG Manager features: https://stig-manager.readthedocs.io/en/1.4.7/features/ (MEDIUM confidence)
- OpenRMF SCAP scan documentation: https://cingulara.github.io/openrmf-docs/scapscans.html (MEDIUM confidence)
- SCC command-line usage (cscc.exe, -u, --setOpt): multiple search results confirming `-u` for output folder, `dirXMLEnabled` for XML output control (MEDIUM confidence — no SCC 5.x official manual obtained)
- SCC output directory structure (Sessions subdirectory): community documentation and SCC job aid PDF reference at https://www.dcsa.mil/portals/91/documents/ctp/tools/SCAP_Compliance_Checker_and_STIG_Viewer_Job_Aid.pdf (LOW confidence — PDF not directly fetched; based on multiple corroborating sources)
- ClosedXML MIT license: https://github.com/ClosedXML/ClosedXML (HIGH confidence — MIT license confirmed)
- EPPlus commercial license since v5: https://itenium.be/blog/dotnet/epplus-pay-to-play/ and https://www.epplussoftware.com/en/LicenseOverview (HIGH confidence)
- XCCDF 1.2 namespace: NIST CSRC specification + ScapResultAdapter source (`http://checklists.nist.gov/xccdf/1.2`) (HIGH confidence)

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Verify 0-results root cause | MEDIUM | ScapRunner 30s timeout and output path mismatch are the most likely causes based on SCC behavior patterns, but root cause requires live system diagnosis |
| SCC CLI argument correctness | MEDIUM | `-u`, `-f`, `--setOpt` flags confirmed from community sources; official 5.x docs not directly obtained |
| SCC Sessions/ output directory | MEDIUM | Multiple sources agree on Sessions/<datestamp>/ structure; not verified against live SCC 5.x installation |
| XCCDF export approach | HIGH | XCCDF 1.2 schema is published; ScapResultAdapter already parses it; inverse is straightforward |
| ClosedXML for Excel | HIGH | MIT license confirmed, actively maintained, no internet required |
| EPPlus incompatibility | HIGH | Commercial license since v5 clearly documented; not viable for offline/classified use |
| IExportAdapter design | HIGH | IVerifyResultAdapter provides the direct template; pattern is proven in codebase |
| Workflow UX pain points | HIGH | Direct codebase inspection of ScapRunner (30s timeout), VerificationWorkflowService (no progress model), and WPF views (minimal status display) |

---

*Feature research for: STIGForge v1.2 — Verify Accuracy, Export Expansion, Workflow Polish*
*Researched: 2026-02-18*

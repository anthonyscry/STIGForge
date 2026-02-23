# Phase 18: Excel Compliance Report - Context

**Gathered:** 2026-02-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Operators can export a multi-tab Excel workbook (.xlsx) for management and auditor review via CLI `export-excel` command. The workbook contains four tabs: Summary, All Controls, Open Findings, and Coverage. This phase implements the real ReportGenerator (replacing the stub) and adds ClosedXML 0.105.0 as the only new NuGet dependency. This phase does NOT include WPF format picker UI (Phase 19).

</domain>

<decisions>
## Implementation Decisions

### Tab design — Summary
- Workbook title row: "STIGForge Compliance Report" with system name and generation timestamp
- Metrics block: Total Controls, Pass Count, Fail Count, Not Applicable Count, Not Reviewed Count
- Pass rate percentage: (Pass / Total) * 100
- Severity breakdown: CAT I (high) count, CAT II (medium) count, CAT III (low) count
- Open findings by severity: CAT I open, CAT II open, CAT III open
- System name from Options["system-name"] or BundleRoot basename (same as CSV adapter)

### Tab design — All Controls
- Same 13 columns as CsvExportAdapter (System Name through Verified At) with identical human-readable headers
- All ControlResult entries, sorted by Severity (high first) then VulnId
- Auto-filter enabled on header row for Excel sorting/filtering

### Tab design — Open Findings
- Same columns as All Controls, but filtered to Status = Fail/Open only
- Sorted by Severity (high first) then VulnId
- This tab is the primary auditor view — shows only what needs remediation

### Tab design — Coverage
- Columns: Severity, Total, Pass, Fail, Not Applicable, Not Reviewed, Pass Rate (%)
- One row per severity level (CAT I, CAT II, CAT III, Unknown)
- Totals row at bottom
- This tab gives management a one-page compliance posture summary

### ReportGenerator implementation
- ReportGenerator receives IReadOnlyList<ControlResult> and options, returns a generated XLWorkbook
- ExcelExportAdapter calls ReportGenerator.GenerateAsync to build the workbook, then saves to disk using fail-closed temp-file pattern
- ReportGenerator lives in STIGForge.Reporting; ExcelExportAdapter lives in STIGForge.Export (referencing STIGForge.Reporting)
- ClosedXML 0.105.0 (MIT) added to STIGForge.Reporting.csproj only — not to STIGForge.Export

### CLI flags and file naming
- Follow existing export-csv pattern: --bundle (required), --output (optional), --file-name (optional), --system-name (optional)
- Default file name stem: "stigforge_compliance_report"
- Default output directory: {bundle}/Export/

### Fail-closed write pattern
- Same temp-file + rename pattern as CsvExportAdapter and XccdfExportAdapter
- Partial output deleted on failure

### Claude's Discretion
- ClosedXML API usage patterns (XLWorkbook, IXLWorksheet, cell styling)
- Column width auto-fit strategy
- Whether to add basic cell formatting (bold headers, severity coloring) or keep it plain
- Test structure and what aspects of Excel output to validate (cell values, sheet names, row counts)

</decisions>

<specifics>
## Specific Ideas

- The workbook should contain the same control data as the CSV export (SC-2 requires this) — reuse the same column mapping and severity-to-CAT-Level logic from CsvExportAdapter
- Tests should verify: sheet names exist, row counts match, cell values are correct, workbook can be read back by ClosedXML
- Follow the same IExportAdapter contract: FormatName "Excel", SupportedExtensions [".xlsx"]
- STIGForge.Reporting needs a ProjectReference to STIGForge.Verify for ControlResult access

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 18-excel-compliance-report*
*Context gathered: 2026-02-19*

# Phase 17: CSV Compliance Report - Context

**Gathered:** 2026-02-19
**Status:** Ready for planning

<domain>
## Phase Boundary

Operators can export a management-facing compliance report as CSV via CLI `export-csv` command. The CSV includes system name, STIG title, CAT level, status, finding detail, and remediation priority columns. Values are correctly escaped per RFC 4180. This phase does NOT include Excel export (Phase 18) or WPF format picker UI (Phase 19).

</domain>

<decisions>
## Implementation Decisions

### Column design
- Column order: VulnId, RuleId, Title, Severity, CATLevel, Status, FindingDetails, Comments, Tool, SourceFile, VerifiedAt
- CATLevel is derived from Severity: high→CAT I, medium→CAT II, low→CAT III, other/null→"Unknown"
- "System name" and "STIG title" from success criteria map to existing ControlResult fields: Title covers STIG title; system name comes from ExportAdapterRequest.BundleRoot basename or a --system-name CLI option
- "Remediation priority" maps to CATLevel (CAT I = highest priority) — no separate remediation data exists in ControlResult
- Header row is always included as the first row
- All ControlResult properties are included — management audience can filter/sort in their spreadsheet tool

### CSV escaping
- RFC 4180 compliant: fields containing commas, double quotes, or newlines are wrapped in double quotes; embedded double quotes are escaped as ""
- No BOM (byte order mark) — standard UTF-8 output for maximum tool compatibility
- Line endings: CRLF per RFC 4180
- Null/empty fields written as empty string (no "null" literal, no "N/A" placeholder)

### CLI flags and file naming
- Follow existing export-xccdf pattern: --bundle (required), --output (optional), --file-name (optional)
- Add --system-name (optional) for the system name column; defaults to bundle directory name if not provided
- Default file name stem: "stigforge_compliance_report"
- Default output directory: {bundle}/Export/ (same as export-xccdf)

### Fail-closed write pattern
- Same temp-file + rename pattern as XccdfExportAdapter
- Partial output deleted on failure

### Claude's Discretion
- Whether to use StreamWriter directly or StringBuilder + File.WriteAllText (performance tradeoff for large result sets)
- Exact CSV escaping implementation (manual string building vs helper method)
- Test structure and organization within CsvExportAdapterTests.cs

</decisions>

<specifics>
## Specific Ideas

- The CSV is management-facing: column headers should be human-readable (e.g., "Vulnerability ID" not "VulnId", "CAT Level" not "CATLevel", "Finding Details" not "FindingDetails")
- Property-based tests for CSV escaping: generate strings with commas, quotes, newlines, and verify round-trip through a CSV parser
- Follow the same IExportAdapter contract established in Phase 15 — FormatName: "CSV", SupportedExtensions: [".csv"]

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 17-csv-compliance-report*
*Context gathered: 2026-02-19*

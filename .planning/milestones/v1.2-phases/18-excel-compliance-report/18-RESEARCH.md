# Phase 18: Excel Compliance Report - Research

**Researched:** 2026-02-19
**Confidence:** HIGH across all domains

## Domain 1: ClosedXML API Patterns

**Confidence: HIGH**

### Workbook and Worksheet Management
- `new XLWorkbook()` creates an empty workbook
- `workbook.Worksheets.Add("Sheet Name")` creates a named worksheet, returns `IXLWorksheet`
- `worksheet.Cell(row, col).Value` sets cell content (1-indexed)
- `worksheet.Cell("A1").Value` also works with cell references
- `workbook.SaveAs(filePath)` writes to disk

### Cell Styling
- `cell.Style.Font.Bold = true` for bold headers
- `cell.Style.Fill.BackgroundColor = XLColor.LightGray` for fills
- `cell.Style.Font.FontColor = XLColor.Red` for font color
- `cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center` for alignment
- `cell.Style.NumberFormat.Format = "0.0%"` for percentage formatting

### Auto-Filter and Column Width
- `worksheet.RangeUsed().SetAutoFilter()` enables auto-filter on the data range
- `worksheet.Columns().AdjustToContents()` auto-fits column widths
- `worksheet.Column(1).Width = 20` sets explicit width

### Range Operations
- `worksheet.Range(row1, col1, row2, col2)` selects a rectangular range
- Range styling applies to all cells in the range

### NuGet Package
- ClosedXML 0.105.0 (MIT license)
- Package ID: `ClosedXML`
- No transitive dependencies that conflict with net48 or net8.0 targets

## Domain 2: IExportAdapter Integration

**Confidence: HIGH**

### Pattern (from CsvExportAdapter, XccdfExportAdapter)
- Implement `IExportAdapter` with `FormatName`, `SupportedExtensions`, `ExportAsync`
- `ExportAdapterRequest` provides `BundleRoot`, `Results`, `OutputDirectory`, `FileNameStem`, `Options`
- `ExportAdapterResult` returns `Success`, `OutputPaths`, `Warnings`, `ErrorMessage`
- Fail-closed temp-file write pattern: write to `.tmp`, rename on success, delete `.tmp` on failure
- System name from `Options["system-name"]` or `Path.GetFileName(BundleRoot)`

### Architecture Decision
- `ExcelExportAdapter` lives in `STIGForge.Export` (implements `IExportAdapter`)
- `ReportGenerator` lives in `STIGForge.Reporting` (builds `XLWorkbook`, returns it)
- ClosedXML 0.105.0 added to `STIGForge.Reporting.csproj` only
- `STIGForge.Export` does NOT reference ClosedXML — it references `STIGForge.Reporting`
- ExcelExportAdapter calls `ReportGenerator.GenerateAsync(results, options)` to get workbook, then saves

### Cross-Project Reference Chain
- `STIGForge.Reporting` already references `STIGForge.Core` and `STIGForge.Infrastructure`
- `STIGForge.Reporting` needs a reference to `STIGForge.Verify` for `ControlResult`
- `STIGForge.Export` needs a reference to `STIGForge.Reporting` for `ReportGenerator`

## Domain 3: CLI Integration

**Confidence: HIGH**

### Pattern (from export-csv, export-xccdf)
- Add `RegisterExportExcel(rootCmd, buildHost)` call to `ExportCommands.Register()`
- Options: `--bundle` (required), `--output` (optional), `--file-name` (optional), `--system-name` (optional)
- Load results from `Verify/consolidated-results.json` using `VerifyReportReader.LoadFromJson`
- Create adapter, call `ExportAsync`, print summary to console

## Domain 4: Test Strategy

**Confidence: HIGH**

### What to Test
- FormatName and SupportedExtensions contract
- Sheet names exist (Summary, All Controls, Open Findings, Coverage)
- Summary tab: metrics cells contain correct computed values
- All Controls tab: row count = header + results count
- Open Findings tab: only contains Fail/Open status entries
- Coverage tab: severity breakdown rows with correct pass rates
- Auto-filter enabled on data sheets
- Bold header rows
- Empty results produce valid workbook with zero-count metrics
- Fail-closed: partial `.tmp` file deleted on error

### Test Approach
- Use ClosedXML to read back the generated workbook in tests
- Create `ExcelExportAdapterTests` in `STIGForge.UnitTests/Export/`
- Add ClosedXML to test project for read-back verification

---

*Phase: 18-excel-compliance-report*
*Research completed: 2026-02-19*

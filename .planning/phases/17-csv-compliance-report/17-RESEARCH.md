# Phase 17: CSV Compliance Report - Research

**Researched:** 2026-02-19
**Confidence:** HIGH across all domains

## Domain 1: IExportAdapter Pattern (from Phase 15/16)

**Confidence: HIGH**

The CSV export adapter follows the identical pattern established in Phase 15 and used in Phase 16:

### Interface Contract
```csharp
public interface IExportAdapter
{
    string FormatName { get; }
    string[] SupportedExtensions { get; }
    Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct);
}
```

### ExportAdapterRequest Fields
- `BundleRoot` (string) — bundle path, used to derive system name
- `Results` (IReadOnlyList<ControlResult>) — verify results to export
- `OutputDirectory` (string) — where to write output
- `FileNameStem` (string?) — optional filename override
- `Options` (IReadOnlyDictionary<string, string>) — can carry system-name

### ControlResult Properties (from VerifyModels.cs)
```
VulnId, RuleId, Title, Severity, Status, FindingDetails, Comments, Tool, SourceFile, VerifiedAt
```

### Fail-Closed Write Pattern (from XccdfExportAdapter)
1. Write to `{output}.tmp`
2. On success: delete existing target, `File.Move(tmp, output)`
3. On exception: delete tmp file in catch block

## Domain 2: CSV Generation (RFC 4180)

**Confidence: HIGH**

### RFC 4180 Rules
1. Each record on its own line, terminated by CRLF
2. Optional header line with same format as records
3. Fields separated by commas
4. Fields containing commas, double quotes, or line breaks MUST be enclosed in double quotes
5. Double quotes within fields escaped as `""`

### Implementation Approach
- Use `StreamWriter` with explicit `\r\n` line endings for RFC 4180 compliance
- UTF-8 encoding without BOM (standard for modern tools)
- Helper method `EscapeCsvField(string?)` handles quoting logic
- No external NuGet dependencies needed — CSV is simple enough for manual generation

### Column Mapping (ControlResult → CSV)
| CSV Header | Source | Notes |
|------------|--------|-------|
| System Name | Options["system-name"] or Path.GetFileName(BundleRoot) | Derived |
| Vulnerability ID | VulnId | Direct |
| Rule ID | RuleId | Direct |
| STIG Title | Title | Direct |
| Severity | Severity | Direct (high/medium/low) |
| CAT Level | Severity → mapped | high→CAT I, medium→CAT II, low→CAT III |
| Status | Status | Direct |
| Finding Details | FindingDetails | May contain newlines — must escape |
| Comments | Comments | May contain newlines — must escape |
| Remediation Priority | Severity → mapped | Same as CAT Level (CAT I = highest) |
| Tool | Tool | Direct |
| Source File | SourceFile | Direct |
| Verified At | VerifiedAt?.ToString("o") | ISO 8601 format |

## Domain 3: CLI Integration Pattern (from ExportCommands.cs)

**Confidence: HIGH**

### Existing Pattern (export-xccdf)
```csharp
private static void RegisterExportXccdf(RootCommand rootCmd, Func<IHost> buildHost)
{
    var cmd = new Command("export-xccdf", "description");
    var bundleOpt = new Option<string>("--bundle", ...) { IsRequired = true };
    var outOpt = new Option<string>("--output", ...);
    var fileNameOpt = new Option<string>("--file-name", ...);
    // ... handler loads results from Verify/consolidated-results.json
}
```

### Phase 17 Addition
- Add `RegisterExportCsv(rootCmd, buildHost)` to `Register()` method
- Add `--system-name` option (optional, defaults to bundle dir name)
- Pass system-name via `ExportAdapterRequest.Options` dictionary

## Domain 4: Test Strategy

**Confidence: HIGH**

### Test Categories (following XccdfExportAdapterTests pattern)
1. FormatName and SupportedExtensions identity tests
2. Export produces non-empty CSV with correct header row
3. CSV escaping: commas, quotes, newlines properly handled
4. Empty results produce header-only CSV
5. Null fields written as empty strings
6. Fail-closed: partial file deleted on error
7. System name derivation (from options and from bundle path)
8. CAT level mapping correctness

### Property-Based Escape Testing
- Generate strings with embedded commas → verify field is quoted
- Generate strings with embedded quotes → verify doubled
- Generate strings with embedded newlines → verify field is quoted

## Key Findings

1. **No new NuGet dependencies** — CSV generation is manual string building
2. **ControlResult has no "system name" field** — must derive from BundleRoot or CLI option
3. **ControlResult has no "remediation priority" field** — derive from Severity (same as CAT Level)
4. **FindingDetails and Comments may contain newlines** — these are the primary escaping concern
5. **ExportAdapterRequest.Options dictionary** is the correct place to pass system-name through
6. **net48 compatibility** — same constraints as Phase 16 (no File.Move overwrite param)

## RESEARCH COMPLETE

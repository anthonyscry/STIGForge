# Phase 16: XCCDF Result Export - Research

**Researched:** 2026-02-19
**Domain:** XCCDF 1.2 XML generation from ControlResult data, IExportAdapter implementation
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- XCCDF 1.2 namespace (`http://checklists.nist.gov/xccdf/1.2`) must be on every element (from success criteria)
- Round-trip test via `ScapResultAdapter.CanHandle()` must pass (from success criteria)
- Partial output file deleted on adapter throw (from success criteria)
- Must implement `IExportAdapter` interface from Phase 15

### Claude's Discretion
- **XCCDF content mapping** — which ControlResult fields map to which XCCDF 1.2 elements; what benchmark metadata and system info to include vs omit
- **CLI invocation design** — flags, output path defaults, overwrite behavior for the `export-xccdf` command
- **Downstream tool compatibility** — prioritization between Tenable, ACAS, STIG Viewer, OpenRMF; strict XCCDF 1.2 schema compliance vs practical compatibility tradeoffs
- **Output file naming** — default filename pattern and directory placement
- **Error handling** — fail-closed behavior details (partial file cleanup, error message format)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| EXP-01 | Operator can export verify results as XCCDF 1.2 XML for tool interop (Tenable, ACAS, STIG Viewer) | XccdfExportAdapter implements IExportAdapter; generates Benchmark/TestResult XML with correct namespace; round-trip validated against ScapResultAdapter |
</phase_requirements>

## Summary

Phase 16 implements the reverse of `ScapResultAdapter`: instead of parsing XCCDF 1.2 XML into `NormalizedVerifyResult`, we generate XCCDF 1.2 XML from `ControlResult` data. The existing `ScapResultAdapter` in `STIGForge.Verify/Adapters/ScapResultAdapter.cs` serves as the authoritative contract — the exported XML must pass `CanHandle()` and produce the same result count when re-parsed.

The implementation is self-contained: one new `XccdfExportAdapter` class in `STIGForge.Export`, one CLI command registration in `ExportCommands.cs`, and unit tests. No external dependencies are needed — `System.Xml.Linq` (already used throughout the codebase) handles all XML generation.

**Primary recommendation:** Build `XccdfExportAdapter` by reverse-engineering `ScapResultAdapter.ParseResults()` — every element the parser reads must be written by the exporter, ensuring round-trip fidelity.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Xml.Linq | (built-in) | XDocument/XElement XML generation | Already used by ScapResultAdapter, CklExporter, CklAdapter; zero new dependencies |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Globalization | (built-in) | DateTimeOffset formatting with CultureInfo.InvariantCulture | Timestamp formatting in XCCDF time elements |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| XDocument | XmlWriter | XmlWriter is faster for large docs but XDocument matches existing codebase patterns |
| Manual XML | XSD-generated classes | Would guarantee schema compliance but adds complexity for a single document type |

**No new NuGet packages required.** All XML generation uses built-in .NET libraries already referenced by the project.

## Architecture Patterns

### Recommended Project Structure
```
src/STIGForge.Export/
├── XccdfExportAdapter.cs         # IExportAdapter implementation (new)
├── CklExportAdapter.cs           # Existing pattern to follow
├── ExportModels.cs               # IExportAdapter, ExportAdapterRequest/Result (existing)
├── ExportAdapterRegistry.cs      # Registration (existing, add XCCDF)
└── ExportOrchestrator.cs         # Dispatch (existing, no changes)

src/STIGForge.Cli/Commands/
└── ExportCommands.cs             # Add RegisterExportXccdf (existing file)

tests/STIGForge.UnitTests/Export/
└── XccdfExportAdapterTests.cs    # Round-trip + structure tests (new)
```

### Pattern 1: IExportAdapter Implementation
**What:** Follow CklExportAdapter pattern — implement `IExportAdapter` with `FormatName`, `SupportedExtensions`, `ExportAsync`
**When to use:** Every new export format
**Example from CklExportAdapter:**
```csharp
public sealed class XccdfExportAdapter : IExportAdapter
{
    public string FormatName => "XCCDF";
    public string[] SupportedExtensions => new[] { ".xml" };

    public async Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        // Build XCCDF XML from request.Results
        // Write to output file
        // Return ExportAdapterResult with Success/OutputPaths
    }
}
```

### Pattern 2: Fail-Closed with Partial File Cleanup
**What:** Write to temp file, rename on success, delete on failure
**When to use:** Required by success criteria — partial output must be deleted on adapter throw
**Example:**
```csharp
var tempPath = outputPath + ".tmp";
try
{
    doc.Save(tempPath);
    File.Move(tempPath, outputPath, overwrite: true);
}
catch
{
    if (File.Exists(tempPath)) File.Delete(tempPath);
    throw;
}
```

### Pattern 3: Round-Trip Validation Contract
**What:** The exported XML must pass `ScapResultAdapter.CanHandle()` and re-parse to the same result count
**When to use:** Test verification for this adapter
**Key insight:** `ScapResultAdapter.CanHandle()` checks:
1. File extension is `.xml`
2. Root element is `Benchmark` with namespace `http://checklists.nist.gov/xccdf/1.2` OR `TestResult` with same namespace

`ScapResultAdapter.ParseResults()` reads:
- `TestResult` descendant element
- `TestResult/@version` attribute
- `start-time` and `end-time` child elements
- `rule-result` child elements with:
  - `@idref` attribute (RuleId)
  - `@time` attribute
  - `@weight` attribute (maps to severity)
  - `result` child element (pass/fail/etc)
  - `ident` child elements
  - `check` > `check-content-ref` child elements
  - `message` child elements

### Anti-Patterns to Avoid
- **Namespace omission:** Every `new XElement(...)` must use `XccdfNs + "elementName"`, not just `"elementName"`. Missing namespace silently breaks all downstream tools.
- **Hardcoded file paths:** Use `request.OutputDirectory` and `request.FileNameStem` from `ExportAdapterRequest`.
- **Ignoring null fields:** `ControlResult` fields are nullable — guard every field mapping.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| XML generation | String concatenation | XDocument/XElement | Handles escaping, encoding, namespace prefixes automatically |
| Severity-to-weight mapping | Custom logic | Reverse of `ScapResultAdapter.MapWeightToSeverity()` | Must round-trip correctly: high→10.0, medium→5.0, low→1.0 |
| Status mapping | New mapping | Reverse of `ScapResultAdapter.MapScapStatus()` | Must produce values that parse back to the same VerifyStatus |
| Timestamp formatting | Custom format | DateTimeOffset.ToString("o") | ISO 8601 round-trip format, matches what ScapResultAdapter.ParseTimestamp() expects |

**Key insight:** Every mapping in XccdfExportAdapter must be the exact inverse of ScapResultAdapter's parsing logic to guarantee round-trip fidelity.

## Common Pitfalls

### Pitfall 1: Namespace Missing on Child Elements
**What goes wrong:** Root element has XCCDF namespace but child elements don't, creating mixed-namespace XML that tools reject
**Why it happens:** `new XElement("rule-result", ...)` creates element in no namespace; must use `XccdfNs + "rule-result"`
**How to avoid:** Define `static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2"` and use it on EVERY element
**Warning signs:** XML output has `xmlns=""` attributes on child elements

### Pitfall 2: Status Value Mismatch
**What goes wrong:** Exported status string doesn't match what ScapResultAdapter expects, breaking round-trip
**Why it happens:** ScapResultAdapter normalizes aggressively (strips underscores, hyphens, spaces, lowercases). The export must produce the canonical XCCDF values.
**How to avoid:** Map VerifyStatus enum to XCCDF canonical values: `pass`, `fail`, `notapplicable`, `notchecked`, `informational`, `error`
**Warning signs:** Round-trip test shows status mismatch

### Pitfall 3: Empty Results Producing Invalid XML
**What goes wrong:** Zero results produces a TestResult element with no rule-result children, which may confuse downstream tools
**Why it happens:** No guard for empty result set
**How to avoid:** Still produce valid XML structure with zero rule-results — this is valid per XCCDF 1.2 spec and ScapResultAdapter handles it (returns empty results array)

### Pitfall 4: ControlResult vs NormalizedVerifyResult Field Mismatch
**What goes wrong:** ControlResult lacks fields that NormalizedVerifyResult has (e.g., Metadata dictionary with `rule_id`, `cce_id`)
**Why it happens:** ControlResult is a simpler model: VulnId, RuleId, Title, Severity, Status, FindingDetails, Comments, Tool, SourceFile, VerifiedAt
**How to avoid:** Map available ControlResult fields; use RuleId for `idref`, Status for `result`, Severity for `weight`, FindingDetails for `message`

## Code Examples

### XCCDF Document Structure (what must be generated)
```xml
<?xml version="1.0" encoding="utf-8"?>
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2">
  <TestResult version="1.0">
    <start-time>2026-02-19T10:00:00.0000000+00:00</start-time>
    <end-time>2026-02-19T10:05:00.0000000+00:00</end-time>
    <rule-result idref="SV-220697r569187_rule" time="2026-02-19T10:05:00Z" weight="10.0">
      <result>pass</result>
      <ident system="http://cyber.mil/cci">V-220697</ident>
      <message>Finding details here</message>
    </rule-result>
  </TestResult>
</Benchmark>
```

### VerifyStatus to XCCDF Status Mapping (reverse of ScapResultAdapter)
```csharp
private static string MapStatusToXccdf(string? status)
{
    // ControlResult.Status is a string, not VerifyStatus enum
    // Map to canonical XCCDF values that ScapResultAdapter.MapScapStatus() understands
    var normalized = (status ?? "").Trim().ToLowerInvariant();
    return normalized switch
    {
        "pass" or "notafinding" => "pass",
        "fail" or "open" => "fail",
        "notapplicable" or "na" => "notapplicable",
        "notreviewed" or "notchecked" => "notchecked",
        "informational" => "informational",
        "error" => "error",
        _ => "unknown"
    };
}
```

### Severity to Weight Mapping (reverse of ScapResultAdapter.MapWeightToSeverity)
```csharp
private static string MapSeverityToWeight(string? severity)
{
    return (severity ?? "").Trim().ToLowerInvariant() switch
    {
        "high" => "10.0",
        "medium" => "5.0",
        "low" => "1.0",
        _ => "0.0"
    };
}
```

### CLI Registration Pattern (from existing ExportCommands.cs)
```csharp
private static void RegisterExportXccdf(RootCommand rootCmd, Func<IHost> buildHost)
{
    var cmd = new Command("export-xccdf", "Export verify results as XCCDF 1.2 XML");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Output directory override");
    cmd.AddOption(bundleOpt); cmd.AddOption(outOpt);
    // Handler loads consolidated-results.json, creates XccdfExportAdapter, calls ExportAsync
    rootCmd.AddCommand(cmd);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Direct XML string building | XDocument with namespaced XElements | .NET Core era | Type-safe, auto-escaped XML |
| Per-format CLI plumbing | IExportAdapter + ExportOrchestrator | Phase 15 (2026-02-19) | Unified export dispatch; new formats just implement interface |

**Project-specific:**
- Phase 15 established IExportAdapter pattern — XccdfExportAdapter is the first format-specific adapter built on this foundation
- `ScapResultAdapter` already validates the exact XML structure we must produce — use it as the test oracle

## Open Questions

1. **ControlResult.Status string values**
   - What we know: ControlResult.Status is `string?`, not VerifyStatus enum. The bridge in Phase 14 maps VerifyStatus to string.
   - What's unclear: Exact string values stored in consolidated-results.json (likely "Pass", "Fail", "NotApplicable", etc. from VerifyStatus.ToString())
   - Recommendation: Map case-insensitively, matching ScapResultAdapter's normalization logic

2. **Benchmark metadata beyond TestResult**
   - What we know: ScapResultAdapter.CanHandle() accepts both `Benchmark` root and standalone `TestResult` root
   - What's unclear: Whether downstream tools (STIG Viewer, Tenable) require Benchmark wrapper or accept standalone TestResult
   - Recommendation: Use `Benchmark` as root (safer for tool compatibility), with `TestResult` child. This passes CanHandle() and is the standard XCCDF 1.2 structure.

## Sources

### Primary (HIGH confidence)
- `src/STIGForge.Verify/Adapters/ScapResultAdapter.cs` — definitive XCCDF 1.2 parsing contract; every element read here must be written by the exporter
- `src/STIGForge.Export/ExportModels.cs` — IExportAdapter interface, ExportAdapterRequest/Result models
- `src/STIGForge.Export/CklExportAdapter.cs` — reference IExportAdapter implementation pattern
- `src/STIGForge.Export/ExportAdapterRegistry.cs` — adapter registration mechanism
- `src/STIGForge.Cli/Commands/ExportCommands.cs` — CLI command registration pattern
- `src/STIGForge.Verify/VerifyModels.cs` — ControlResult model (input data for export)

### Secondary (MEDIUM confidence)
- XCCDF 1.2 specification (NIST SP 800-126 Rev 3) — namespace `http://checklists.nist.gov/xccdf/1.2` confirmed in ScapResultAdapter

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — uses only built-in .NET XML libraries already in the project
- Architecture: HIGH — follows established IExportAdapter pattern from Phase 15; ScapResultAdapter provides exact structural contract
- Pitfalls: HIGH — all pitfalls identified from actual code analysis of ScapResultAdapter parsing logic

**Research date:** 2026-02-19
**Valid until:** 2026-03-19 (stable — internal codebase patterns, no external dependency churn)

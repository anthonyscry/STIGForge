# STIGForge Result Integration & Checklist Export Analysis

**Date:** 2026-02-26  
**Scope:** Evaluate-STIG + SCC (SCAP) result consolidation and CKL/CKLB export  
**Status:** Current implementation review (read-only)

---

## Executive Summary

STIGForge implements a **unified result consolidation pipeline** that:
1. **Parses** multiple verification sources (SCAP, Evaluate-STIG, Manual CKL) via adapter pattern
2. **Normalizes** to common `NormalizedVerifyResult` schema
3. **Merges** with conflict resolution (Manual CKL > Evaluate-STIG > SCAP precedence)
4. **Exports** to CKL/CKLB with **per-benchmark iSTIG elements** in a single CHECKLIST

**Key Finding:** The current export produces a **merged checklist with multiple iSTIG sections** (one per bundle/benchmark), which is **STIG Viewer compatible** but requires validation for multi-benchmark import behavior.

---

## 1) Key Classes & Methods for Result Consolidation

### 1.1 Orchestration & Merging

| File | Class | Method | Line | Purpose |
|------|-------|--------|------|---------|
| `VerifyOrchestrator.cs` | `VerifyOrchestrator` | `ParseAndMergeResults()` | 43-72 | Entry point: parse multiple result files and merge into consolidated report |
| `VerifyOrchestrator.cs` | `VerifyOrchestrator` | `MergeReports()` | 135-219 | Core merge logic: groups results by control ID, applies reconciliation |
| `VerifyOrchestrator.cs` | `VerifyOrchestrator` | `ReconcileResults()` | 225-328 | Conflict resolution: applies precedence rules, merges metadata/evidence |
| `VerifyOrchestrator.cs` | `VerifyOrchestrator` | `ApplyMappingManifest()` | 78-126 | Enriches results with BenchmarkId from ScapMappingManifest |
| `VerifyOrchestrator.cs` | `ConsolidatedVerifyReport` | (class) | 467-475 | Output model: merged results + conflicts + diagnostics |

### 1.2 Adapter Pattern (Tool-Specific Parsing)

| File | Class | Method | Line | Purpose |
|------|-------|--------|------|---------|
| `Adapters/IVerifyResultAdapter.cs` | `IVerifyResultAdapter` | `ParseResults()` | 17 | Interface: convert tool output to NormalizedVerifyReport |
| `Adapters/EvaluateStigAdapter.cs` | `EvaluateStigAdapter` | `ParseResults()` | 41-108 | Parse Evaluate-STIG XML → NormalizedVerifyResult[] |
| `Adapters/EvaluateStigAdapter.cs` | `EvaluateStigAdapter` | `ParseCheckElement()` | 110-153 | Extract VulnId, RuleId, Status, FindingDetails from XML |
| `Adapters/ScapResultAdapter.cs` | `ScapResultAdapter` | `ParseResults()` | 41-107 | Parse XCCDF TestResult → NormalizedVerifyResult[] |
| `Adapters/CklAdapter.cs` | `CklAdapter` | `ParseResults()` | 34-77 | Parse CKL VULN elements → NormalizedVerifyResult[] |

### 1.3 Result Persistence & Loading

| File | Class | Method | Line | Purpose |
|------|-------|--------|------|---------|
| `VerifyReportWriter.cs` | `VerifyReportWriter` | `WriteJson()` | 29-33 | Serialize VerifyReport to consolidated-results.json |
| `VerifyReportReader.cs` | `VerifyReportReader` | `LoadFromJson()` | 7-19 | Deserialize consolidated-results.json → VerifyReport |
| `VerificationArtifactAggregationService.cs` | `VerificationArtifactAggregationService` | `WriteCoverageArtifacts()` | 31-87 | Aggregate multiple reports into coverage summaries |

### 1.4 Export to CKL/CKLB

| File | Class | Method | Line | Purpose |
|------|-------|--------|------|---------|
| `CklExporter.cs` | `CklExporter` | `ExportCkl()` | 18-77 | Main export: load results, build XML, write CKL/CKLB |
| `CklExporter.cs` | `CklExporter` | `BuildCklDocument()` | 120-172 | Construct CHECKLIST XML with multiple iSTIG sections |
| `CklExporter.cs` | `CklExporter` | `BuildVulnElements()` | 174-199 | Convert ControlResult[] → VULN XML elements |
| `CklExporter.cs` | `CklExporter` | `LoadResultsForBundle()` | 221-256 | Load consolidated-results.json from bundle, dedup by control key |
| `CklExporter.cs` | `CklExporter` | `WriteChecklistFile()` | 100-118 | Write CKL or CKLB (ZIP with embedded CKL) |

---

## 2) Data Model for Unified Finding Rows

### 2.1 Normalized Result Schema

**File:** `NormalizedVerifyResult.cs` (lines 7-65)

```csharp
public sealed class NormalizedVerifyResult
{
  public string ControlId { get; set; }           // VulnId preferred, fallback to RuleId
  public string? VulnId { get; set; }             // e.g., V-220697
  public string? RuleId { get; set; }             // e.g., SV-220697r569187_rule
  public string? Title { get; set; }              // Control title
  public string? Severity { get; set; }           // high/medium/low
  public VerifyStatus Status { get; set; }        // Pass/Fail/NotApplicable/NotReviewed/Informational/Error
  public string? FindingDetails { get; set; }     // Evidence text
  public string? Comments { get; set; }           // Reviewer notes
  public string Tool { get; set; }                // SCAP/Evaluate-STIG/Manual CKL/Merged
  public string SourceFile { get; set; }          // Path to original tool output
  public DateTimeOffset? VerifiedAt { get; set; } // Verification timestamp
  public IReadOnlyList<string> EvidencePaths { get; set; }  // Supporting files
  public IReadOnlyDictionary<string, string> Metadata { get; set; }  // Tool-specific metadata
  public string? RawArtifactPath { get; set; }    // Provenance: link to raw tool output
  public string? BenchmarkId { get; set; }        // SCAP benchmark ID (from manifest)
}
```

**Status Enum:** `VerifyStatus` (lines 71-93)
- `Pass` (1) - NotAFinding, pass, Compliant
- `Fail` (2) - Open, fail, NonCompliant
- `NotApplicable` (3)
- `NotReviewed` (4) - notchecked, NotReviewed
- `Informational` (5)
- `Error` (6)

### 2.2 Consolidated Report Model

**File:** `VerifyOrchestrator.cs` (lines 467-498)

```csharp
public sealed class ConsolidatedVerifyReport
{
  public DateTimeOffset MergedAt { get; set; }
  public IReadOnlyList<SourceReportInfo> SourceReports { get; set; }  // Tools used
  public IReadOnlyList<NormalizedVerifyResult> Results { get; set; }   // Merged findings
  public VerifySummary Summary { get; set; }                           // Pass/Fail counts
  public IReadOnlyList<ResultConflict> Conflicts { get; set; }         // Reconciliation log
  public IReadOnlyList<string> DiagnosticMessages { get; set; }        // Errors/warnings
}

public sealed class ResultConflict
{
  public string ControlId { get; set; }
  public VerifyStatus ResolvedStatus { get; set; }
  public IReadOnlyList<ConflictingResult> ConflictingResults { get; set; }
  public string ResolutionReason { get; set; }  // e.g., "Applied precedence: Manual CKL..."
}
```

### 2.3 Export-Layer Model

**File:** `VerifyModels.cs` (lines 3-34)

```csharp
public sealed class ControlResult
{
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public string? Severity { get; set; }
  public string? Status { get; set; }            // String status (not enum)
  public string? FindingDetails { get; set; }
  public string? Comments { get; set; }
  public string Tool { get; set; }
  public string SourceFile { get; set; }
  public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class VerifyReport
{
  public string Tool { get; set; }
  public string ToolVersion { get; set; }
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public string OutputRoot { get; set; }
  public IReadOnlyList<ControlResult> Results { get; set; }
}
```

**Note:** `ControlResult` is the JSON-serialized form stored in `consolidated-results.json`.

---

## 3) CKL/CKLB Export Paths & Multi-Benchmark Handling

### 3.1 Export Request Model

**File:** `CklExporter.cs` (lines 335-347)

```csharp
public sealed class CklExportRequest
{
  public string BundleRoot { get; set; }                    // Single bundle
  public IReadOnlyList<string>? BundleRoots { get; set; }   // Multiple bundles
  public string? OutputDirectory { get; set; }
  public string? FileName { get; set; }
  public string? HostName { get; set; }
  public string? HostIp { get; set; }
  public string? HostMac { get; set; }
  public string? StigId { get; set; }                       // Override STIG ID
  public CklFileFormat FileFormat { get; set; }             // Ckl or Cklb
  public bool IncludeCsv { get; set; }
}
```

### 3.2 Export Flow

**File:** `CklExporter.cs` (lines 18-77)

```
ExportCkl(request)
  ├─ ResolveBundleRoots(request)           [lines 79-98]
  │   └─ Merge BundleRoot + BundleRoots into list
  │
  ├─ LoadResults(bundleRoots)              [lines 208-219]
  │   └─ For each bundle:
  │       └─ LoadResultsForBundle()        [lines 221-256]
  │           ├─ Find Verify/*/consolidated-results.json
  │           ├─ Load VerifyReport via VerifyReportReader
  │           └─ Dedup by control key (VulnId > RuleId > Title)
  │
  ├─ BuildCklDocument(resultSets)          [lines 120-172]
  │   └─ Create CHECKLIST with:
  │       ├─ ASSET (host info)
  │       └─ STIGS
  │           └─ For each resultSet (per bundle):
  │               └─ iSTIG
  │                   ├─ STIG_INFO (stigid, title, releaseinfo)
  │                   └─ VULN[] (from BuildVulnElements)
  │
  └─ WriteChecklistFile()                  [lines 100-118]
      ├─ If CKLB: ZIP with embedded CKL
      └─ If CKL: Direct XML write
```

### 3.3 Multi-Benchmark Output Structure

**Current behavior (lines 127-153):**

```xml
<CHECKLIST>
  <ASSET>
    <HOST_NAME>...</HOST_NAME>
    ...
  </ASSET>
  <STIGS>
    <iSTIG>                          <!-- Bundle 1 -->
      <STIG_INFO>
        <SI_DATA>
          <SID_NAME>stigid</SID_NAME>
          <SID_DATA>STIG_ID_1</SID_DATA>
        </SI_DATA>
        <SI_DATA>
          <SID_NAME>title</SID_NAME>
          <SID_DATA>Bundle 1 Title</SID_DATA>
        </SI_DATA>
      </STIG_INFO>
      <VULN>...</VULN>
      <VULN>...</VULN>
    </iSTIG>
    <iSTIG>                          <!-- Bundle 2 -->
      <STIG_INFO>
        <SI_DATA>
          <SID_NAME>stigid</SID_NAME>
          <SID_DATA>STIG_ID_2</SID_DATA>
        </SI_DATA>
        ...
      </STIG_INFO>
      <VULN>...</VULN>
      <VULN>...</VULN>
    </iSTIG>
  </STIGS>
</CHECKLIST>
```

**Key Points:**
- ✅ **Single CHECKLIST** (not per-benchmark)
- ✅ **Multiple iSTIG sections** (one per bundle/benchmark)
- ✅ **Shared ASSET** (single host info)
- ✅ **Merged VULN elements** (all findings in one document)

---

## 4) Result Consolidation Logic

### 4.1 Merge Algorithm

**File:** `VerifyOrchestrator.cs` (lines 135-219)

**Step 1: Group by Control ID** (lines 145-152)
```csharp
var grouped = indexedResults
  .GroupBy(x => BuildGroupKey(x.Result, x.Index), StringComparer.OrdinalIgnoreCase)
  .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
  .ToList();
```

**Step 2: Reconcile Conflicts** (lines 157-178)
```csharp
foreach (var group in grouped)
{
  if (controlResults.Count == 1)
    mergedResults.Add(controlResults[0]);  // No conflict
  else
    var reconciled = ReconcileResults(controlId, controlResults, out var conflict);
    mergedResults.Add(reconciled);
}
```

### 4.2 Conflict Resolution Precedence

**File:** `VerifyOrchestrator.cs` (lines 225-328)

**Precedence Order (highest to lowest):**

1. **Manual CKL** (precedence = 3)
   - Human review is authoritative
   - Overrides automated tools

2. **Evaluate-STIG** (precedence = 2)
   - PowerShell-based automation
   - More reliable than SCAP

3. **SCAP** (precedence = 1)
   - Fully automated
   - Lowest precedence

**Tiebreaker Rules** (lines 230-237):
```csharp
var sorted = results
  .OrderByDescending(r => GetToolPrecedence(r.Tool))      // Tool precedence
  .ThenByDescending(r => r.VerifiedAt ?? DateTimeOffset.MinValue)  // Latest timestamp
  .ThenByDescending(r => GetStatusSeverity(r.Status))     // Fail > Pass
  .ThenBy(r => r.Tool, StringComparer.OrdinalIgnoreCase)  // Deterministic
  .ThenBy(r => r.SourceFile, StringComparer.OrdinalIgnoreCase)
  .ThenBy(r => r.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
  .ThenBy(r => r.VulnId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
  .ToList();
```

**Status Severity** (lines 342-355):
```csharp
Fail (5) > Error (4) > Pass (3) > NotReviewed (2) > NotApplicable (1) > Informational (0)
```

### 4.3 Metadata & Evidence Merging

**File:** `VerifyOrchestrator.cs` (lines 273-310)

- **Metadata:** Prefixed with tool name (e.g., `scap_check_id`, `evaluate_stig_test_id`)
- **Raw Artifact Paths:** Joined with `;` for provenance
- **Evidence Paths:** Deduplicated and sorted
- **Comments:** Merged with `\n---\n` separator

### 4.4 Conflict Logging

**File:** `VerifyOrchestrator.cs` (lines 255-271)

Conflicts are recorded when:
- Multiple tools report **different statuses** (Pass vs Fail)
- Resolution reason includes tool precedence and timestamp

Example:
```
ControlId: V-220697
ResolvedStatus: Pass
ResolutionReason: "Applied precedence: Manual CKL (verified 2026-02-26 14:30) overrides SCAP, Evaluate-STIG"
ConflictingResults: [
  { Tool: "SCAP", Status: Fail, VerifiedAt: 2026-02-26 14:00 },
  { Tool: "Evaluate-STIG", Status: Fail, VerifiedAt: 2026-02-26 14:15 }
]
```

---

## 5) Existing Logic for Appending Findings

### 5.1 Per-Bundle Result Loading

**File:** `CklExporter.cs` (lines 221-256)

```csharp
private static BundleChecklistResultSet LoadResultsForBundle(string bundleRoot)
{
  var set = new BundleChecklistResultSet { BundleRoot = bundleRoot };
  
  TryReadBundleManifest(bundleRoot, out var packId, out var packName);
  set.PackId = packId;
  set.PackName = packName;
  
  var verifyRoot = Path.Combine(bundleRoot, "Verify");
  var reports = Directory.GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories)
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();
  
  var dedup = new Dictionary<string, ControlResult>(StringComparer.OrdinalIgnoreCase);
  foreach (var reportPath in reports)
  {
    var report = VerifyReportReader.LoadFromJson(reportPath);
    foreach (var result in report.Results)
    {
      var key = BuildControlKey(result);
      dedup[key] = result;  // Last one wins (deterministic due to OrderBy)
    }
  }
  
  set.Results = dedup.Values
    .OrderBy(r => r.VulnId ?? r.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
    .ThenBy(r => r.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
    .ToList();
  return set;
}
```

**Key Behavior:**
- ✅ Loads **all** `consolidated-results.json` files from bundle
- ✅ Deduplicates by control key (VulnId > RuleId > Title)
- ✅ Last-write-wins for same control (deterministic due to OrderBy)
- ✅ Sorts output for deterministic export

### 5.2 Multi-Bundle Consolidation

**File:** `CklExporter.cs` (lines 208-219)

```csharp
private static List<BundleChecklistResultSet> LoadResults(IReadOnlyList<string> bundleRoots)
{
  var sets = new List<BundleChecklistResultSet>();
  foreach (var bundleRoot in bundleRoots)
  {
    var set = LoadResultsForBundle(bundleRoot);
    if (set.Results.Count > 0)
      sets.Add(set);
  }
  return sets;
}
```

**Behavior:**
- ✅ Processes each bundle independently
- ✅ Preserves bundle identity (PackId, PackName)
- ✅ Creates separate iSTIG section per bundle in output

### 5.3 VULN Element Construction

**File:** `CklExporter.cs` (lines 174-199)

```csharp
private static object[] BuildVulnElements(IReadOnlyList<ControlResult> results)
{
  var elements = new List<object>(results.Count);
  foreach (var r in results)
  {
    var vuln = new XElement("VULN",
      StigData("Vuln_Num", r.VulnId ?? string.Empty),
      StigData("Severity", r.Severity ?? "medium"),
      StigData("Rule_ID", r.RuleId ?? string.Empty),
      StigData("Rule_Title", r.Title ?? string.Empty),
      // ... more STIG_DATA elements ...
      new XElement("STATUS", ExportStatusMapper.MapToCklStatus(r.Status)),
      new XElement("FINDING_DETAILS", r.FindingDetails ?? string.Empty),
      new XElement("COMMENTS", r.Comments ?? string.Empty),
      // ... severity override, justification ...
    );
    elements.Add(vuln);
  }
  return elements.ToArray();
}
```

**Status Mapping** (via `ExportStatusMapper.cs`):
- `VerifyStatus.Pass` → `NotAFinding`
- `VerifyStatus.Fail` → `Open`
- `VerifyStatus.NotApplicable` → `Not_Applicable`
- `VerifyStatus.Error` → `Open`
- Others → `Not_Reviewed`

---

## 6) Gaps to Achieve 'Merged Checklist Importable in STIG Viewer'

### 6.1 STIG Viewer Compatibility Issues

| Gap | Severity | Impact | Notes |
|-----|----------|--------|-------|
| **Multiple iSTIG sections in single CHECKLIST** | ⚠️ Medium | STIG Viewer may not handle multiple benchmarks in one file | Viewer typically expects 1 iSTIG per CHECKLIST; multi-benchmark support untested |
| **No explicit benchmark-to-VULN mapping** | ⚠️ Medium | Viewer cannot distinguish which VULN belongs to which benchmark | VULNs lack benchmark context; only iSTIG parent provides it |
| **Missing SCAP benchmark ID in VULN** | ⚠️ Low | Cannot trace VULN back to SCAP benchmark programmatically | BenchmarkId exists in NormalizedVerifyResult but not exported to CKL |
| **No per-benchmark CKL option** | ⚠️ Medium | Cannot export separate CKL per benchmark if needed | Current design always merges into single CHECKLIST |
| **Metadata not exported to CKL** | ⚠️ Low | Tool provenance and conflict info lost in export | Metadata exists in ControlResult but not written to CKL XML |
| **No validation of multi-benchmark CKL** | ⚠️ High | Unknown if STIG Viewer can import/edit multi-benchmark CKL | No integration tests with actual STIG Viewer |

### 6.2 Recommended Fixes

#### Fix 1: Validate Multi-Benchmark CKL with STIG Viewer
**Priority:** HIGH  
**Effort:** Medium  
**Action:**
1. Export test CKL with 2+ benchmarks
2. Import into STIG Viewer
3. Verify all VULNs load correctly
4. Test edit/save round-trip

**Test Location:** `tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs`

#### Fix 2: Add Per-Benchmark Export Option
**Priority:** MEDIUM  
**Effort:** Medium  
**Action:**
1. Add `ExportPerBenchmark` flag to `CklExportRequest`
2. Modify `BuildCklDocument()` to create separate CHECKLIST per benchmark
3. Update `WriteChecklistFile()` to handle multiple output files
4. Add tests for per-benchmark export

**Files to Modify:**
- `CklExporter.cs` (lines 18-77, 120-172)
- `CklExportRequest` (line 335)

#### Fix 3: Export Benchmark ID to CKL Metadata
**Priority:** LOW  
**Effort:** Low  
**Action:**
1. Add custom STIG_DATA element for BenchmarkId (if not standard)
2. Or store in COMMENTS field with prefix (e.g., `[SCAP:cce-1.2.3]`)
3. Update `BuildVulnElements()` (line 174)

#### Fix 4: Add Metadata Export Option
**Priority:** LOW  
**Effort:** Medium  
**Action:**
1. Add `IncludeMetadata` flag to `CklExportRequest`
2. Serialize metadata to JSON and embed in COMMENTS or separate file
3. Update CSV export to include metadata columns

#### Fix 5: Add Integration Tests with STIG Viewer
**Priority:** HIGH  
**Effort:** High  
**Action:**
1. Create test fixture that exports CKL
2. Invoke STIG Viewer CLI (if available) to validate
3. Test round-trip: export → import → verify

**Test Location:** `tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs`

---

## 7) Current Export Behavior Summary

### 7.1 Single Bundle Export

**Input:**
```
bundle/
  Verify/
    run-1/
      consolidated-results.json (SCAP + Evaluate-STIG merged)
```

**Output:**
```
bundle/Export/
  stigforge_checklist.ckl
  stigforge_checklist.csv (optional)
```

**CKL Structure:**
```xml
<CHECKLIST>
  <ASSET>...</ASSET>
  <STIGS>
    <iSTIG>
      <STIG_INFO>
        <SI_DATA><SID_NAME>stigid</SID_NAME><SID_DATA>STIG_ID</SID_DATA></SI_DATA>
        <SI_DATA><SID_NAME>title</SID_NAME><SID_DATA>STIG Title</SID_DATA></SI_DATA>
      </STIG_INFO>
      <VULN>...</VULN>
      <VULN>...</VULN>
    </iSTIG>
  </STIGS>
</CHECKLIST>
```

### 7.2 Multi-Bundle Export

**Input:**
```
CklExportRequest {
  BundleRoots = [
    "bundle-1/",
    "bundle-2/"
  ]
}
```

**Output:**
```
bundle-1/Export/
  stigforge_checklist.ckl
  stigforge_checklist.csv
```

**CKL Structure:**
```xml
<CHECKLIST>
  <ASSET>...</ASSET>
  <STIGS>
    <iSTIG>  <!-- Bundle 1 -->
      <STIG_INFO>
        <SI_DATA><SID_NAME>stigid</SID_NAME><SID_DATA>STIG_ID_1</SID_DATA></SI_DATA>
      </STIG_INFO>
      <VULN>...</VULN>
    </iSTIG>
    <iSTIG>  <!-- Bundle 2 -->
      <STIG_INFO>
        <SI_DATA><SID_NAME>stigid</SID_NAME><SID_DATA>STIG_ID_2</SID_DATA></SI_DATA>
      </STIG_INFO>
      <VULN>...</VULN>
    </iSTIG>
  </STIGS>
</CHECKLIST>
```

**Key Characteristics:**
- ✅ Single CHECKLIST file
- ✅ Multiple iSTIG sections (one per bundle)
- ✅ Shared ASSET element
- ✅ All VULNs merged into single document
- ⚠️ STIG Viewer compatibility **untested**

---

## 8) Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ Verification Sources                                            │
├─────────────────────────────────────────────────────────────────┤
│  SCAP (XCCDF)  │  Evaluate-STIG (XML)  │  Manual CKL (.ckl)    │
└────────┬────────────────────┬──────────────────────┬────────────┘
         │                    │                      │
         ▼                    ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│ Adapter Pattern (IVerifyResultAdapter)                          │
├─────────────────────────────────────────────────────────────────┤
│  ScapResultAdapter  │  EvaluateStigAdapter  │  CklAdapter       │
└────────┬────────────────────┬──────────────────────┬────────────┘
         │                    │                      │
         └────────────────────┼──────────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ NormalizedVerifyReport[]               │
         │ (common schema for all tools)          │
         └────────────────────┬───────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ VerifyOrchestrator.MergeReports()      │
         │ - Group by ControlId                   │
         │ - Apply precedence rules               │
         │ - Merge metadata & evidence            │
         │ - Log conflicts                        │
         └────────────────────┬───────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ ConsolidatedVerifyReport               │
         │ - Results (merged)                     │
         │ - Conflicts (reconciliation log)       │
         │ - Summary (Pass/Fail counts)           │
         └────────────────────┬───────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ VerifyReportWriter.WriteJson()         │
         │ → consolidated-results.json            │
         │ (VerifyReport with ControlResult[])    │
         └────────────────────┬───────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ CklExporter.ExportCkl()                │
         │ - Load consolidated-results.json       │
         │ - Dedup by control key                 │
         │ - Build CHECKLIST XML                  │
         │ - Write CKL or CKLB                    │
         └────────────────────┬───────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │ Output Files                           │
         │ - stigforge_checklist.ckl              │
         │ - stigforge_checklist.cklb (ZIP)       │
         │ - stigforge_checklist.csv (optional)   │
         └────────────────────────────────────────┘
```

---

## 9) Testing Coverage

### 9.1 Existing Tests

| Test File | Coverage | Status |
|-----------|----------|--------|
| `VerifyOrchestratorTests.cs` | Merge logic, conflict resolution, precedence | ✅ Comprehensive |
| `CklExporterTests.cs` | Single/multi-bundle export, CKL/CKLB format | ✅ Good |
| `CklExporterIntegrationTests.cs` | End-to-end export with real files | ✅ Good |
| `VerifyAdapterParsingTests.cs` | Adapter parsing for each tool | ✅ Good |

### 9.2 Missing Tests

| Test | Priority | Effort |
|------|----------|--------|
| Multi-benchmark CKL import in STIG Viewer | HIGH | High |
| Per-benchmark export option | MEDIUM | Medium |
| Metadata export to CKL | LOW | Low |
| Conflict resolution with 3+ tools | MEDIUM | Low |
| BenchmarkId enrichment from manifest | MEDIUM | Low |

---

## 10) Recommendations

### Immediate (v1.0)
1. ✅ **Validate multi-benchmark CKL with STIG Viewer** (HIGH priority)
   - Export test CKL with 2+ benchmarks
   - Import into STIG Viewer
   - Document compatibility findings

2. ✅ **Add integration test for STIG Viewer round-trip** (HIGH priority)
   - Export → Import → Verify cycle
   - Ensure no data loss

### Short-term (v1.1)
3. **Add per-benchmark export option** (MEDIUM priority)
   - Allow operators to choose merged vs. per-benchmark
   - Useful for separate submission workflows

4. **Export benchmark ID to CKL** (LOW priority)
   - Add SCAP benchmark ID to VULN metadata
   - Enables traceability

### Long-term (v2.0)
5. **Enhance metadata export** (LOW priority)
   - Include tool provenance in CKL
   - Support conflict resolution audit trail

6. **Support STIG Viewer native format** (MEDIUM priority)
   - If STIG Viewer adds multi-benchmark support
   - Ensure STIGForge exports are compatible

---

## 11) Appendix: Key File Locations

```
src/STIGForge.Verify/
  ├─ VerifyOrchestrator.cs              (Merge orchestration)
  ├─ NormalizedVerifyResult.cs           (Unified schema)
  ├─ VerifyModels.cs                     (Export models)
  ├─ VerifyReportWriter.cs               (JSON serialization)
  ├─ VerifyReportReader.cs               (JSON deserialization)
  ├─ VerificationArtifactAggregationService.cs  (Coverage aggregation)
  └─ Adapters/
      ├─ IVerifyResultAdapter.cs         (Interface)
      ├─ ScapResultAdapter.cs            (SCAP parsing)
      ├─ EvaluateStigAdapter.cs          (Evaluate-STIG parsing)
      └─ CklAdapter.cs                   (CKL parsing)

src/STIGForge.Export/
  ├─ CklExporter.cs                      (CKL/CKLB export)
  ├─ ExportStatusMapper.cs               (Status normalization)
  └─ ExportModels.cs                     (Export request/result)

tests/STIGForge.UnitTests/
  ├─ Verify/VerifyOrchestratorTests.cs
  ├─ Verify/VerifyAdapterParsingTests.cs
  └─ Export/CklExporterTests.cs

tests/STIGForge.IntegrationTests/
  └─ Export/CklExporterIntegrationTests.cs
```

---

**End of Analysis**

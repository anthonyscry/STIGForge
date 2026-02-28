# STIGForge Integration Quick Reference

## Result Consolidation Pipeline

```
SCAP (XCCDF)
Evaluate-STIG (XML)    ──→ Adapters ──→ NormalizedVerifyResult[] ──→ VerifyOrchestrator
Manual CKL (.ckl)                                                      (merge & reconcile)
                                                                              ↓
                                                                    ConsolidatedVerifyReport
                                                                              ↓
                                                                    VerifyReportWriter
                                                                              ↓
                                                                    consolidated-results.json
                                                                              ↓
                                                                    CklExporter
                                                                              ↓
                                                    stigforge_checklist.ckl/cklb
```

---

## Key Classes & Line References

### Consolidation
| Class | File | Lines | Purpose |
|-------|------|-------|---------|
| `VerifyOrchestrator` | `VerifyOrchestrator.cs` | 11-462 | Merge orchestration |
| `ConsolidatedVerifyReport` | `VerifyOrchestrator.cs` | 467-475 | Merged output model |
| `NormalizedVerifyResult` | `NormalizedVerifyResult.cs` | 7-65 | Unified finding schema |

### Adapters
| Class | File | Lines | Purpose |
|-------|------|-------|---------|
| `ScapResultAdapter` | `Adapters/ScapResultAdapter.cs` | 11-287 | SCAP parsing |
| `EvaluateStigAdapter` | `Adapters/EvaluateStigAdapter.cs` | 11-299 | Evaluate-STIG parsing |
| `CklAdapter` | `Adapters/CklAdapter.cs` | 10-242 | CKL parsing |

### Export
| Class | File | Lines | Purpose |
|-------|------|-------|---------|
| `CklExporter` | `CklExporter.cs` | 13-327 | CKL/CKLB export |
| `CklExportRequest` | `CklExporter.cs` | 335-347 | Export request model |
| `ControlResult` | `VerifyModels.cs` | 3-15 | JSON-serialized finding |

---

## Merge Algorithm (VerifyOrchestrator.cs)

### Precedence (Highest to Lowest)
1. **Manual CKL** (3) - Human review
2. **Evaluate-STIG** (2) - PowerShell automation
3. **SCAP** (1) - Fully automated

### Tiebreakers
1. Tool precedence
2. Latest timestamp
3. Status severity (Fail > Pass)
4. Deterministic sort (Tool, SourceFile, RuleId, VulnId)

### Conflict Logging
- Recorded when multiple tools report different statuses
- Includes resolution reason and all conflicting results
- Stored in `ConsolidatedVerifyReport.Conflicts`

---

## Export Structure

### Single Bundle
```
bundle/Export/
  stigforge_checklist.ckl
  stigforge_checklist.csv (optional)
```

### Multi-Bundle
```
bundle-1/Export/
  stigforge_checklist.ckl  ← Contains multiple iSTIG sections
  stigforge_checklist.csv
```

### CKL XML Structure
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

---

## Data Models

### NormalizedVerifyResult (Unified Schema)
```csharp
ControlId, VulnId, RuleId, Title, Severity
Status (enum: Pass/Fail/NotApplicable/NotReviewed/Informational/Error)
FindingDetails, Comments
Tool (SCAP/Evaluate-STIG/Manual CKL/Merged)
SourceFile, VerifiedAt, EvidencePaths
Metadata (tool-specific), RawArtifactPath, BenchmarkId
```

### ControlResult (JSON-Serialized)
```csharp
VulnId, RuleId, Title, Severity
Status (string, not enum)
FindingDetails, Comments
Tool, SourceFile, VerifiedAt
```

---

## Key Methods

### Merge Entry Points
- `VerifyOrchestrator.ParseAndMergeResults(paths)` - Parse + merge
- `VerifyOrchestrator.MergeReports(reports, errors)` - Merge only
- `VerifyOrchestrator.ApplyMappingManifest(report, manifest)` - Enrich with BenchmarkId

### Export Entry Points
- `CklExporter.ExportCkl(request)` - Main export
- `CklExporter.LoadResultsForBundle(bundleRoot)` - Load per-bundle
- `CklExporter.BuildCklDocument(resultSets, ...)` - Build XML

### Persistence
- `VerifyReportWriter.WriteJson(path, report)` - Save consolidated-results.json
- `VerifyReportReader.LoadFromJson(path)` - Load consolidated-results.json

---

## Status Mapping

### Normalize to VerifyStatus
| Input | Output |
|-------|--------|
| NotAFinding, pass, Compliant | Pass |
| Open, fail, NonCompliant | Fail |
| Not_Applicable, notapplicable, NotApplicable | NotApplicable |
| Not_Reviewed, notchecked, NotReviewed | NotReviewed |
| informational | Informational |
| error | Error |

### Export to CKL Status
| VerifyStatus | CKL Status |
|--------------|-----------|
| Pass | NotAFinding |
| Fail | Open |
| Error | Open |
| NotApplicable | Not_Applicable |
| Others | Not_Reviewed |

---

## Critical Gaps

| Gap | Severity | Fix |
|-----|----------|-----|
| Multi-benchmark CKL STIG Viewer compatibility untested | HIGH | Validate with STIG Viewer |
| No per-benchmark export option | MEDIUM | Add flag to CklExportRequest |
| BenchmarkId not exported to CKL | LOW | Add to VULN metadata |
| Metadata not exported | LOW | Add optional metadata export |
| No STIG Viewer round-trip test | HIGH | Add integration test |

---

## File Locations

```
Consolidation:
  src/STIGForge.Verify/VerifyOrchestrator.cs
  src/STIGForge.Verify/NormalizedVerifyResult.cs
  src/STIGForge.Verify/Adapters/*.cs

Export:
  src/STIGForge.Export/CklExporter.cs
  src/STIGForge.Export/ExportStatusMapper.cs

Tests:
  tests/STIGForge.UnitTests/Verify/VerifyOrchestratorTests.cs
  tests/STIGForge.UnitTests/Export/CklExporterTests.cs
  tests/STIGForge.IntegrationTests/Export/CklExporterIntegrationTests.cs
```

---

## Quick Facts

✅ **Supports:** SCAP, Evaluate-STIG, Manual CKL merging  
✅ **Exports:** CKL and CKLB (ZIP) formats  
✅ **Multi-bundle:** Single merged CHECKLIST with multiple iSTIG sections  
✅ **Conflict resolution:** Manual CKL > Evaluate-STIG > SCAP  
✅ **Deduplication:** Per-bundle, by control key (VulnId > RuleId > Title)  
✅ **Deterministic:** Sorted output, reproducible merges  

⚠️ **Untested:** Multi-benchmark CKL import in STIG Viewer  
⚠️ **Missing:** Per-benchmark export option  
⚠️ **Missing:** Metadata export to CKL  
⚠️ **Missing:** BenchmarkId in exported VULN elements  

---

**For detailed analysis, see INTEGRATION_ANALYSIS.md**

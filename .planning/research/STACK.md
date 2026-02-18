# Stack Research

**Domain:** STIG compliance tooling — export format expansion, verify accuracy fix, WPF UX polish
**Researched:** 2026-02-18
**Confidence:** HIGH (existing stack verified from source; new additions verified via NuGet and official docs)

---

## Existing Stack (Do NOT Re-Research)

The following is already in place and validated through v1.1. These entries are listed only to document integration
points for new features — do not re-evaluate these choices.

| Component | Version | Where Used |
|-----------|---------|-----------|
| .NET 8 / net48 dual-target | net8.0-windows (App/CLI), net48;net8.0 (libs) | All projects |
| WPF + CommunityToolkit.Mvvm | 8.4.0 | STIGForge.App |
| Serilog | 4.3.0 | Infrastructure, App |
| Dapper + Microsoft.Data.Sqlite | 2.1.66 / 10.0.2 | Infrastructure (SQLite) |
| System.Text.Json | 10.0.2 | Export, Infrastructure, Verify |
| System.Xml.Linq (XDocument/XElement) | BCL (built-in) | CklExporter, ScapResultAdapter, CklParser |

---

## New Stack Additions for v1.2

### 1. Excel/CSV Export: ClosedXML

**Purpose:** Generate `.xlsx` compliance reports for human audiences (management, auditors, assessment teams).

| Library | Version | Target Frameworks | License |
|---------|---------|------------------|---------|
| ClosedXML | 0.105.0 | netstandard2.0 (runs on net48 and net8.0) | MIT |

**Why ClosedXML over EPPlus:**
- MIT license — no commercial license required, works in air-gapped government environments without procurement overhead.
- EPPlus 8.x requires a commercial license key embedded at runtime or via config, which is a friction point for offline-first deployments and creates a license-auditing burden.
- ClosedXML targets netstandard2.0, meaning one package satisfies both the net48 and net8.0 build targets already used by STIGForge.Export.
- Dataset size for compliance reports (hundreds to low thousands of rows) is well within ClosedXML's performance envelope. The 100k+ row performance concern does not apply here.
- Actively maintained (0.105.0 released May 2025).

**Integration point:** Add to `STIGForge.Export.csproj`. Produces `.xlsx` worksheets from `ControlResult` collections via the existing `ExportModels.cs` data contract.

**Install:**
```xml
<PackageReference Include="ClosedXML" Version="0.105.0" />
```

**Note on dual-targeting:** ClosedXML 0.105.0 targets netstandard2.0 and netstandard2.1. Since `STIGForge.Export` already dual-targets `net48;net8.0`, no conditional `<ItemGroup Condition>` block is needed — NuGet resolves the correct netstandard compatibility automatically for both TFMs.

---

### 2. XCCDF/SCAP Result Export: System.Xml.Linq (BCL — already present)

**Purpose:** Write XCCDF 1.2 result files consumable by Tenable/ACAS, eMASS, and STIG Viewer.

**No new package required.** The existing `System.Xml.Linq` (XDocument/XElement) used in `CklExporter.cs` and `ScapResultAdapter.cs` is sufficient to generate well-formed XCCDF 1.2 XML output. The XCCDF 1.2 namespace (`http://checklists.nist.gov/xccdf/1.2`) and `TestResult` element structure are already modeled in `ScapResultAdapter.cs` for reading — the same XML Linq patterns write it for export.

**XCCDF 1.2 output envelope (reference):**
```xml
<xccdf:TestResult
    xmlns:xccdf="http://checklists.nist.gov/xccdf/1.2"
    id="xccdf_stigforge_testresult_[timestamp]"
    start-time="[ISO8601]"
    end-time="[ISO8601]">
  <xccdf:title>[Bundle/Pack name]</xccdf:title>
  <xccdf:target>[hostname]</xccdf:target>
  <xccdf:rule-result idref="[ruleId]" time="[ISO8601]">
    <xccdf:result>[pass|fail|notapplicable|notchecked]</xccdf:result>
  </xccdf:rule-result>
</xccdf:TestResult>
```

**Integration point:** New `XccdfExporter` class in `STIGForge.Export`, parallel to `CklExporter`. Takes `ControlResult` collections (same data contract), writes `xccdf_results.xml`. The `ExportStatusMapper` already maps internal statuses — extend it to map `VerifyStatus` to XCCDF vocabulary (`pass`, `fail`, `notapplicable`, `notchecked`).

---

### 3. Pluggable Export Adapter Architecture: IExportAdapter Interface (No New Package)

**Purpose:** Allow future formats (POAM Excel, SARIF, Tenable .nessus) to be added without modifying the export coordinator.

**No new package required.** The existing `IVerifyResultAdapter` pattern in `STIGForge.Verify/Adapters/` is the right model. Mirror it in `STIGForge.Export` with an `IExportAdapter` interface:

```csharp
public interface IExportAdapter
{
    string FormatName { get; }          // "XCCDF", "CSV", "Excel", "CKL"
    string[] SupportedExtensions { get; } // [".xml"], [".csv"], [".xlsx"], [".ckl"]
    ExportAdapterResult Export(ExportAdapterRequest request);
}
```

**Integration point:** `STIGForge.Export` already has `EmassExporter`, `CklExporter`, `StandalonePoamExporter`. Refactor these behind `IExportAdapter`. Register in DI via `Microsoft.Extensions.DependencyInjection` (already present in App via `Microsoft.Extensions.Hosting`).

---

### 4. SCC Verify 0-Results Fix: No New Package — Code Logic Fix

**Root cause (identified from code analysis):**

`VerifyReportWriter.BuildFromCkls()` (line 8 of `VerifyReportWriter.cs`) scans for `*.ckl` files only:

```csharp
var cklFiles = Directory.GetFiles(outputRoot, "*.ckl", SearchOption.AllDirectories)
```

SCC/SCAP does not write `.ckl` files. It writes XCCDF XML result files (typically named with patterns like `*_xccdf-results.xml` or similar). The `ScapResultAdapter` already exists and correctly parses XCCDF XML, but the workflow never routes SCAP output through it — it looks for CKLs that don't exist.

**Fix requires:** No new package. Two code changes:
1. `VerifyReportWriter.BuildFromCkls` — add a parallel scan for `*.xml` files in the output directory, route each through the existing `ScapResultAdapter.CanHandle()` / `ParseResults()` logic, merge with CKL results.
2. Or restructure `VerificationWorkflowService.RunAsync()` to write SCAP XML output path(s) and pass them directly to the `ScapResultAdapter` rather than relying on glob discovery.

The `ScapResultAdapter.ParseResults()` already handles the XCCDF namespace and `rule-result` elements correctly. The gap is purely in how the workflow collects files after SCAP runs.

**SCC output file location note (LOW confidence — official SCC docs not publicly accessible):** SCC writes results to a directory specified via its `--output` (or `-o`) argument, or to a default directory under the SCC installation. The `ScapCommandPath` and `ScapAdditionalArgs` fields in the VerifyView already expose these arguments to operators. The fix should also expose an "SCAP results folder" field in the verify workflow so operators can point STIGForge at where SCC wrote its output.

---

### 5. WPF Workflow UX Polish: No New Package — Existing MVVM + WPF Primitives

**Purpose:** Reduce operator friction — fewer clicks, clearer status, better error recovery.

**No new package required.** All needed primitives exist:

| UX Capability | Mechanism | Already Available |
|--------------|-----------|-------------------|
| Async progress indication | WPF `ProgressBar IsIndeterminate=True` bound to ViewModel `bool` property | Yes — WPF built-in |
| "IsRunning" state | `[ObservableProperty] bool _isRunning` with `CommunityToolkit.Mvvm` source gen | Yes — CommunityToolkit.Mvvm 8.4.0 |
| Cancellation | `AsyncRelayCommand` with `CancellationToken` support | Yes — CommunityToolkit.Mvvm 8.4.0 |
| Error display | `TextBlock` bound to `StatusMessage` observable property | Yes — existing pattern |
| Step reduction | Consolidate multi-click flows into single-command orchestration in ViewModel | Yes — no library needed |
| Status feedback | `TextBlock` bound to `VerifyStatus`, `ExportStatus`, etc. | Yes — already in VerifyView.xaml and ExportView.xaml |

**Known WPF/CommunityToolkit issue:** `IAsyncRelayCommand.IsRunning` does not trigger `INotifyPropertyChanged` reliably in WPF (documented GitHub issue #4266). Mitigation: do not bind `ProgressBar.IsIndeterminate` directly to `AsyncRelayCommand.IsRunning`. Instead, use a separate `[ObservableProperty] bool _isBusy` that is set/cleared in the command body. This is a zero-dependency fix.

---

## Summary: What Is and Is NOT Being Added

| Category | Decision | Rationale |
|----------|----------|-----------|
| Excel .xlsx export | ADD ClosedXML 0.105.0 | MIT license, netstandard2.0, air-gapped safe |
| XCCDF export | NO new package | System.Xml.Linq (BCL) is sufficient |
| Export adapter interface | NO new package | Mirrors existing IVerifyResultAdapter pattern |
| SCC 0-results fix | NO new package | Code logic fix in VerifyReportWriter + workflow |
| WPF UX polish | NO new package | CommunityToolkit.Mvvm 8.4.0 + WPF primitives |
| EPPlus | DO NOT ADD | Commercial license incompatible with air-gapped gov deployment |
| DocumentFormat.OpenXml (raw) | DO NOT ADD | ClosedXML wraps it with a usable API; raw SDK is too verbose |
| Telerik/DevExpress controls | DO NOT ADD | Commercial license, not in existing stack |
| SCAP schema validation library | DO NOT ADD | Offline environments cannot fetch XSD; validate structurally via adapter |

---

## Installation (Single Change)

Only one new NuGet reference is needed across the entire codebase:

**File:** `src/STIGForge.Export/STIGForge.Export.csproj`
```xml
<ItemGroup>
  <PackageReference Include="ClosedXML" Version="0.105.0" />
</ItemGroup>
```

No changes to `STIGForge.App.csproj`, `STIGForge.Verify.csproj`, or any other project files for the library layer.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| ClosedXML 0.105.0 (MIT) | EPPlus 8.4.2 (commercial license) | EPPlus requires a license key configured at runtime or in appSettings.json; this is procurement friction for air-gapped government environments. The non-commercial option requires explicit organizational registration. ClosedXML has no such requirement. |
| ClosedXML 0.105.0 | DocumentFormat.OpenXml 3.4.1 (raw) | Raw Open XML SDK generates verbose, boilerplate-heavy code for spreadsheet generation. ClosedXML wraps it with a clean API. Both are MIT-licensed and work offline. |
| System.Xml.Linq (BCL) for XCCDF export | Third-party SCAP library | No mature, actively-maintained SCAP library exists on NuGet. The XCCDF 1.2 schema is well-documented XML — generating it with XDocument/XElement is straightforward and already validated via ScapResultAdapter's parsing logic. |
| IExportAdapter interface (hand-rolled) | MEF / plugin framework | MEF adds assembly-load complexity and is overkill for 3-4 known adapters. A simple DI-registered interface list is sufficient and consistent with the existing IVerifyResultAdapter pattern. |

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| ClosedXML 0.105.0 | net48 (via netstandard2.0), net8.0 | DocumentFormat.OpenXml 3.x is a transitive dependency — verify no conflict with any existing direct reference. STIGForge.Export does not currently reference DocumentFormat.OpenXml directly, so no conflict expected. |
| CommunityToolkit.Mvvm 8.4.0 | net8.0-windows | Already in STIGForge.App.csproj. No version change needed. |
| System.Text.Json 10.0.2 | net48, net8.0 | Already in STIGForge.Export.csproj and STIGForge.Verify.csproj. No version change needed. |

---

## Sources

- [NuGet Gallery — ClosedXML 0.105.0](https://www.nuget.org/packages/ClosedXML/) — version and framework targets verified
- [GitHub — ClosedXML/ClosedXML](https://github.com/ClosedXML/ClosedXML) — MIT license confirmed
- [NuGet Gallery — EPPlus 8.4.2](https://www.nuget.org/packages/epplus/) — version confirmed; commercial license confirmed via epplussoftware.com
- [NuGet Gallery — DocumentFormat.OpenXml 3.4.1](https://www.nuget.org/packages/DocumentFormat.OpenXml/) — version confirmed
- [NIST CSRC — XCCDF 1.2 Specification](https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/xccdf) — namespace and TestResult structure reference
- [CommunityToolkit MVVM — AsyncRelayCommand docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/asyncrelaycommand) — IsRunning behavior confirmed (HIGH confidence)
- [CommunityToolkit — IsRunning INotifyPropertyChanged issue #4266](https://github.com/CommunityToolkit/WindowsCommunityToolkit/issues/4266) — known WPF binding limitation confirmed
- Code analysis of STIGForge.Verify source — VerifyReportWriter.BuildFromCkls 0-results root cause identified (HIGH confidence — direct code read)

---

*Stack research for: STIGForge v1.2 — XCCDF/SCAP export, CSV/Excel reporting, export adapter architecture, SCC verify correctness, WPF UX polish*
*Researched: 2026-02-18*

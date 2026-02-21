# Stack Research

**Domain:** Offline-first Windows STIG compliance workflow tooling
**Researched:** 2026-02-20
**Confidence:** MEDIUM

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET SDK + Runtime | 8.0 LTS (latest 8.0.x patch) | Primary app/runtime for WPF + CLI + orchestration | .NET 8 is still supported through Nov 2026 and is the stability-first choice for a greenfield compliance tool that must run offline and deterministically. **Confidence: HIGH** |
| WPF (`net8.0-windows`) | .NET 8 WindowsDesktop | Native Windows operator console | WPF is Windows-native, no browser runtime needed, and aligns with your hard Windows mission environment. **Confidence: HIGH** |
| Windows PowerShell execution host | 5.1 (`powershell.exe`, out-of-process) | PowerSTIG apply/evaluate compatibility boundary | PowerSTIG declares minimum PowerShell 5.1, so process-boundary invocation preserves compatibility with existing PSDSC workflows and avoids runtime-mixing fragility. **Confidence: HIGH** |
| PowerShell (sidecar runtime) | 7.4 LTS | Non-legacy scripting, modern automation shell | Keep 7.4 for modern scripting/CLI utilities, but route PowerSTIG/legacy DSC calls through 5.1. This split is standard in mixed Windows compliance stacks. **Confidence: MEDIUM** |
| PowerSTIG | 4.28.0 | STIG data parsing + DSC composite resources + checklist generation | This is the de-facto Microsoft-maintained automation base for DISA STIG application and includes `New-StigCheckList` for STIG Viewer workflows. **Confidence: HIGH** |
| SQLite | 3.51.2 engine + `Microsoft.Data.Sqlite` 10.0.3 | Offline evidence/index store | Single-file, no-service local DB is the right fit for air-gapped operation and portable evidence bundles. **Confidence: HIGH** |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.CommandLine | 2.0.3 | CLI verbs for build/apply/evaluate/export mission loop | Use for first-class CLI UX and predictable argument parsing instead of hand-rolled parsers |
| CommunityToolkit.Mvvm | 8.4.0 | WPF MVVM plumbing | Use for observable models and command wiring; avoid custom MVVM boilerplate |
| Dapper | 2.1.66 | Explicit SQL over SQLite | Use for deterministic query control and transparent audit/debug behavior |
| FluentValidation | 12.1.1 | Input/config validation | Use for operator-provided config, overlays, and import validation before execution |
| Serilog + file sink | 4.3.1 + Serilog.Sinks.File 7.0.0 | Structured local logging | Use for machine-readable audit logs and reproducible troubleshooting in offline environments |
| `System.Xml.Linq` + `System.Xml.Schema` (built-in) | .NET 8 BCL | XCCDF/ARF/SCC result parsing and schema validation | Use for strict schema-first parsing of SCAP artifacts instead of ad-hoc XML string processing |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `dotnet` SDK 8.0.x | Build/test/publish | Pin with `global.json` to lock deterministic builds |
| PowerShellGet / PSResourceGet | Module acquisition in connected build environment | Mirror PowerSTIG and dependent modules into an internal offline feed for air-gapped installs |
| Windows SDK `signtool` | Authenticode signing | Sign binaries and installers for enterprise trust chains |

## Installation

```bash
# .NET packages
dotnet add package CommunityToolkit.Mvvm --version 8.4.0
dotnet add package System.CommandLine --version 2.0.3
dotnet add package Microsoft.Data.Sqlite --version 10.0.3
dotnet add package Dapper --version 2.1.66
dotnet add package FluentValidation --version 12.1.1
dotnet add package Serilog --version 4.3.1
dotnet add package Serilog.Sinks.File --version 7.0.0

# PowerShell modules (connected environment, then mirror offline)
Install-PSResource -Name PowerSTIG -Version 4.28.0
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| .NET 8 LTS | .NET 9/10 | Use only after core compliance workflows stabilize and full revalidation budget exists |
| WPF desktop | Web/Electron shell | Use only if cross-platform UI becomes a hard requirement (it is not for this mission) |
| SQLite + Dapper | EF Core-heavy model | Use only if domain model complexity clearly outweighs deterministic SQL transparency |
| PowerShell 5.1 process boundary for PowerSTIG | In-proc runspaces only | Use only if you remove dependency on legacy PSDSC-based resources |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Single-runtime assumption ("everything in PS7") | Breaks compatibility with PowerSTIG/legacy DSC workflows that still depend on Windows PowerShell semantics | Dual-runtime model: PS7 for modern automation, 5.1 host for PowerSTIG/DSC execution |
| Custom checklist XML format | Breaks STIG Viewer interoperability and downstream audit workflows | Emit STIG Viewer-compatible CKL via PowerSTIG `New-StigCheckList` pipeline |
| Loose XML parsing without schema validation | Silent parser drift on ARF/XCCDF changes can corrupt findings | Validate against XCCDF/ARF schemas before ingesting SCC artifacts |
| Browser-wrapper desktop stack (Electron/Tauri) | Adds update/runtime surface area and weakens offline-hardening posture for Windows-only mission | Native WPF + .NET desktop runtime |

## Stack Patterns by Variant

**If strict DISA compatibility is the priority (recommended baseline):**
- Use PowerSTIG 4.28.0 + Windows PowerShell 5.1 execution boundary
- Because current PowerSTIG documentation and module requirements are still anchored in this model

**If modernization is a later-phase priority:**
- Keep orchestration in .NET 8 + PowerShell 7.4 and isolate 5.1 calls behind adapter interfaces
- Because this enables future migration to newer DSC patterns without destabilizing the compliance core

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| PowerSTIG 4.28.0 | Windows PowerShell 5.1+ | PowerShell Gallery lists minimum PowerShell version 5.1 |
| `Microsoft.Data.Sqlite` 10.0.3 | SQLite engine 3.x | Provider bundles SQLitePCL dependencies; pin package to lock behavior |
| `System.CommandLine` 2.0.3 | .NET 8+ | Stable 2.x release line, suitable for new CLI surfaces |
| PowerShell 7.4 LTS | .NET 8 ecosystem | Use for modern scripts, not as a drop-in replacement for all 5.1 module workflows |

## Validation Gap (Important)

- I could not find a single, current official distribution channel or versioned spec for "Evaluate-STIG" equivalent to PowerSTIG on PowerShell Gallery. Treat Evaluate-STIG integration as an external adapter boundary (process + contract tests), and validate your chosen upstream artifact during the implementation phase. **Confidence: LOW**

## Sources

- https://learn.microsoft.com/en-us/dotnet/core/releases-and-support (.NET support policy, updated 2025-11-18) - **HIGH**
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/ (WPF Windows-only framework) - **HIGH**
- https://learn.microsoft.com/en-us/powershell/scripting/install/powershell-support-lifecycle (PowerShell lifecycle incl. 7.4 LTS, Windows PowerShell note) - **HIGH**
- https://www.powershellgallery.com/packages/PowerSTIG (PowerSTIG 4.28.0, min PowerShell 5.1, release date) - **HIGH**
- https://github.com/microsoft/PowerStig/wiki/Documentation-via-STIG-Checklists (`New-StigCheckList`, STIG Viewer checklist workflow) - **HIGH**
- https://github.com/microsoft/PowerStig/wiki (home page indicates DSC composite resource model and current module guidance) - **MEDIUM**
- https://learn.microsoft.com/en-us/powershell/dsc/overview?view=dsc-3.0 (modern DSC and adapter model differences) - **HIGH**
- https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/xccdf (XCCDF XML spec/schema reference) - **HIGH**
- https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/arf (ARF model/schema reference) - **HIGH**
- https://www.nuget.org/packages/System.CommandLine (2.0.3) - **MEDIUM**
- https://www.nuget.org/packages/Microsoft.Data.Sqlite (10.0.3) - **MEDIUM**
- https://www.nuget.org/packages/CommunityToolkit.Mvvm (8.4.0) - **MEDIUM**
- https://www.nuget.org/packages/Dapper (2.1.66) - **MEDIUM**
- https://www.nuget.org/packages/FluentValidation (12.1.1) - **MEDIUM**
- https://www.nuget.org/packages/Serilog and https://www.nuget.org/packages/Serilog.Sinks.File (4.3.1 / 7.0.0) - **MEDIUM**
- https://www.sqlite.org/index.html (SQLite latest release stream and offline suitability context) - **HIGH**

---
*Stack research for: Windows compliance workflow tooling (PowerSTIG + Evaluate-STIG + SCC + STIG Viewer outputs)*
*Researched: 2026-02-20*

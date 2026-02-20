# Technology Stack

**Project:** STIGForge Next
**Researched:** 2026-02-19

## Recommended Stack

### Core Framework
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| .NET SDK + Runtime | 8.0.x (LTS, latest patch) | Core runtime for App + CLI + shared modules | **Prescriptive choice:** keep .NET 8 for reboot scope because the product requires stability/offline operation now, and .NET 8 is still supported through 2026-11-10. Pin exact SDK via `global.json` and patch monthly. **Confidence: HIGH** |
| WPF (`net8.0-windows`) | .NET 8 WindowsDesktop | Desktop operator UX | Native Windows, mature data binding, no web runtime dependency, aligns with explicit product requirement for WPF parity with CLI. **Confidence: HIGH** |
| System.CommandLine | latest stable 2.x | CLI command tree, help, parsing, completion | Microsoft-supported CLI stack used by .NET CLI/tooling; stronger long-term fit than ad-hoc parsers. **Confidence: MEDIUM** (exact stable 2.x package version should be pinned at implementation start) |

### Database
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| SQLite engine | 3.x (pinned file version in distribution) | Offline local relational store for canonical model, overlays, audit/evidence indexes | SQLite is designed for local app storage and no-admin operation. Single-file DB supports air-gapped workflows and transportability. **Confidence: HIGH** |
| Microsoft.Data.Sqlite | latest stable compatible with .NET 8 | ADO.NET provider for SQLite | First-party provider from Microsoft docs; lightweight and predictable. **Confidence: HIGH** |
| Dapper | 2.x | Explicit SQL mapping for control over query shape and deterministic behavior | Prefer explicit SQL and schema control for compliance/auditability over heavy ORM abstraction. **Confidence: MEDIUM** |

### Infrastructure
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Windows PowerShell host integration | Windows PowerShell 5.1 (`powershell.exe`) invoked out-of-process | Execute enforcement/verification scripts with 5.1 module compatibility | 5.1 runs on .NET Framework (Desktop edition). For strict compatibility, use a process boundary (`powershell.exe`) instead of in-proc hosting in .NET 8. **Confidence: HIGH** |
| Microsoft.Extensions.DependencyInjection | 10.x package line (compatible with .NET 8 app) | Composition root and service lifetimes across App/CLI modules | Standardized DI model in .NET ecosystem, reduces hidden coupling in module boundaries. **Confidence: HIGH** |
| Microsoft.Extensions.Logging (+ EventLog provider) | 10.x package line | Structured logs for CLI + desktop + audit diagnostics | Native structured logging API with provider model; no custom logging substrate needed. **Confidence: HIGH** |
| WiX Toolset | v6.x | Deterministic MSI packaging for offline enterprise deployment | MSI remains the most controllable offline enterprise installer path; WiX gives fine-grained install authoring and signing control. **Confidence: MEDIUM** |
| SignTool (Windows SDK) | latest Windows SDK in build image | Authenticode signing of binaries/installers | Required trust chain for Windows enterprise distribution and tamper detection workflows. **Confidence: MEDIUM** |

### Supporting Libraries
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| CommunityToolkit.Mvvm | 8.x | MVVM source generators, observable properties/commands | Default for WPF presentation layer; avoid custom MVVM plumbing |
| FluentValidation | 11.x | Declarative validation for profile/overlay/manual-answer inputs | Use for all operator-entered config and import diagnostics |
| Polly | 8.x | Retry/timeout/circuit policies for process and file IO boundaries | Use at external boundaries (PowerShell process, scanner wrappers, WinRM), not inside pure domain logic |
| System.Text.Json | built-in | Deterministic JSON contracts/schemas/manifest serialization | Default JSON stack for all schema-bound contracts |

## Prescriptive Build and Packaging Profile (Determinism)

Use this baseline in all projects and CI:

- `Deterministic=true` (default in modern .NET, keep explicit in shared props)
- `ContinuousIntegrationBuild=true` on CI
- NuGet lock files: `RestorePackagesWithLockFile=true`, CI restore in locked mode
- Pinned SDK in `global.json`
- Fixed artifact ordering + normalized timestamps in package manifest generation
- Reproducibility gate: rebuild same git commit twice in clean env and compare hashes for key outputs (`*.dll`, `*.exe`, bundle manifest, export package index)

## What Not to Use

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Desktop UI | WPF | Electron/Tauri/webview shell | Adds unnecessary browser/runtime surface, larger footprint, weaker offline hardening story for this Windows-only platform |
| Data access | Microsoft.Data.Sqlite + Dapper | Heavy EF-first model for all modules | Reduces SQL visibility/control needed for deterministic outputs and auditability |
| PowerShell integration | Out-of-process `powershell.exe` 5.1 | In-proc runspace coupling to one PowerShell runtime | Increases compatibility risk across 5.1/Desktop vs Core runtime boundaries |
| Packaging | WiX MSI as primary | ClickOnce/Squirrel as primary enterprise channel | Weaker enterprise policy control and installer customization for this compliance-focused deployment model |
| Dependency resolution | Locked package graph | Floating package versions in production builds | Breaks reproducibility and can invalidate deterministic-output guarantees |

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| .NET runtime track | .NET 8 LTS now | Immediate migration to .NET 10 | Adds migration and revalidation risk during reboot; defer until baseline MVP stabilizes |
| Installer format | MSI (WiX) primary, optional MSIX later | MSIX-only distribution | MSIX benefits are real, but enterprise/offline operational constraints and app container behavior can complicate first release rollout |
| Data store | SQLite | SQL Server LocalDB/PostgreSQL | Requires service/process admin overhead and weakens air-gapped portability |

## Installation

```bash
# Core app dependencies
dotnet add package CommunityToolkit.Mvvm
dotnet add package System.CommandLine
dotnet add package Microsoft.Data.Sqlite
dotnet add package Dapper
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.EventLog
dotnet add package FluentValidation
dotnet add package Polly

# Deterministic dependency locking (project property)
# <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>

# WiX tooling (packaging pipeline)
dotnet tool install --global wix
```

## Sources

- .NET support lifecycle (updated 2026-02-10, .NET 8 EOS 2026-11-10): https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core (**HIGH**)
- WPF on .NET (Windows-only): https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/ (**HIGH**)
- System.CommandLine overview: https://learn.microsoft.com/en-us/dotnet/standard/commandline/ (**HIGH**)
- C# deterministic compilation option: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation#deterministic (**HIGH**)
- .NET SDK MSBuild properties (`ContinuousIntegrationBuild`): https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#continuousintegrationbuild (**HIGH**)
- NuGet lock files / locked restore: https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies (**HIGH**)
- Microsoft.Data.Sqlite overview: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/ (**HIGH**)
- SQLite use guidance: https://www.sqlite.org/whentouse.html (**HIGH**)
- SQLite WAL behavior/concurrency constraints: https://www.sqlite.org/wal.html (**HIGH**)
- PowerShell 5.1 vs 7.x runtime differences: https://learn.microsoft.com/en-us/powershell/scripting/whats-new/differences-from-windows-powershell?view=powershell-7.5 (**HIGH**)
- PowerShell editions (Desktop vs Core): https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_powershell_editions?view=powershell-7.5 (**HIGH**)
- .NET Community Toolkit MVVM: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/ (**MEDIUM**)
- .NET logging and DI overviews: https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview and https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview (**HIGH**)
- WiX Toolset project and release status (v6.0.2 latest observed): https://github.com/wixtoolset/wix (**MEDIUM**)
- MSIX overview (for optional later phase): https://learn.microsoft.com/en-us/windows/msix/overview (**MEDIUM**)

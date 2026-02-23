# Technology Stack

**Analysis Date:** 2026-02-21

## Languages

**Primary:**
- C# - All source code (.NET 8.0)
- XAML - WPF UI markup in `src/STIGForge.App`
- PowerShell - DSC configuration, system commands via ProcessRunner
- XML - Answer files, configuration templates, manifest files
- JSON - Data serialization (profiles, overlays, evidence, reports)

**Secondary:**
- Batch/CMD - Windows command-line invocation
- Regex - Pattern matching for content parsing and HTML link extraction

## Runtime

**Environment:**
- .NET 8.0 (LTS) - specified in `global.json` with rollForward: latestFeature
- Target framework: `net8.0` for most projects, `net8.0-windows` for WPF app
- Windows platform required for Apply operations (secedit, auditpol, DSC, Registry)
- Cross-platform capable for CLI and Infrastructure layers (Linux/macOS for reading only)

**Package Manager:**
- NuGet - configured in `NuGet.config` with nuget.org as source
- Lockfile: Implicit (NuGet manages via .csproj ProjectReference versions)

## Frameworks

**Core:**
- Microsoft.NET.Sdk - Standard .NET 8.0 SDK for all projects
- Windows Presentation Foundation (WPF) - Desktop UI in `src/STIGForge.App` (UseWPF: true)
- Microsoft.Extensions.Hosting - Dependency injection and host configuration
- Microsoft.Extensions.Configuration.Json - Configuration file loading

**Data Access:**
- Dapper 2.1.66 - Lightweight ORM for SQLite queries in `src/STIGForge.Infrastructure`
- Microsoft.Data.Sqlite 10.0.2 - SQLite database client

**Logging:**
- Serilog 4.3.0 - Structured logging framework across all projects
- Serilog.Extensions.Hosting 10.0.0 - Integration with IHost
- Serilog.Sinks.File 7.0.0 - File-based log output

**UI/MVVM:**
- CommunityToolkit.Mvvm 8.4.0 - MVVM pattern, ObservableProperty, RelayCommand in WPF app

**CLI:**
- System.CommandLine 2.0.0-beta4.22272.1 - Command-line argument parsing in CLI projects

**Testing:**
- xunit 2.9.3 - Unit test framework
- Moq 4.20.72 - Mocking library
- FluentAssertions 8.8.0 - Assertion syntax
- Microsoft.NET.Test.Sdk 18.0.1 - Test runner integration
- coverlet.collector 6.0.4 - Code coverage collection

**Build/Dev:**
- Microsoft.Extensions.Logging - Structured logging interfaces
- Microsoft.Extensions.Logging.Abstractions 10.0.0/10.0.3 - Logging abstractions
- Microsoft.Extensions.Logging.Console 10.0.3 - Console logging provider
- System.Text.Json 10.0.2 - JSON serialization
- System.DirectoryServices 9.0.0 - Active Directory integration
- System.Security.Cryptography.ProtectedData 9.0.5 - Data protection API for encryption
- System.Net.Http - HTTP client (implicit, used in BuildCommands)
- System.Net.NetworkInformation - Network diagnostics (Ping)

## Key Dependencies

**Critical:**
- `Dapper` 2.1.66 - Required for all SQLite data access; type handlers for DateTimeOffset conversion in `DbBootstrap.cs`
- `Microsoft.Data.Sqlite` 10.0.2 - SQLite data provider; all database connections use this
- `Serilog` 4.3.0 - All logging throughout codebase depends on this framework
- `System.CommandLine` 2.0.0-beta4.22272.1 - CLI command parsing; all CLI commands depend on this

**Infrastructure:**
- `CommunityToolkit.Mvvm` 8.4.0 - MVVM property binding in `MainViewModel` and related partial classes
- `Microsoft.Extensions.Hosting` 10.0.2 - Dependency injection and service host lifecycle
- `System.DirectoryServices` 9.0.0 - Active Directory/LDAP queries in ToolDefaults (see `MainViewModel.ToolDefaults.cs`)
- `System.Security.Cryptography.ProtectedData` 9.0.5 - Encryption for sensitive data storage
- `System.Text.Json` 10.0.2 - JSON serialization for profiles, overlays, evidence
- `xunit` 2.9.3 - All tests depend on this test framework

## Configuration

**Environment:**
- Configuration via `Directory.Build.props`:
  - LangVersion: latest (C# 12.0 features allowed)
  - Nullable: enable (strict null checking)
  - TreatWarningsAsErrors: false (warnings not enforced)
  - Deterministic: true (reproducible builds)
  - CI detection: ContinuousIntegrationBuild set when CI=true

**Build:**
- `STIGForge.sln` - Visual Studio solution file (root)
- Multiple `.csproj` files per project (see Structure for locations)
- Global properties in `Directory.Build.props` applied to all projects
- Project-specific settings in each project's `<PropertyGroup>`

**NuGet Configuration:**
- Single source: `https://api.nuget.org/v3/index.json`
- No custom feeds configured
- All packages sourced from official NuGet.org repository

## Platform Requirements

**Development:**
- .NET 8.0 SDK installed
- Windows 10 or later (for WPF application development)
- Visual Studio 2022 or JetBrains Rider (for C# development)
- PowerShell 5.1+ or PowerShell 7+ (for DSC, secedit, auditpol execution)

**Production:**
- Windows Server 2016 or later (for Apply operations with DSC/Registry/secedit/auditpol)
- .NET 8.0 Runtime
- PowerShell 5.1+ (on Windows Server)
- Administrator/System privileges for Apply, Verify operations
- Active Directory access (optional, for ToolDefaults LDAP queries)

**Deployment Target:**
- Windows desktop/server deployment via WPF app or CLI console app
- CLI app distributable as self-contained executable (win-x64)
- Database: Local SQLite file (embedded in deployment)
- Logs: Written to file system (Serilog file sink)

---

*Stack analysis: 2026-02-21*

# External Integrations

**Analysis Date:** 2026-02-21

## APIs & External Services

**GitHub Repository Integration:**
- PowerStig Source (Microsoft)
  - URL: `https://github.com/microsoft/PowerStig`
  - ZIP fallback: `https://github.com/microsoft/PowerStig/archive/refs/heads/master.zip`
  - Purpose: Source for STIG content bundle downloads
  - Implementation: `src/STIGForge.Cli/Commands/BuildCommands.cs` downloads via HttpClient
  - SDK/Client: `System.Net.Http.HttpClient`

- NIWC Atlantic SCAP Content Library
  - URL: `https://github.com/niwc-atlantic/scap-content-library`
  - ZIP fallback: `https://github.com/niwc-atlantic/scap-content-library/archive/refs/heads/main.zip`
  - Purpose: Source for SCAP benchmark content bundles
  - Implementation: Same HTTP client in BuildCommands
  - SDK/Client: `System.Net.Http.HttpClient`

**HTTP Client Configuration:**
- User Agent: `STIGForge/1.0 (+mission-autopilot)`
- Timeout: 5 minutes
- Implemented in: `src/STIGForge.Cli/Commands/BuildCommands.cs`
- Supports streaming large ZIP downloads

## Data Storage

**Databases:**
- SQLite (local embedded)
  - Type: File-based relational database
  - Provider: `Microsoft.Data.Sqlite` v10.0.2
  - Client: `Dapper` v2.1.66 (micro-ORM for queries)
  - Location: Configured via connection string (typically local file path)
  - Schema: Initialized in `src/STIGForge.Infrastructure/Storage/DbBootstrap.cs`
  - Tables:
    - `content_packs` - Downloaded/imported STIG bundles
    - `profiles` - User-created security profiles (stored as JSON)
    - `overlays` - Profile customizations/overrides
    - `audit_trail` - Change log for compliance auditing

**File Storage:**
- Local filesystem only
- Locations:
  - Working directory: Bundle extraction, Apply/Verify outputs
  - Logs: File-based Serilog sink configuration
  - Database: SQLite `.db` file in application directory
  - Exports: CSV, JSON, CKL, eMASS formats written to output directory

**Caching:**
- None - No external cache service configured
- In-memory caching: MVVM ObservableCollections in WPF UI (FleetInventoryItems, etc.)

## Authentication & Identity

**Auth Provider:**
- Custom Active Directory integration (optional feature)
  - Implementation: `src/STIGForge.App/MainViewModel.ToolDefaults.cs`
  - SDK/Client: `System.DirectoryServices` v9.0.0
  - Protocol: LDAP
  - Functionality:
    - DirectoryEntry queries to LDAP://RootDSE for forest information
    - DirectorySearcher for user/computer lookups in domain
  - Use case: Auto-populate tool defaults with domain information

**No OAuth/OpenID Connect:**
- No cloud identity provider integration
- No JWT/Bearer token authentication
- No API key management system

**Local Credentials:**
- WinRM credentials for remote execution (fleet management)
  - Stored in UI state/profiles (no encryption noted)
  - Used by remote commands in FleetCommands
- Encryption: System.Security.Cryptography.ProtectedData available but usage unclear

## Monitoring & Observability

**Error Tracking:**
- None - No error tracking service (Sentry, AppInsights, etc.)
- Errors logged locally via Serilog

**Logs:**
- Framework: Serilog v4.3.0
- Implementation:
  - Structured logging with correlation IDs (implicit via event properties)
  - File sink: Serilog.Sinks.File v7.0.0
  - Console sink: Microsoft.Extensions.Logging.Console
- Levels: Information, Warning, Error, Debug
- Location: Application output directory (file path configured at startup)
- Retention: No automatic rotation/cleanup configured

**Performance Monitoring:**
- None - No APM (Application Performance Monitoring) tool integrated
- Manual timing via stopwatch in Apply/Verify operations

## CI/CD & Deployment

**Hosting:**
- Windows desktop application (WPF) - `src/STIGForge.App`
- Windows console application (CLI) - `src/STIGForge.Cli`
- Self-contained deployment (win-x64) for CLI
- No cloud hosting (Azure, AWS, etc.) detected

**CI Pipeline:**
- Not detected in source - no GitHub Actions, Azure Pipelines, etc. configured in repo
- Build artifacts: Generated in bin/obj directories
- Test execution: xunit test runner available (Microsoft.NET.Test.Sdk)

**Package Distribution:**
- No NuGet package publishing detected
- No Docker/container deployment
- No automated release process evident

## Environment Configuration

**Required Environment Variables:**
- No explicit .env file detected (forbidden_files restriction prevents reading)
- Configuration sources:
  - `appsettings.json` loaded via `Microsoft.Extensions.Configuration.Json`
  - Command-line arguments (System.CommandLine)
  - UI configuration saved to file (SaveUiState, SaveFleetInventory methods)

**Secrets Storage:**
- Implicit: Active Directory credentials for remote execution
- Implicit: Database connection string (SQLite path)
- No external secrets vault (Azure KeyVault, AWS Secrets Manager, etc.)
- System.Security.Cryptography.ProtectedData available for protecting sensitive strings

## Webhooks & Callbacks

**Incoming:**
- None detected - No HTTP listener or webhook endpoint

**Outgoing:**
- None detected - No external API calls for notifications/webhooks
- GitHub downloads are pull-only (no push/callback)

## Remote Execution & Control

**WinRM Integration:**
- Purpose: Fleet management - remote STIG Apply/Verify on multiple hosts
- Protocol: Windows Remote Management
- Implementation: `src/STIGForge.App/MainViewModel.Fleet.cs`
- Configuration: Stores host credentials in fleet inventory (plaintext in UI state)
- Ping diagnostics: `System.Net.NetworkInformation.Ping` for connectivity checks

**PowerShell DSC Integration:**
- Framework: Desired State Configuration via PowerShell
- Implementation: `src/STIGForge.Apply/Dsc/LcmService.cs`
- Purpose: Apply STIG configurations to local system
- LCM configuration: Generates MOF files for Local Configuration Manager
- Execution: Windows PowerShell engine (implicit via System.Diagnostics.Process)

**Windows Native Tools:**
- secedit - Security Configuration Editor (local system only)
- auditpol - Audit policy configuration (local system only)
- Registry Editor APIs - Direct Windows Registry manipulation
- All via: `System.Diagnostics.ProcessStartInfo` invocations

**Network Diagnostics:**
- Ping connectivity check to fleet hosts
- Used in: `MainViewModel.ToolDefaults.cs` to validate WinRM connectivity
- Library: `System.Net.NetworkInformation`

---

*Integration audit: 2026-02-21*

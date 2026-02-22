# Architecture

**Analysis Date:** 2026-02-21

## Pattern Overview

**Overall:** Layered architecture with domain-driven design, organized around compliance workflow phases (Import → Build → Apply → Verify → Export)

**Key Characteristics:**
- Separation of concerns across 11 specialized projects
- Dependency injection via Microsoft.Extensions with Serilog logging
- SQLite persistence with Dapper ORM for data access
- MVVM pattern for WPF UI using CommunityToolkit
- Service-oriented composition with orchestrators for complex workflows
- Heavy use of immutable request/response models for phase boundaries

## Layers

**Presentation (UI):**
- Purpose: WPF desktop application frontend with MVVM architecture
- Location: `src/STIGForge.App/`
- Contains: XAML views, ViewModels, theme resources
- Depends on: Core abstractions, all service layers
- Used by: End users via WPF application

**Application/Orchestration:**
- Purpose: Coordinates complex multi-phase workflows and UI state management
- Location: `src/STIGForge.App/MainViewModel.cs` (partially split across `MainViewModel.*.cs` files)
- Contains: MainViewModel (observes UI state), orchestrators (BundleOrchestrator, ImportSelectionOrchestrator)
- Depends on: Service layer, repositories, infrastructure
- Used by: Presentation layer and asynchronous task handlers

**Domain/Business Logic:**
- Purpose: Core compliance engine - models, contracts, validation
- Location: `src/STIGForge.Core/`
- Contains: Domain models (ControlRecord, Profile, ContentPack, RunManifest), service interfaces, enums (ControlStatus, OsTarget, RoleTemplate)
- Depends on: Nothing - pure domain layer
- Used by: All other layers

**Service/Workflow Layer:**
- Purpose: Implements compliance workflows and phase orchestration
- Location: `src/STIGForge.Build/`, `src/STIGForge.Content/`, `src/STIGForge.Apply/`, `src/STIGForge.Verify/`, `src/STIGForge.Export/`
- Contains:
  - `STIGForge.Build.BundleBuilder` - Creates deployable bundles with compiled controls and templates
  - `STIGForge.Content.Import.ContentPackImporter` - Parses STIG/SCAP/GPO formats, detects format, validates controls
  - `STIGForge.Apply.ApplyRunner` - Executes PowerStig DSC, manages snapshots, coordinates reboots
  - `STIGForge.Verify.VerificationWorkflowService` - Orchestrates Evaluate-STIG and SCAP verification runners
  - `STIGForge.Export.EmassExporter` - Generates eMASS-compliant export packages (CKL, POA&M, attestations)
- Depends on: Core domain, infrastructure, repositories
- Used by: Orchestration layer

**Infrastructure/Technical:**
- Purpose: Cross-cutting concerns and system integration
- Location: `src/STIGForge.Infrastructure/`
- Contains:
  - `Storage/` - SQLite repositories, audit trail, credential storage (DPAPI-based)
  - `Hashing/` - SHA256 file/text hashing for integrity verification
  - `Paths/` - Centralized file system path building
  - `System/` - Process execution, reboot coordination, scheduled tasks, remote fleet management
- Depends on: Core domain only
- Used by: Service layer and application layer

## Data Flow

**Content Import & Storage Flow:**
1. User drops STIG/SCAP/GPO ZIP into import inbox
2. ImportInboxScanner detects and stages files
3. ContentPackImporter detects format (Stig/Scap/Gpo) with confidence scoring
4. Parsers extract controls:
   - XccdfParser: XCCDF → ControlRecord
   - OvalParser: OVAL tests → CheckText
   - GpoParser: GPO admx files → FixText
5. ControlRecordContractValidator validates against canonical schema
6. SqliteContentPackRepository persists to content_packs table
7. SqliteJsonControlRepository persists control JSON to controls table

**Bundle Build & Prepare Flow:**
1. User selects profile and content pack
2. BundleBuilder compiles controls via IClassificationScopeService (applies scope/classification filters)
3. ReleaseAgeGate evaluates automation policy (grace period for new controls)
4. ReviewQueue populated for controls needing manual review
5. Bundle directory structure created:
   - Apply/ - PowerStig templates and rollback scripts
   - Manual/ - Answer file templates for manual controls
   - Verify/ - Verification script templates
   - Reports/ - NA scope filter, review queue, automation gate reports
   - Manifest/ - Metadata (bundle ID, profile, pack, timestamps)

**Apply Flow:**
1. ApplyRunner reads manifest and Apply directory
2. SnapshotService captures system state pre-apply
3. If resume detected: RebootCoordinator loads .resume_marker.json
4. PowerStigDataGenerator builds MOF files from bundle settings
5. LcmService applies DSC configuration
6. IdempotencyTracker detects and handles failed resources
7. RebootCoordinator manages scheduled reboots with context persistence
8. RollbackScriptGenerator maintains rollback capability

**Verify Flow:**
1. VerificationWorkflowService orchestrates verification tools
2. EvaluateStigRunner executes Evaluate-STIG CLI tool
3. ScapRunner executes SCAP scanner (oval, scap commands)
4. VerifyReportWriter consolidates CKL output to JSON/CSV
5. VerifyReportWriter builds coverage summary (closed/open counts)

**Export Flow:**
1. EmassExporter reads bundle manifest and verification results
2. Loads manual answers from Manual/answers.json
3. MergeManualAnswers applies manual control findings
4. ConvertToNormalizedResults maps results to NormalizedVerifyResult
5. PoamGenerator creates Plan of Action & Milestones
6. AttestationGenerator creates manual control attestation stubs
7. ControlEvidenceIndex maps controls to evidence artifacts
8. Output structure:
   - 00_Manifest/ - Bundle metadata
   - 01_Scans/ - Verify scan outputs
   - 02_Checklists/ - CKL files
   - 03_POAM/ - POA&M spreadsheets
   - 04_Evidence/ - Evidence files (logs, screenshots)
   - 05_Attestations/ - Manual control attestations
   - 06_Index/ - HTML index and control evidence map

## Key Abstractions

**Repositories (Data Access):**
- Purpose: Abstraction for persistent storage
- Examples: `IContentPackRepository`, `IControlRepository`, `IProfileRepository`, `IOverlayRepository`
- Pattern: SQLite-backed, using Dapper for SQL mapping, JSON serialization for complex types
- Located in: `src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs`

**Service Interfaces (Cross-cutting):**
- Purpose: Provide standardized contracts for infrastructure concerns
- Examples: `IPathBuilder`, `IHashingService`, `IAuditTrailService`, `ICredentialStore`, `IVerificationWorkflowService`
- Pattern: Injected via DI, single responsibility per interface
- Located in: `src/STIGForge.Core/Abstractions/Services.cs`

**Process Abstractions:**
- Purpose: Hide system-level concerns
- Examples: `IProcessRunner`, `IClock`
- Pattern: Testable, mockable for unit testing
- Located in: `src/STIGForge.Core/Abstractions/Process.cs`

**Models & Contracts:**
- Purpose: Domain entities and request/response contracts
- Examples: `ControlRecord`, `Profile`, `ContentPack`, `BundleManifest`, `VerificationWorkflowRequest`
- Pattern: Immutable properties, use of enums for constrained values
- Located in: `src/STIGForge.Core/Models/`

## Entry Points

**Application Entry Point:**
- Location: `src/STIGForge.App/App.xaml.cs` OnStartup
- Triggers: Application start
- Responsibilities:
  - Builds Microsoft.Extensions.Hosting container
  - Registers all services and repositories
  - Configures Serilog logging to file
  - Creates MainWindow and MainViewModel
  - Initializes database via DbBootstrap
  - Handles unhandled exceptions from dispatcher, AppDomain, and task scheduler

**CLI Entry Point:**
- Location: `src/STIGForge.Cli/` Commands namespace
- Triggers: Command-line invocation
- Responsibilities: Parse arguments, dispatch to same service layer

**ViewModel Entry Point:**
- Location: `src/STIGForge.App/MainViewModel.cs`
- Triggers: UI view/command invocations
- Responsibilities:
  - Orchestrates phase transitions (Import → Build → Apply → Verify → Export)
  - Manages UI binding observables (IsDarkTheme, StatusText, etc.)
  - Delegates to service layer for actual work
  - Persists UI state and user preferences

## Error Handling

**Strategy:** Multi-level exception handling with recovery guidance

**Patterns:**
- Startup exceptions trigger error message boxes with guidance for recovery
- Service-level exceptions are logged and propagated as failed task results
- Process failures (Apply/Verify) persist resume context for manual recovery
- Audit trail provides forensic trail of compliance-relevant actions
- ParsingException from content import includes file path and line context
- RebootException blocks automatic resume until operator manually validates context

**Example Handling:**
- ApplyRunner validates bundle exists before execution
- RebootCoordinator validates resume context matches planned steps
- DbBootstrap ensures SQLite schema exists before any repository access
- ContentPackImporter validates ZIP archive integrity before extraction

## Cross-Cutting Concerns

**Logging:**
- Framework: Serilog
- Configuration: File-based rolling daily logs to `%PROGRAMDATA%\STIGForge\logs\stigforge.log`
- Startup trace: Separate file at `%LOCALAPPDATA%\STIGForge\startup-trace.log`
- Level: Information and above
- Pattern: Structured logging with properties in service classes

**Validation:**
- Null checks and guard clauses at service entry points
- Schema validation in repositories (VerifySchema method)
- Control record contract validation in ImportDedupService
- Bundle manifest validation before apply/verify/export operations
- ControlRecordContractValidator ensures canonical format compliance

**Authentication:**
- No application-level auth (Windows-integrated)
- Credentials stored via DPAPI credential store for fleet operations
- ScheduledTaskService uses Windows Task Scheduler (SYSTEM account)
- FleetService uses WinRM credentials from DPAPI store

**Auditing:**
- IAuditTrailService logs compliance-relevant actions with SHA-256 chaining
- Entries include: timestamp, user, machine, action, target, result, detail
- Hash chain provides tamper detection
- Located in: `src/STIGForge.Infrastructure/Storage/AuditTrailService.cs`

---

*Architecture analysis: 2026-02-21*

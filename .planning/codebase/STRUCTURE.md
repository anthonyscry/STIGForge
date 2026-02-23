# Codebase Structure

**Analysis Date:** 2026-02-21

## Directory Layout

```
STIGForge/
├── src/                           # Main source code
│   ├── STIGForge.App/            # WPF application (UI)
│   ├── STIGForge.Core/           # Domain models and abstractions
│   ├── STIGForge.Shared/         # Shared types (minimal)
│   ├── STIGForge.Infrastructure/ # Infrastructure services
│   ├── STIGForge.Content/        # Content import and parsing
│   ├── STIGForge.Build/          # Bundle building
│   ├── STIGForge.Apply/          # DSC application and snapshots
│   ├── STIGForge.Verify/         # Verification orchestration
│   ├── STIGForge.Export/         # Export generators (eMASS, CKL, POA&M)
│   ├── STIGForge.Evidence/       # Evidence collection
│   ├── STIGForge.Reporting/      # Report generation
│   └── STIGForge.Cli/            # Command-line interface
├── tests/                         # Test projects
│   ├── STIGForge.UnitTests/
│   └── STIGForge.IntegrationTests/
├── docs/                          # Documentation
├── tools/                         # Build and utility scripts
├── .github/                       # GitHub actions workflows
├── .planning/                     # GSD planning artifacts
├── STIGForge.sln                 # Solution file
├── global.json                   # SDK version pinning
└── Directory.Build.props         # MSBuild properties
```

## Directory Purposes

**STIGForge.App:**
- Purpose: WPF desktop application - main entry point for end users
- Contains: XAML views, ViewModels, MainWindow, theme resources
- Key files: `App.xaml.cs` (startup), `MainViewModel.cs` (orchestration), `MainWindow.xaml.cs` (UI binding)
- Subdirectories:
  - `Views/` - XAML view files (DashboardView, ImportView, ExportView, etc.)
  - `ViewModels/` - Additional ViewModels (DiffViewerViewModel, ManualCheckWizardViewModel, RebaseWizardViewModel)
  - `Themes/` - DarkTheme.xaml, LightTheme.xaml

**STIGForge.Core:**
- Purpose: Domain layer - models, contracts, and abstraction interfaces
- Contains: Immutable domain models, service interfaces, enums
- Key files: `Models/` (ControlRecord, Profile, ContentPack, etc.), `Abstractions/Services.cs` (interface contracts)
- Subdirectories:
  - `Models/` - ControlRecord, ContentPack, Profile, Overlay, RunManifest, CanonicalContract, Enums
  - `Abstractions/` - Services.cs (interface contracts), Repositories.cs (data access), Process.cs (system abstraction)
  - `Services/` - ManualAnswerService, ClassificationScopeService, ReleaseAgeGate (core business logic)

**STIGForge.Infrastructure:**
- Purpose: Infrastructure layer - system integration, persistence, external services
- Contains: Database access, file paths, process execution, audit trail
- Subdirectories:
  - `Storage/` - SqliteRepositories.cs, AuditTrailService, DpapiCredentialStore, DbBootstrap
  - `Hashing/` - Sha256HashingService
  - `Paths/` - PathBuilder (centralizes application directories)
  - `System/` - ProcessRunner, FleetService, ScheduledTaskService

**STIGForge.Content:**
- Purpose: Content pack import and parsing
- Contains: Parsers for STIG/SCAP/GPO formats, import orchestration
- Key classes: ContentPackImporter, ImportInboxScanner, ImportQueuePlanner
- Subdirectories:
  - `Import/` - ContentPackImporter, XccdfParser, OvalParser, GpoParser, ControlRecordContractValidator, ImportDedupService
  - `Models/` - AdmxPolicy, OvalDefinition, ParsingException
  - `Extensions/` - XmlReaderExtensions

**STIGForge.Build:**
- Purpose: Bundle building - takes content pack + profile, outputs ready-to-apply bundle
- Contains: BundleBuilder (main orchestrator)
- Key classes: BundleBuilder, BundleOrchestrator
- Output: Directory structure with Apply/, Manual/, Verify/, Reports/, Manifest/ subdirs

**STIGForge.Apply:**
- Purpose: DSC application, system snapshots, reboot coordination
- Contains: PowerStig generation, LCM service, snapshot/rollback logic
- Key classes: ApplyRunner, PowerStigDataGenerator, LcmService, SnapshotService, RollbackScriptGenerator, RebootCoordinator
- Subdirectories:
  - `PowerStig/` - PowerStigDataGenerator, PowerStigDataWriter, PowerStigModels, PowerStigValidator
  - `Dsc/` - LcmService, LcmModels
  - `Reboot/` - RebootCoordinator, RebootModels
  - `Snapshot/` - SnapshotService, RollbackScriptGenerator

**STIGForge.Verify:**
- Purpose: Verification workflow orchestration
- Contains: Runner orchestration for Evaluate-STIG and SCAP
- Key classes: VerificationWorkflowService, EvaluateStigRunner, ScapRunner, VerifyOrchestrator
- Reports: CKL consolidation, JSON/CSV output, coverage summary
- Subdirectories:
  - `Adapters/` - Scanner-specific adapters for CKL parsing

**STIGForge.Export:**
- Purpose: Export to compliance formats (eMASS, CKL, POA&M, attestations)
- Contains: Export generators and validators
- Key classes: EmassExporter, CklExporter, PoamGenerator, AttestationGenerator, EmassPackageValidator
- Output: eMASS-compliant directory structure (00_Manifest through 06_Index)

**STIGForge.Evidence:**
- Purpose: Evidence collection and aggregation
- Key classes: EvidenceCollector

**STIGForge.Reporting:**
- Purpose: Report generation and aggregation
- Key classes: BundleMissionSummaryService, VerificationArtifactAggregationService

**STIGForge.Cli:**
- Purpose: Command-line interface for automation
- Contains: Command implementations
- Subdirectories:
  - `Commands/` - Individual command classes

**STIGForge.Shared:**
- Purpose: Shared types across multiple projects
- Contains: Minimal cross-project types

**STIGForge.UnitTests:**
- Purpose: Unit tests for individual components
- Structure mirrors src/ structure (Apply/*, Content/*, Export/*, etc.)
- Key test categories:
  - Apply/ - ApplyRunnerTests, PowerStigDataGeneratorTests, RebootCoordinatorTests, LcmServiceTests
  - Content/ - ImporterTests, ParserTests
  - Export/ - CklExporterTests, EmassExporterTests, PoamExporterTests
  - Storage/ - RepositoryTests, AuditTrailTests, CredentialStoreTests

**STIGForge.IntegrationTests:**
- Purpose: Integration tests for workflows and external tools
- Key test categories:
  - E2E/ - FullPipelineTests (end-to-end import → build → apply → verify → export)
  - Apply/ - SnapshotIntegrationTests
  - Content/ - RoundTripTests (parse → store → retrieve → re-export)
  - Export/ - EmassExporterIntegrationTests, PoamExporterIntegrationTests
  - Cli/ - CliCommandTests, VerifyCommandFlowTests
  - SystemServices/ - FleetServiceIntegrationTests, ScheduledTaskServiceIntegrationTests

## Key File Locations

**Entry Points:**
- `src/STIGForge.App/App.xaml.cs` - Application startup, DI container configuration
- `src/STIGForge.App/MainWindow.xaml.cs` - WPF window code-behind
- `src/STIGForge.Cli/Program.cs` - CLI entry point

**Configuration:**
- `global.json` - .NET SDK version (net8.0)
- `Directory.Build.props` - MSBuild properties and version settings
- `STIGForge.sln` - Solution file listing all 13 projects

**Core Logic:**
- `src/STIGForge.Core/Models/ControlRecord.cs` - Domain model for compliance controls
- `src/STIGForge.Core/Models/Profile.cs` - Configuration profile with automation policies
- `src/STIGForge.Core/Abstractions/Services.cs` - Service interface contracts
- `src/STIGForge.Build/BundleBuilder.cs` - Bundle creation and compilation
- `src/STIGForge.Apply/ApplyRunner.cs` - DSC application orchestrator
- `src/STIGForge.Verify/VerificationWorkflowService.cs` - Verification orchestration
- `src/STIGForge.Export/EmassExporter.cs` - eMASS export generator

**Testing:**
- `tests/STIGForge.UnitTests/` - Unit test suite (Apply, Content, Export, Storage, etc.)
- `tests/STIGForge.IntegrationTests/` - Integration and E2E tests
- `tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs` - End-to-end workflow tests

**Infrastructure:**
- `src/STIGForge.Infrastructure/Storage/SqliteRepositories.cs` - SQLite data access (Dapper-based)
- `src/STIGForge.Infrastructure/Storage/DbBootstrap.cs` - Database schema creation
- `src/STIGForge.Infrastructure/Storage/AuditTrailService.cs` - Compliance audit logging
- `src/STIGForge.Infrastructure/Paths/PathBuilder.cs` - Centralized path construction

## Naming Conventions

**Files:**
- C# classes: PascalCase with .cs extension (e.g., `BundleBuilder.cs`, `ApplyRunner.cs`)
- XAML views: PascalCase with .xaml extension (e.g., `ExportView.xaml`, `MainWindow.xaml`)
- Separate partial class files: `ClassName.AspectName.cs` (e.g., `MainViewModel.ApplyVerify.cs`, `MainViewModel.Export.cs`)
- Test files: `ClassNameTests.cs` or `ClassNameIntegrationTests.cs`
- Interface files: Often grouped with implementations in single file or `ServiceName.cs` for interfaces + implementations

**Directories:**
- Project dirs: Pascal case (e.g., `STIGForge.App`, `STIGForge.Infrastructure`)
- Feature dirs: Pascal case (e.g., `Views/`, `Models/`, `Abstractions/`, `PowerStig/`, `Storage/`)
- Lower case for cross-cutting (e.g., `bin/`, `obj/`)

**Namespaces:**
- Pattern: `STIGForge.{ProjectName}.{Feature?}`
- Examples:
  - `STIGForge.App` - Application layer
  - `STIGForge.Core.Models` - Core domain models
  - `STIGForge.Content.Import` - Content import feature
  - `STIGForge.Apply.PowerStig` - PowerStig subfeature in Apply
  - `STIGForge.Infrastructure.Storage` - Storage infrastructure

**Classes:**
- Concrete services: No suffix or Service suffix (e.g., `BundleBuilder`, `ApplyRunner`, `LcmService`)
- Interfaces: `I` prefix (e.g., `IContentPackRepository`, `IVerificationWorkflowService`)
- Models: Often suffixed with Model, Record, or no suffix (e.g., `ControlRecord`, `RunManifest`, `ApplyResult`)
- Exceptions: Suffixed with `Exception` (e.g., `RebootException`, `ParsingException`)
- ViewModels: Suffixed with `ViewModel` (e.g., `MainViewModel`, `OverlayEditorViewModel`)

## Where to Add New Code

**New Feature (e.g., New Export Format):**
- Primary code: `src/STIGForge.Export/NewFormatExporter.cs`
- Models: `src/STIGForge.Export/ExportModels.cs` (or separate file)
- Unit tests: `tests/STIGForge.UnitTests/Export/NewFormatExporterTests.cs`
- Integration tests: `tests/STIGForge.IntegrationTests/Export/NewFormatExporterIntegrationTests.cs`
- Register in DI: `src/STIGForge.App/App.xaml.cs` ConfigureServices

**New Service/Module (e.g., Notification Service):**
- Interface: `src/STIGForge.Core/Abstractions/Services.cs` (add INotificationService)
- Implementation: `src/STIGForge.Infrastructure/Notifications/NotificationService.cs`
- Models: In same directory or Services.cs if simple
- Tests: `tests/STIGForge.UnitTests/Infrastructure/NotificationServiceTests.cs`
- Register in DI: `src/STIGForge.App/App.xaml.cs` under services.AddSingleton

**New UI Component/View:**
- XAML View: `src/STIGForge.App/Views/NewFeatureView.xaml`
- Code-behind: `src/STIGForge.App/Views/NewFeatureView.xaml.cs`
- ViewModel: `src/STIGForge.App/MainViewModel.NewFeature.cs` (partial class)
- Register in MainViewModel constructor and MainWindow.xaml navigation

**New CLI Command:**
- Command class: `src/STIGForge.Cli/Commands/NewCommand.cs`
- Inherit from appropriate base or implement ICommand pattern
- Register in CLI program setup
- Tests: `tests/STIGForge.IntegrationTests/Cli/NewCommandTests.cs`

**Utilities/Helpers:**
- Shared helpers: `src/STIGForge.Shared/Helpers/HelperName.cs`
- Service-specific helpers: Colocate with service (e.g., `src/STIGForge.Apply/PowerStig/PowerStigValidator.cs`)
- Extension methods: `src/STIGForge.{ProjectName}/Extensions/TypeExtensions.cs`

## Special Directories

**bin/ and obj/:**
- Purpose: Build artifacts
- Generated: Yes (dotnet build)
- Committed: No (in .gitignore)
- Location: Per-project directories

**docs/:**
- Purpose: User-facing and architecture documentation
- Generated: No (manually written)
- Committed: Yes
- Contains: Markdown docs, diagrams, user guides

 **tools/:**
- Purpose: Build scripts, code generation, utility tools
- Generated: No
- Committed: Yes
- Contains: PowerShell scripts, build helpers

**.github/workflows/:**
- Purpose: CI/CD automation
- Generated: No
- Committed: Yes
- Contains: GitHub Actions workflow files for build, test, release

**.planning/:**
- Purpose: GSD orchestration planning artifacts
- Generated: By GSD commands
- Committed: Yes
- Contains: REQUIREMENTS.md, ROADMAP.md, STATE.md, phases/, codebase/

**.artifacts/:**
- Purpose: Test outputs, build reports, release gates
- Generated: Yes (during CI/testing)
- Committed: No
- Contains: release-gate/, quarterly-pack/

**import/, .stigforge/, .sisyphus/, .worktrees/:**
- Purpose: Application data, runtime state
- Generated: Yes (at runtime)
- Committed: No

---

*Structure analysis: 2026-02-21*

# STIGForge Architecture

## Overview

STIGForge is an offline-first Windows STIG hardening platform built on .NET 8 (with .NET Framework 4.8 compatibility for Core/Infrastructure). It provides a complete compliance lifecycle: **Import -> Build -> Apply -> Verify -> Export**.

## Module Diagram

```
+----------------------------------+     +-------------------------------+
|          STIGForge.App           |     |        STIGForge.Cli          |
|        (WPF Desktop App)         |     |     (Command-Line Interface)  |
|   13 tabs, MVVM, CommunityToolkit|     |   37 commands, System.CommandLine|
+----------------------------------+     +-------------------------------+
        |                                          |
        v                                          v
+--------------------------------------------------------------+
|                    STIGForge.Build                            |
|   BundleBuilder, BundleOrchestrator                          |
|   Orchestrates: Apply -> Verify -> Export                    |
+--------------------------------------------------------------+
        |              |              |              |
        v              v              v              v
+-------------+ +-------------+ +-----------+ +-----------+
| .Content    | | .Apply      | | .Verify   | | .Export   |
| XCCDF/SCAP  | | DSC/Scripts | | SCAP/CKL  | | eMASS    |
| GPO import  | | PowerSTIG   | | Evaluate- | | POAM     |
| Parsing     | | Snapshot    | | STIG      | | CKL      |
|             | | Rollback    | | Overlap   | | Attestation|
+-------------+ +-------------+ +-----------+ +-----------+
        |              |              |              |
        v              v              v              v
+--------------------------------------------------------------+
|                    STIGForge.Core                             |
|   Models: ControlRecord, Profile, Overlay, ContentPack       |
|   Abstractions: IClock, IAuditTrailService, IPathBuilder,    |
|     IHashingService, IClassificationScopeService,            |
|     ICredentialStore                                         |
|   Services: ManualAnswerService, OverlayRebaseService,       |
|     BaselineDiffService, ClassificationScopeService          |
+--------------------------------------------------------------+
        |
        v
+--------------------------------------------------------------+
|                  STIGForge.Infrastructure                     |
|   Storage: SQLite repos (Dapper), AuditTrailService,         |
|     DpapiCredentialStore, DbBootstrap                        |
|   System: FleetService, ScheduledTaskService, ProcessRunner  |
|   Hashing: Sha256HashingService                              |
|   Paths: PathBuilder                                         |
+--------------------------------------------------------------+
        |
        v
+--------------------------------------------------------------+
|                  STIGForge.Evidence                           |
|   EvidenceCollector: file/text/command evidence with SHA-256  |
+--------------------------------------------------------------+
```

## Data Flow

### Import Phase
1. User provides a DISA content pack (ZIP containing XCCDF/SCAP/GPO data)
2. `ContentPackImporter` extracts and parses with `XccdfParser`
3. Controls are persisted to SQLite via `SqliteJsonControlRepository`
4. Pack metadata saved via `SqliteContentPackRepository`

### Build Phase
1. User selects a pack and profile (hardening mode, classification, overlays)
2. `ClassificationScopeService` compiles controls against the profile
3. `BundleBuilder` creates an on-disk bundle directory:
   - `Manifest/` — manifest.json, pack_controls.json
   - `Apply/` — RunApply.ps1, rollback scripts
   - `Verify/` — placeholders for scan results
   - `Manual/` — manual_answers.json
   - `Evidence/` — evidence artifacts
   - `Reports/` — automation_gate.json

### Apply Phase
1. `ApplyRunner` executes hardening via DSC MOFs, PowerSTIG, and/or custom scripts
2. `SnapshotService` captures before/after system state (registry, audit policy, services)
3. `RollbackScriptGenerator` creates undo scripts
4. Audit trail entry recorded

### Verify Phase
1. External tools (Evaluate-STIG, SCAP/SCC) run against the system
2. Results (CKL files) are parsed by `ScapResultAdapter` / `CklResultAdapter`
3. `VerifyOrchestrator` merges multi-tool results with conflict detection
4. `VerifyReportWriter` outputs consolidated-results.json/csv

### Export Phase
1. `EmassExporter` builds complete eMASS submission package (7 subdirectories)
2. `StandalonePoamExporter` generates POA&M from open findings
3. `CklExporter` generates STIG Viewer-compatible CKL XML
4. `AttestationGenerator` creates attestation templates
5. `EmassPackageValidator` validates package completeness

## Storage Architecture

### SQLite Database
- Location: `%ProgramData%/STIGForge/data/stigforge.db`
- ORM: Dapper with custom `DateTimeOffsetHandler`
- Schema managed by `DbBootstrap.EnsureCreated()`

**Tables:**
| Table | Purpose |
|-------|---------|
| `content_packs` | Imported STIG pack metadata |
| `controls` | Pack controls as JSON blobs (pack_id + control_id PK) |
| `profiles` | Hardening profiles as JSON |
| `overlays` | Override overlays as JSON |
| `audit_trail` | Tamper-evident audit log with chained SHA-256 hashes |

### File-Based Storage
- **Bundles**: `%ProgramData%/STIGForge/bundles/{bundleId}/`
- **Content Packs**: `%ProgramData%/STIGForge/packs/{packId}/`
- **Credentials**: `%ProgramData%/STIGForge/credentials/*.cred` (DPAPI encrypted)
- **Logs**: `%ProgramData%/STIGForge/logs/` (Serilog rolling daily)

## Audit Trail

The audit trail uses chained SHA-256 hashing for tamper evidence:

1. Each entry's hash includes the previous entry's hash ("genesis" for the first)
2. Hash payload: `timestamp|user|machine|action|target|result|detail|previousHash`
3. `VerifyIntegrityAsync()` walks the chain validating each link
4. Audit events are recorded at key compliance points (apply, verify, export, import, rebase, manual-answer)

## Fleet Architecture

Fleet operations use WinRM/PSRemoting for multi-machine hardening:

1. `FleetService` manages parallel execution across targets
2. `SemaphoreSlim` controls concurrency (default: 5)
3. `DpapiCredentialStore` provides per-user encrypted credential storage
4. `ResolveCredentials()` transparently loads stored credentials for targets

## Design Decisions

### Dual-Framework Targeting
Core and Infrastructure target both `net8.0` and `net48` to support legacy Windows environments. This imposes API constraints (no `String.Contains(StringComparison)`, no `Dictionary.TryAdd`, etc.).

### Offline-First
All operations work without network connectivity. The platform stores everything locally (SQLite + file system) and never phones home.

### Modular Library Architecture
Each concern (Content, Build, Apply, Verify, Export, Evidence) is a separate .NET project, enabling independent testing and deployment.

### Optional Dependencies
Services like `IAuditTrailService` and `ICredentialStore` are injected as optional constructor parameters (`= null`) to maintain backward compatibility while enabling new features.

### MVVM Pattern (WPF)
The WPF app uses CommunityToolkit.Mvvm source generators with partial classes for view models (e.g., `MainViewModel.cs` + `MainViewModel.Import.cs` + `MainViewModel.Dashboard.cs` etc.).

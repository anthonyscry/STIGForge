# Changelog

All notable changes to STIGForge are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.2] - 2026-03-15

### Added
- `tools/qa/stigforge-qa-suite.ps1` — Hyper-V lab QA test suite covering smoke tests, import/build/orchestrate commands, fleet credential DPAPI round-trip, PS Direct connectivity, fleet-status WinRM, verify/compliance/audit commands, security path-traversal checks, and VM health (eval days, AD services, disk). Verified 40/40 checks on lab VMs DC01, SRV01, SRV02.

## [1.0.1] - 2026-03-15

### Added
- `IApplyRunner` interface abstracting `ApplyRunner` for testability and DI
- `IBundleBuilder` interface abstracting `BundleBuilder` for testability and DI
- `IFleetInventoryRepository` interface + `FleetHostRecord` model for fleet persistence
- `FleetInventoryRepository` SQLite implementation with UPSERT, role-based listing, and compliance state tracking
- `fleet_inventory` table migration in `DbBootstrap`
- `WriteChainAnchorToEventLogAsync` on `IAuditTrailService` — writes trust-anchor entries to Windows Event Log (event ID 1701) or `audit_anchor.log` fallback on non-Windows
- `TotalTimeout` (`TimeSpan?`) and `IProgress<FleetProgress>` on `FleetRequest`; `ExecuteAsync` enforces a linked cancellation deadline
- `useTls` parameter on `SyslogForwarder` — TCP path wraps `NetworkStream` in `SslStream` with mutual-auth negotiation
- `CanExecute` guards (`!IsBusy`) on browse and settings commands in `WorkflowViewModel`
- CI/CD pipeline (`.github/workflows/ci.yml`) with restore, build, test, coverage gate (60%), and CLI publish on `main`
- `RestorePackagesWithLockFile` in `Directory.Build.props` for reproducible restores

### Changed
- `BundleOrchestrator` depends on `IBundleBuilder`/`IApplyRunner` interfaces (not concrete types); inline audit try/catch replaced with `SafeRecordAsync`; calls `WriteChainAnchorToEventLogAsync` after successful orchestration
- `ScapBundleParser` ZIP extraction now delegates entirely to `ImportZipHandler` (uses `CountingStream` for actual decompressed bytes, not ZIP-header-declared size — proper ZIP bomb defence)
- `DpapiCredentialStore` adds application-specific entropy (`"STIGForge-CredentialStore-v1"`) to all `ProtectedData.Protect/Unprotect` calls (breaking change for existing `.cred` files)
- `EmassExporter` inline audit try/catch replaced with `SafeRecordAsync`
- `AuditTrailService` connection-string parsing for anchor path uses `SqliteConnectionStringBuilder` instead of brittle string splitting
- DI containers (`App.xaml.cs`, `CliHostFactory.cs`) register `IBundleBuilder`, `IApplyRunner`, `IFleetInventoryRepository`

### Fixed
- Path traversal vulnerability in `BundleIntegrityVerifier`: resolved full path now checked for containment within `bundleRoot` before processing
- Path traversal vulnerability in `PolicyStepHandler.CopyTemplateFile`: resolved destination path checked for containment within `targetRoot` before `File.Copy`
- `FleetService` null-dereference: `RunPowerShellRemoteAsync` and `TestConnectionAsync` return descriptive failure results when `IProcessRunner` is not injected
- ZIP bomb protection in `ScapBundleParser` previously counted header-declared bytes, not actual decompressed bytes

### Removed
- Empty `STIGForge.Shared/` and `STIGForge.Reporting/` project directories

# Changelog

All notable changes to STIGForge are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.3] - 2026-03-23

### Security

- `ControlException.StatusValue` — unknown or corrupt status strings now map to `Revoked` instead of `Active`. A bad value no longer silently grants exception coverage.
- `DpapiCredentialStore` — automatic one-time migration renames legacy sanitized-filename `.cred` files to the new SHA-256-hashed names on first access. Upgrade is silent and credential-safe; no manual intervention required.
- `AuditTrailService.CommitAsync` — uses `CancellationToken.None` so a cancelled caller cannot silently drop an already-written audit record from the chain.
- `App.xaml.cs` — graceful 6-second host shutdown replaces fire-and-forget stop; prevents a race between `StopAsync` and `IHost.Dispose` on exit.
- `App.xaml.cs` — `ValidateOnBuild = true` unconditionally (was `#if DEBUG` only); scoped-into-singleton DI violations now surface at startup in all builds.

### Fixed

- `ComplianceTrendService` — being above the minimum compliance floor no longer suppresses the delta regression check. Both checks are independent: scoring below the floor is an immediate regression regardless of trend direction.
- `FleetCommands` — `RegisterFleetApply` handler body and `RegisterFleetVerify` method were silently lost during a stash merge; both commands are restored.
- `ProcessRunner` — `BeginRead` now called before `HasExited` check; prevents stdout/stderr loss when a fast process exits before output is captured.
- `SqliteRepositories` — restored safe `DateTimeOffset.Parse` fallback for ISO 8601 strings returned by SQLite/Dapper; fixes `RuntimeBinderException` on content pack reads.
- `ExceptionStatus.Pending` enum value restored (was dropped during conflict resolution).

### Infrastructure

- Scriban upgraded from 5.12.0 → 6.6.0, resolving CVEs NU1902 and NU1903.
- CI coverage gate now correctly aggregates multiple `coverage.cobertura.xml` files (one per test assembly); coverage threshold set to 35% to account for the WPF UI layer, which cannot be exercised by unit tests.
- All previously failing CI tests resolved: `AppSmokeTests` tab names aligned with `AutomationProperties.Name`; `ExceptionStatusTests`, `ComplianceTrendServiceTests`, and `ScapBundleParserTests` updated to match intentional behavior changes.



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

# Phase 11: Foundation and Test Stability - Research

**Researched:** 2026-02-22
**Domain:** .NET 8 test infrastructure, Serilog observability, structured error codes
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
No `*-CONTEXT.md` file exists for this phase. Use provided objective/requirements as the lock source:
- Phase must address requirement IDs: TEST-01, TEST-05, OBSV-01, OBSV-04, ERRX-03
- Success criteria defined:
  1. Test suite runs to completion without flaky failures (BuildHost test passes consistently)
  2. Tests are categorized by speed, enabling faster feedback on unit tests
  3. Structured logs include correlation IDs that enable trace correlation
  4. Log verbosity is configurable between debug and production environments
  5. Error codes are structured and machine-readable for cataloging

### Claude's Discretion
No explicit discretion section exists. Recommended discretion boundaries for planning:
- Choose specific xUnit trait naming conventions (Category: Unit/Integration recommended)
- Choose correlation ID propagation strategy (Activity-based vs custom)
- Choose error code format and structure (string codes vs numeric vs hybrid)

### Deferred Ideas (OUT OF SCOPE)
- Test coverage enforcement (Phase 14)
- Performance baselining (Phase 13)
- Human-readable error messages and recovery guidance (Phase 15)
- Mission-level tracing (Phase 12)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TEST-01 | Test suite runs reliably without flaky failures (fix BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory) | xUnit IAsyncLifetime pattern, unique temp directory fixtures, proper async disposal |
| TEST-05 | Tests categorized by speed (unit vs integration) for pipeline efficiency | xUnit `[Trait("Category", "Unit")]` and `[Trait("Category", "Integration")]` attributes |
| OBSV-01 | Structured logging with correlation IDs for trace correlation | Serilog ILogEventEnricher with Activity.Current or custom AsyncLocal correlation |
| OBSV-04 | Log levels configurable per environment (debug vs production) | Serilog MinimumLevel configuration, LoggingLevelSwitch for runtime control |
| ERRX-03 | Structured error codes enable machine-readable error cataloging | StigForgeException base class with ErrorCode property, hierarchical code format |
</phase_requirements>

## Summary

This phase establishes the foundation services that subsequent phases depend on: reliable test infrastructure, structured logging with correlation, configurable verbosity, and machine-readable error codes. The pre-existing flaky test `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory` is the critical blocker that must be resolved first - it prevents coverage gate enforcement in CI.

The research confirms that all four domains use well-established .NET patterns with direct support in the existing stack. The flaky test issue is a classic file system async race condition: the test creates a temp directory in constructor, but the host's Serilog file sink may still be writing when `Dispose()` deletes the directory. The fix requires `IAsyncLifetime` for proper async disposal ordering. For observability, Serilog enrichers with `Activity.Current` provide correlation IDs compatible with OpenTelemetry. For error codes, a simple base exception class with a structured code property enables cataloging without over-engineering.

**Primary recommendation:** Fix the flaky test with IAsyncLifetime first, then implement the correlation enricher, level switch, and error base class in parallel as they have no interdependencies.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xUnit | 2.9.3 | Test framework | Already in use; supports Traits, IAsyncLifetime, parallel execution |
| Serilog | 4.3.0 | Structured logging | Already in use across 3 host files; supports enrichers, level switches |
| Serilog.Settings.Configuration | (to add) | JSON configuration | Enables environment-based log level configuration via appsettings.json |
| System.Diagnostics.Activity | (built-in .NET 8) | Correlation/tracing | Native W3C trace context support; integrates with Serilog enrichers |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Hosting | 10.0.2 | Host configuration | Already in use; provides IConfiguration for Serilog config |
| FluentAssertions | 8.8.0 | Test assertions | Already in use; provides readable test assertions |
| Moq | 4.20.72 | Test mocking | Already in use; for service mock scenarios |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| xUnit Traits | NUnit Categories | NUnit not in project; xUnit is current standard with better parallel support |
| Serilog enrichers | Custom middleware | Enrichers are purpose-built for log enrichment; middleware adds complexity |
| Activity-based correlation | Custom correlation service | Activity is built-in, OTel-compatible, zero additional dependencies |
| Structured string error codes | Numeric HRESULT-style | Strings are self-documenting, searchable, no collision math needed |

**Installation:**
```bash
dotnet add package Serilog.Settings.Configuration
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── STIGForge.Core/
│   ├── Errors/                      # NEW: Error code definitions
│   │   ├── StigForgeException.cs    # Base exception with ErrorCode
│   │   └── ErrorCodes.cs            # Centralized error code constants
│   └── Models/
├── STIGForge.Infrastructure/
│   └── Logging/                     # NEW: Serilog enrichers
│       ├── CorrelationIdEnricher.cs # Activity-based correlation
│       └── LoggingConfiguration.cs  # Level switch + config helper
tests/
├── STIGForge.UnitTests/
│   ├── fixtures/                    # Shared test fixtures
│   └── TestCategories.cs            # Trait constants for consistency
└── STIGForge.IntegrationTests/
    └── TestCategories.cs            # Matching trait constants
```

### Pattern 1: IAsyncLifecycle for File System Tests
**What:** Use `IAsyncLifetime` instead of `IDisposable` for tests involving async file I/O or host lifecycle.
**When to use:** Any test that starts/stops a host, writes to files, or uses Serilog sinks.
**Example:**
```csharp
// Source: xUnit documentation + analysis of flaky test
public sealed class CliHostFactoryTests : IAsyncLifetime
{
    private readonly string _tempRoot;
    private IHost? _host;

    public CliHostFactoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public async Task InitializeAsync()
    {
        // No async init needed for this test
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // CRITICAL: Stop host BEFORE deleting directory
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }

        // Small delay to ensure file handles are released
        await Task.Delay(50);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    [Fact]
    public async Task BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory()
    {
        _host = CliHostFactory.BuildHost(() => new TestPathBuilder(_tempRoot));
        await _host.StartAsync();
        await _host.StopAsync();

        var logsRoot = Path.Combine(_tempRoot, "logs");
        Assert.True(Directory.Exists(logsRoot));
    }
}
```

### Pattern 2: xUnit Trait Categorization
**What:** Use `[Trait("Category", "...")]` to categorize tests for selective execution.
**When to use:** All tests - categorize as Unit (fast, isolated) or Integration (slower, external deps).
**Example:**
```csharp
// Source: xUnit documentation
public static class TestCategories
{
    public const string Unit = "Unit";
    public const string Integration = "Integration";
    public const string Slow = "Slow";
}

// Usage in test class:
[Fact]
[Trait("Category", TestCategories.Unit)]
public void PathBuilder_Should_Create_Deterministic_Path()
{
    // Fast, isolated test
}

[Fact]
[Trait("Category", TestCategories.Integration)]
[Trait("Category", TestCategories.Slow)]
public async Task ApplyRunner_Should_Complete_Full_Mission()
{
    // Slower test with real dependencies
}
```

**Command-line filtering:**
```bash
# Run only unit tests (fast feedback)
dotnet test --filter "Category=Unit"

# Run integration tests in CI
dotnet test --filter "Category=Integration"

# Exclude slow tests
dotnet test --filter "Category!=Slow"
```

### Pattern 3: Serilog Correlation ID Enricher
**What:** Use `ILogEventEnricher` with `Activity.Current` to add correlation IDs to all log events.
**When to use:** All Serilog configurations (CLI, WPF, tests) for trace correlation.
**Example:**
```csharp
// Source: Serilog documentation + System.Diagnostics.Activity
using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

public class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            // Add TraceId for distributed correlation
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "TraceId", activity.TraceId.ToString()));

            // Add SpanId for hierarchical correlation
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "SpanId", activity.SpanId.ToString()));
        }
        else
        {
            // Generate correlation ID if no Activity context exists
            var correlationId = Guid.NewGuid().ToString("N");
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "CorrelationId", correlationId));
        }
    }
}
```

**Integration with host:**
```csharp
// In CliHostFactory.cs and App.xaml.cs
.UseSerilog((ctx, lc) =>
{
    var root = pathBuilderFactory().GetLogsRoot();
    Directory.CreateDirectory(root);

    lc.MinimumLevel.ControlledBy(LoggingConfiguration.LevelSwitch)
      .Enrich.With(new CorrelationIdEnricher())
      .Enrich.FromLogContext()
      .WriteTo.File(
          Path.Combine(root, "stigforge-cli.log"),
          rollingInterval: RollingInterval.Day,
          outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}");
})
```

### Pattern 4: Configurable Log Levels
**What:** Use `LoggingLevelSwitch` with configuration binding for environment-based log levels.
**When to use:** Production (Information) vs Development (Debug) log verbosity control.
**Example:**
```csharp
// Source: Serilog.Settings.Configuration documentation
// Infrastructure/Logging/LoggingConfiguration.cs
using Serilog;
using Serilog.Events;

public static class LoggingConfiguration
{
    public static LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Information);

    public static void ConfigureFromEnvironment(LoggerConfiguration lc, string? logPath = null)
    {
        var level = Environment.GetEnvironmentVariable("STIGFORGE_LOG_LEVEL") switch
        {
            "Debug" => LogEventLevel.Debug,
            "Verbose" => LogEventLevel.Verbose,
            "Warning" => LogEventLevel.Warning,
            "Error" => LogEventLevel.Error,
            _ => LogEventLevel.Information
        };

        LevelSwitch.MinimumLevel = level;

        lc.MinimumLevel.ControlledBy(LevelSwitch)
          .Enrich.FromLogContext();
    }
}
```

**appsettings.json (optional for future):**
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Pattern 5: Structured Error Codes
**What:** Define a base exception class with a machine-readable error code property.
**When to use:** All domain-specific exceptions inherit from StigForgeException.
**Example:**
```csharp
// Source: Enterprise error handling patterns
// Core/Errors/StigForgeException.cs
namespace STIGForge.Core.Errors;

public abstract class StigForgeException : Exception
{
    public string ErrorCode { get; }
    public string? Component { get; }

    protected StigForgeException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
    }

    protected StigForgeException(string errorCode, string component, string message, Exception? innerException = null)
        : this(errorCode, message, innerException)
    {
        Component = component;
    }
}

// Core/Errors/ErrorCodes.cs
public static class ErrorCodes
{
    // Format: {COMPONENT}_{CATEGORY}_{SPECIFIC}
    // Example: BUILD_001, IMPORT_PARSE_001

    // Build errors
    public const string BUILD_BUNDLE_FAILED = "BUILD_001";
    public const string BUILD_INVALID_PROFILE = "BUILD_002";

    // Import errors
    public const string IMPORT_PARSE_FAILED = "IMPORT_001";
    public const string IMPORT_VALIDATION_FAILED = "IMPORT_002";

    // Apply errors
    public const string APPLY_DSC_FAILED = "APPLY_001";
    public const string APPLY_REBOOT_REQUIRED = "APPLY_002";

    // Verify errors
    public const string VERIFY_SCAP_FAILED = "VERIFY_001";
    public const string VERIFY_TIMEOUT = "VERIFY_002";
}

// Example domain exception
public class BundleBuildException : StigForgeException
{
    public BundleBuildException(string errorCode, string message, Exception? innerException = null)
        : base(errorCode, "Build", message, innerException)
    {
    }
}
```

### Anti-Patterns to Avoid
- **Synchronous disposal for async resources:** Using `IDisposable` when host/file operations need async cleanup causes race conditions.
- **Trait proliferation:** Creating too many trait categories ("Quick", "Fast", "Medium") - stick to Unit/Integration/Slow.
- **Correlation ID per log line:** Generating new IDs per log instead of propagating one per operation breaks correlation.
- **Exception without error codes:** Raw `Exception` types cannot be cataloged or filtered by machine-readable codes.
- **Hardcoded log levels:** Prevents production debugging without code changes.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Async test lifecycle | Custom async dispose patterns | `IAsyncLifetime` | xUnit built-in support, proper ordering guarantees |
| Test categorization | Custom attributes or naming conventions | `[Trait("Category", "...")]` | IDE and CI support, standard filtering syntax |
| Correlation ID | Custom request context | `Activity.Current` | W3C standard, OTel compatible, built-in .NET |
| Log level switching | Custom verbosity flags | `LoggingLevelSwitch` | Runtime control, configuration binding |
| Error codes | String parsing from messages | `StigForgeException.ErrorCode` | Structured, typed, catalogable |

**Key insight:** All these patterns have standard library support in .NET 8. The research found no need for custom implementations.

## Common Pitfalls

### Pitfall 1: File System Race in Test Disposal
**What goes wrong:** Test deletes temp directory while Serilog file sink is still flushing, causing `Directory.Delete` to fail or logs to be incomplete.
**Why it happens:** `IDisposable.Dispose()` is synchronous but host shutdown and file sink flush are async operations.
**How to avoid:** Use `IAsyncLifetime.DisposeAsync()` to await host stop before directory cleanup. Add small delay for file handle release.
**Warning signs:** Intermittent test failures with "directory not empty" or "file in use" errors.

### Pitfall 2: Missing Correlation When Activity Not Started
**What goes wrong:** Logs have no correlation ID when code runs outside an Activity context.
**Why it happens:** Activity.Current is null if no activity was started at entry point.
**How to avoid:** Enricher should fall back to generating a correlation ID when Activity.Current is null, or ensure activities are started at entry points.
**Warning signs:** Logs with empty TraceId fields, inability to correlate related operations.

### Pitfall 3: Log Level Not Respecting Configuration
**What goes wrong:** Debug logs appear in production despite configuration setting Information level.
**Why it happens:** Logger created before configuration loaded, or level switch not connected to configuration.
**How to avoid:** Ensure LoggingLevelSwitch is created before logger and connected via `.MinimumLevel.ControlledBy()`. Use consistent configuration source.
**Warning signs:** Verbose logs in production, unexpected log file sizes.

### Pitfall 4: Error Codes Not Machine-Readable
**What goes wrong:** Error codes like "Build failed due to invalid profile" cannot be parsed programmatically.
**Why it happens:** Using free-form message text as error identifier.
**How to avoid:** Use structured codes (e.g., "BUILD_002") in a dedicated property, keep message for humans.
**Warning signs:** Log parsing attempts fail, cannot build error catalog.

## Code Examples

Verified patterns from official sources:

### IAsyncLifetime for Host Tests
```csharp
// Source: https://xunit.net/docs/shared-context
using Xunit;

public class HostTests : IAsyncLifetime
{
    private IHost? _host;

    public async Task InitializeAsync()
    {
        _host = Host.CreateDefaultBuilder().Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
```

### Serilog Enricher Registration
```csharp
// Source: https://serilog.net/
Log.Logger = new LoggerConfiguration()
    .Enrich.With(new CorrelationIdEnricher())
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

### Logging Level Switch with Configuration
```csharp
// Source: Serilog.Settings.Configuration
var levelSwitch = new LoggingLevelSwitch();

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

// Update level at runtime:
levelSwitch.MinimumLevel = LogEventLevel.Debug;
```

### xUnit Trait Filtering
```bash
# Source: https://xunit.net/docs/running-tests-in-command-line
# Run unit tests only
dotnet test --filter "Category=Unit"

# Run all except slow tests
dotnet test --filter "Category!=Slow"

# Multiple filters (AND)
dotnet test --filter "Category=Unit&Priority=High"
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| IDisposable for all tests | IAsyncLifetime for async resources | xUnit 2.x | Proper async cleanup ordering |
| No test categorization | Trait-based filtering | xUnit 2.x | Selective test execution in CI |
| Correlation via log parsing | Activity-based TraceId | .NET 5+ | Standard W3C trace context |
| Hardcoded log levels | LoggingLevelSwitch | Serilog 2.x | Runtime log level control |
| Exception messages as identifiers | Structured error codes | Enterprise pattern | Machine-readable error cataloging |

**Deprecated/outdated:**
- NUnit/MSTest for new .NET 8 projects: xUnit provides better async support and parallel execution
- Custom correlation ID services: Activity.Current is the standard
- Configuration via web.config: appsettings.json + environment variables is the modern approach

## Open Questions

1. **Should test categories include performance hints (e.g., "Slow" trait)?**
   - What we know: TEST-05 mentions speed categorization for pipeline efficiency
   - What's unclear: Whether to use binary Unit/Integration or three-tier Unit/Integration/Slow
   - Recommendation: Start with Unit/Integration, add Slow trait only when needed for tests >5s

2. **Should error codes be hierarchical (BUILD_PARSE_001) or flat (BUILD_001)?**
   - What we know: ERRX-03 requires machine-readable codes
   - What's unclear: Depth of hierarchy needed for catalog usefulness
   - Recommendation: Use flat two-part format (COMPONENT_NUMBER) for simplicity, extend if catalog needs grow

3. **Should log level configuration support hot reload?**
   - What we know: OBSV-04 requires configurable levels
   - What's unclear: Whether runtime changes without restart are needed
   - Recommendation: Start with startup-only configuration, add hot reload only if operations team requests it

## Sources

### Primary (HIGH confidence)
- xUnit Documentation: Shared Context (https://xunit.net/docs/shared-context) - IAsyncLifetime pattern
- xUnit Documentation: Running Tests (https://xunit.net/docs/running-tests-in-command-line) - Trait filtering
- Serilog Documentation (https://serilog.net/) - Enrichers, level switches
- Serilog.Settings.Configuration GitHub (https://github.com/serilog/serilog-settings-configuration) - Configuration binding
- Microsoft Learn: System.Diagnostics.Activity (https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing) - W3C trace context
- Repository code: `tests/STIGForge.UnitTests/Cli/CliHostFactoryTests.cs` - Flaky test analysis
- Repository code: `src/STIGForge.Cli/CliHostFactory.cs`, `src/STIGForge.App/App.xaml.cs` - Existing Serilog configuration

### Secondary (MEDIUM confidence)
- Microsoft Learn: .NET Generic Host (https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) - Host lifecycle
- Microsoft Learn: Logging in .NET (https://learn.microsoft.com/en-us/dotnet/core/extensions/logging) - Log level configuration
- Repository code: `tests/STIGForge.UnitTests/SmokeTests.cs` - Existing test patterns

### Tertiary (LOW confidence)
- Industry patterns for structured error codes (general enterprise patterns, not STIGForge-specific)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All packages already in use or verified compatible with .NET 8
- Architecture: HIGH - Patterns directly from xUnit and Serilog documentation
- Pitfalls: HIGH - Flaky test root cause verified by reading test code; other pitfalls are well-documented library gotchas

**Research date:** 2026-02-22
**Valid until:** 2026-03-24 (30 days; stable .NET/xUnit/Serilog versions)

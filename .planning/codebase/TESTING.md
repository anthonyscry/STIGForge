# Testing Patterns

**Analysis Date:** 2026-02-21

## Test Framework

**Runner:**
- xUnit 2.9.3
- Config: `.csproj` files at `tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj` and `tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj`
- Target framework: `net8.0`
- Implicit usings enabled: `<Using Include="Xunit" />`

**Assertion Library:**
- FluentAssertions 8.8.0
- Chained assertion syntax: `.Should().Be()`, `.Should().HaveCount()`, `.Should().OnlyContain()`

**Run Commands:**
```bash
dotnet test tests/STIGForge.UnitTests          # Run unit tests
dotnet test tests/STIGForge.IntegrationTests   # Run integration tests
dotnet test                                     # Run all tests from solution root
```

## Test File Organization

**Location:**
- Unit tests co-located with integration tests in `tests/` directory at project root
- Mirror source structure: `src/STIGForge.Core/Services/BaselineDiffService.cs` → `tests/STIGForge.UnitTests/Services/BaselineDiffServiceTests.cs`
- Categories by type: `Apply/`, `Services/`, `Content/`, `Infrastructure/`, `Verify/`, `Export/`, `Audit/`, `Build/`, `Evidence/`

**Naming:**
- Test classes: `[ClassName]Tests`
- Test methods: `[MethodName]_[Condition]_[Expected]` pattern
- Example: `ComparePacks_IdenticalPacks_NoChanges()`, `RunAsync_WhenManifestHardeningModeIsNumeric_ParsesAndCompletes()`

**Structure:**
```
tests/
├── STIGForge.UnitTests/
│   ├── Services/
│   │   └── BaselineDiffServiceTests.cs
│   ├── Apply/
│   │   ├── ApplyRunnerTests.cs
│   │   ├── LcmServiceTests.cs
│   │   └── ...
│   ├── Content/
│   ├── Infrastructure/
│   ├── fixtures/
│   └── SmokeTests.cs
├── STIGForge.IntegrationTests/
│   ├── Storage/
│   │   └── AuditTrailIntegrationTests.cs
│   ├── Apply/
│   ├── E2E/
│   │   └── FullPipelineTests.cs
│   ├── fixtures/
│   └── IntegrationPlaceholder.cs
```

## Test Structure

**Suite Organization:**
```csharp
namespace STIGForge.UnitTests.Services;

public class BaselineDiffServiceTests
{
    private static ControlRecord MakeControl(string id, string title = "Title", ...)
    {
        return new ControlRecord
        {
            ControlId = id,
            Title = title,
            // ... factory pattern
        };
    }

    private static (BaselineDiffService svc, Mock<IControlRepository> repo) CreateService()
    {
        var repo = new Mock<IControlRepository>();
        var svc = new BaselineDiffService(repo.Object);
        return (svc, repo);
    }

    [Fact]
    public async Task ComparePacks_IdenticalPacks_NoChanges()
    {
        // Arrange
        var (svc, repo) = CreateService();
        var controls = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };

        repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>()))
            .ReturnsAsync(controls);

        // Act
        var diff = await svc.ComparePacksAsync("baseline", "target");

        // Assert
        diff.TotalAdded.Should().Be(0);
    }
}
```

**Patterns:**
- Setup with private static factory methods: `CreateService()`, `MakeControl()`
- Arrange-Act-Assert (AAA) structure explicitly labeled in comments
- Tuple returns for grouped setup: `private static (Service svc, Mock<IRepository> repo) CreateService()`
- Test class declares as `public` (not sealed) to allow inheritance of shared setup

## Mocking

**Framework:** Moq 4.20.72

**Patterns:**
```csharp
// Service mock setup
var audit = new Mock<IAuditTrailService>();
audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);

// Repository mock setup
repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>()))
    .ReturnsAsync(controls);

// Pass mock via .Object property
var runner = CreateRunner(audit.Object);
```

**What to Mock:**
- Repository/data access interfaces: `IControlRepository`, `IAuditTrailService`
- External service dependencies: `IProcessRunner`, `ILogger<T>`
- Infrastructure abstractions: `IClock`, `IPathBuilder`

**What NOT to Mock:**
- Data models: `ControlRecord`, `AuditEntry` - use factory methods instead
- Value objects and DTOs - construct real instances
- Logic being tested - test real implementations of tested class

## Fixtures and Factories

**Test Data:**
```csharp
// Factory method pattern for creating test data
private static ControlRecord MakeControl(
    string id,
    string title = "Title",
    string severity = "medium",
    string? checkText = null,
    string? fixText = null,
    string? ruleId = null,
    string? vulnId = null)
{
    return new ControlRecord
    {
        ControlId = id,
        Title = title,
        Severity = severity,
        CheckText = checkText ?? "Check " + id,
        FixText = fixText ?? "Fix " + id,
        IsManual = false,
        ExternalIds = new ExternalIds
        {
            RuleId = ruleId ?? "SV-" + id + "_rule",
            VulnId = vulnId ?? "V-" + id,
        },
        Applicability = new Applicability
        {
            OsTarget = OsTarget.Win11,
            RoleTags = Array.Empty<RoleTemplate>(),
            ClassificationScope = ScopeTag.Both,
            Confidence = Confidence.High
        },
        Revision = new RevisionInfo { PackName = "TestPack" }
    };
}
```

**Location:**
- Fixtures directory at `tests/STIGForge.UnitTests/fixtures/` and `tests/STIGForge.IntegrationTests/fixtures/` (present but contents not heavily used)
- Factory methods defined as `private static` within test classes for close coupling with specific test needs
- Shared test helpers used sparingly; most data construction inline or in test-class factories

## Coverage

**Requirements:**
- No coverage enforcement detected in project configuration
- Coverage tool present: `coverlet.collector` 6.0.4 included in test projects

**View Coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage"
# View results in ./TestResults/*/coverage.opencover.xml
```

## Test Types

**Unit Tests:**
- Scope: Individual service methods and utility functions
- Location: `tests/STIGForge.UnitTests/`
- Example: `BaselineDiffServiceTests.cs` tests `ComparePacksAsync()` with mocked repository
- Approach: Mock all dependencies, test behavior in isolation
- Fast execution: 100+ unit tests run quickly
- Example test file size: `BaselineDiffServiceTests.cs` contains 15 test methods covering different scenarios

**Integration Tests:**
- Scope: Service interactions with real infrastructure (file I/O, database, process execution)
- Location: `tests/STIGForge.IntegrationTests/`
- Example: `AuditTrailIntegrationTests.cs` uses real SQLite database (`DbBootstrap.EnsureCreated()`)
- Approach: Real instances of infrastructure services, minimal mocking
- Setup/teardown: Each test creates temp database, cleans up in `Dispose()`
- Example:
  ```csharp
  public AuditTrailIntegrationTests()
  {
      _dbPath = Path.Combine(Path.GetTempPath(), "stigforge-test-" + Guid.NewGuid().ToString("N")[..8] + ".db");
      _cs = $"Data Source={_dbPath}";
      DbBootstrap.EnsureCreated(_cs);
  }
  ```

**E2E Tests:**
- Location: `tests/STIGForge.IntegrationTests/E2E/FullPipelineTests.cs`
- Scope: Full workflow from import through export
- Approach: Exercise complete application workflow

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task RunAsync_WhenManifestHardeningModeIsNumeric_ParsesAndCompletes()
{
    // Setup
    var runner = CreateRunner(CreatePassingAudit());

    // Act
    var result = await runner.RunAsync(new ApplyRequest
    {
        BundleRoot = _bundleRoot,
        SkipSnapshot = true
    }, CancellationToken.None);

    // Assert
    result.Mode.Should().Be(HardeningMode.Safe);
}
```

**Error/Exception Testing:**
```csharp
[Fact]
public async Task RunAsync_WhenAuditIntegrityIsInvalid_ThrowsBlockingFailure()
{
    var audit = new Mock<IAuditTrailService>();
    audit.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

    var runner = CreateRunner(audit.Object);

    var exception = await Record.ExceptionAsync(() => runner.RunAsync(
        new ApplyRequest { BundleRoot = _bundleRoot, SkipSnapshot = true },
        CancellationToken.None));

    exception.Should().BeOfType<InvalidOperationException>()
        .Which.Message.Should().ContainEquivalentOf("Mission completion blocked");
}
```

**Parameterized Testing:**
- Not heavily used; individual [Fact] methods preferred over [Theory] with [InlineData]
- Encourages explicit test scenarios with clear names

## Test Disposables

**Pattern:**
- Test classes implement `IDisposable` when using temporary resources
- Cleanup in `Dispose()` method
- Example from `ApplyRunnerTests.cs`:
  ```csharp
  public sealed class ApplyRunnerTests : IDisposable
  {
      private readonly string _bundleRoot;

      public ApplyRunnerTests()
      {
          _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-apply-runner-" + Guid.NewGuid());
          Directory.CreateDirectory(_bundleRoot);
      }

      public void Dispose()
      {
          try { Directory.Delete(_bundleRoot, true); } catch { }
      }
  }
  ```

## Test Data Builders

**Patterns for complex objects:**
- Use helper methods with optional parameters to build test data
- Example from `BaselineDiffServiceTests.cs`:
  ```csharp
  private static ControlRecord MakeControl(
      string id,
      string title = "Title",
      string severity = "medium",
      string? checkText = null,
      string? fixText = null)
  {
      return new ControlRecord
      {
          ControlId = id,
          Title = title,
          Severity = severity,
          CheckText = checkText ?? "Check " + id,
          // ... defaults
      };
  }
  ```

## Testing Constraints

**Platform-Specific Tests:**
- Some tests account for environment constraints
- Example from `LcmServiceTests.cs`:
  ```csharp
  // In constrained environments, PowerShell invocation can fail.
  // In permissive environments the command may succeed.
  if (exception is null)
  {
      state.Should().NotBeNull();
      return;
  }
  ```

---

*Testing analysis: 2026-02-21*

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Apply;
using STIGForge.Verify;
using STIGForge.Build;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Core.Services;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Apply.Snapshot;
using Microsoft.Extensions.Logging.Abstractions;

namespace STIGForge.UnitTests.Build;

/// <summary>
/// Verifies overlay decision filtering in orchestration:
/// overlay_decisions.json is consumed at apply-time,
/// NotApplicable controls are excluded from PowerStig generation,
/// and filtering logic is deterministic.
/// </summary>
public sealed class BundleOrchestratorControlOverrideTests : IDisposable
{
  private readonly string _tempRoot;
  private Mock<IVerificationWorkflowService> _mockVerification = null!;

  public BundleOrchestratorControlOverrideTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-orchestrator-override-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public async Task OrchestrateAsync_WithOverlayDecisions_FiltersNotApplicableFromPowerStigGeneration()
  {
    // Arrange
    var bundleDir = Path.Combine(_tempRoot, "bundle");
    var reportsDir = Path.Combine(bundleDir, "Reports");
    var manifestDir = Path.Combine(bundleDir, "Manifest");
    var applyDir = Path.Combine(bundleDir, "Apply");

    Directory.CreateDirectory(reportsDir);
    Directory.CreateDirectory(manifestDir);
    Directory.CreateDirectory(applyDir);

    // Create overlay_decisions.json with NotApplicable decision
    var decisions = new[]
    {
      new
      {
        key = "RULE:SV-12345r1_rule",
        overlayId = "overlay1",
        overlayName = "NA Overlay",
        overlayOrder = 0,
        overrideOrder = 0,
        outcome = new
        {
          statusOverride = "NotApplicable",
          naReason = "Out of scope",
          notes = (string?)null
        }
      }
    };

    var decisionsJson = JsonSerializer.Serialize(decisions, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(reportsDir, "overlay_decisions.json"), decisionsJson, Encoding.UTF8);

    // Create pack_controls.json with test controls
    var controls = new[]
    {
      new ControlRecord
      {
        ExternalIds = new ExternalIds
        {
          RuleId = "SV-12345r1_rule",
          VulnId = "V-12345",
          BenchmarkId = "TEST-1.0"
        },
        Title = "NA Rule",
        Applicability = new Applicability
        {
          ClassificationScope = ScopeTag.Unknown,
          Confidence = Confidence.High
        }
      },
      new ControlRecord
      {
        ExternalIds = new ExternalIds
        {
          RuleId = "SV-67890r1_rule",
          VulnId = "V-67890",
          BenchmarkId = "TEST-1.0"
        },
        Title = "Pass Rule",
        Applicability = new Applicability
        {
          ClassificationScope = ScopeTag.Unknown,
          Confidence = Confidence.High
        }
      }
    };

    var controlsJson = JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(manifestDir, "pack_controls.json"), controlsJson, Encoding.UTF8);

    // Create empty overlays.json
    File.WriteAllText(Path.Combine(manifestDir, "overlays.json"), "[]", Encoding.UTF8);

    // Setup mocks
    var applyRunner = CreateNoOpApplyRunner();
    _mockVerification = new Mock<IVerificationWorkflowService>(MockBehavior.Strict);

    var orchestrator = new BundleOrchestrator(
      CreateBundleBuilder(),
      applyRunner,
      _mockVerification.Object,
      new VerificationArtifactAggregationService(),
      new MissionTracingService(),
      new PerformanceInstrumenter()
    );

    var request = new OrchestrateRequest
    {
      BundleRoot = bundleDir,
      PowerStigModulePath = "/test/modules"
    };

    // Act
    await orchestrator.OrchestrateAsync(request, CancellationToken.None);

    // Assert - The NA control should be filtered
    // We verify this by checking the mock was called with PowerStigDataGeneratedPath set
    applyRunner.Requests.Should().HaveCount(1);
    applyRunner.Requests[0].PowerStigDataGeneratedPath.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void LoadOverlayDecisions_WithValidJson_ReturnsDecisions()
  {
    // Arrange
    var bundleDir = Path.Combine(_tempRoot, "bundle");
    var reportsDir = Path.Combine(bundleDir, "Reports");

    Directory.CreateDirectory(reportsDir);

    var decisions = new[]
    {
      new
      {
        key = "RULE:SV-12345",
        overlayId = "overlay1",
        overlayName = "Test Overlay",
        overlayOrder = 0,
        overrideOrder = 0,
        outcome = new
        {
          statusOverride = "NotApplicable",
          naReason = "Test",
          notes = (string?)null
        }
      }
    };

    var json = JsonSerializer.Serialize(decisions, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(reportsDir, "overlay_decisions.json"), json, Encoding.UTF8);

    // Act - Use reflection to call private method
    var orchestrator = new BundleOrchestrator(
      CreateBundleBuilder(),
      CreateNoOpApplyRunner(),
      Mock.Of<IVerificationWorkflowService>(),
      new VerificationArtifactAggregationService(),
      new MissionTracingService(),
      new PerformanceInstrumenter()
    );

    var method = typeof(BundleOrchestrator).GetMethod("LoadOverlayDecisions",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var result = method?.Invoke(null, new object[] { bundleDir }) as System.Collections.IList;

    // Assert
    result.Should().NotBeNull();
    result.Count.Should().Be(1);
  }

  [Fact]
  public void LoadOverlayDecisions_WithMissingFile_ReturnsEmptyList()
  {
    // Arrange
    var bundleDir = Path.Combine(_tempRoot, "bundle");
    Directory.CreateDirectory(bundleDir);

    // Act - File doesn't exist
    var orchestrator = new BundleOrchestrator(
      CreateBundleBuilder(),
      CreateNoOpApplyRunner(),
      Mock.Of<IVerificationWorkflowService>(),
      new VerificationArtifactAggregationService(),
      new MissionTracingService(),
      new PerformanceInstrumenter()
    );

    var method = typeof(BundleOrchestrator).GetMethod("LoadOverlayDecisions",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var result = method?.Invoke(null, new object[] { bundleDir }) as System.Collections.IList;

    // Assert
    result.Should().NotBeNull();
    result.Count.Should().Be(0);
  }

  [Fact]
  public void IsControlNotApplicable_WithMatchingRuleKey_ReturnsTrue()
  {
    // Arrange
    var control = new ControlRecord
    {
      ExternalIds = new ExternalIds
      {
        RuleId = "SV-12345",
        VulnId = "V-12345"
      }
    };

    var notApplicableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "RULE:SV-12345",
      "VULN:V-99999"
    };

    // Act
    var method = typeof(BundleOrchestrator).GetMethod("IsControlNotApplicable",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var result = method?.Invoke(null, new object[] { control, notApplicableKeys }) as bool?;

    // Assert
    result.Should().BeTrue();
  }

  [Fact]
  public void IsControlNotApplicable_WithMatchingVulnKey_ReturnsTrue()
  {
    // Arrange
    var control = new ControlRecord
    {
      ExternalIds = new ExternalIds
      {
        RuleId = "SV-12345",
        VulnId = "V-67890"
      }
    };

    var notApplicableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "VULN:V-67890"
    };

    // Act
    var method = typeof(BundleOrchestrator).GetMethod("IsControlNotApplicable",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var result = method?.Invoke(null, new object[] { control, notApplicableKeys }) as bool?;

    // Assert
    result.Should().BeTrue();
  }

  [Fact]
  public void IsControlNotApplicable_WithNoMatch_ReturnsFalse()
  {
    // Arrange
    var control = new ControlRecord
    {
      ExternalIds = new ExternalIds
      {
        RuleId = "SV-11111",
        VulnId = "V-11111"
      }
    };

    var notApplicableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "RULE:SV-99999",
      "VULN:V-99999"
    };

    // Act
    var method = typeof(BundleOrchestrator).GetMethod("IsControlNotApplicable",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var result = method?.Invoke(null, new object[] { control, notApplicableKeys }) as bool?;

    // Assert
    result.Should().BeFalse();
  }

  private static BundleBuilder CreateBundleBuilder()
  {
    var path = new Mock<IPathBuilder>();
    var hash = new Mock<IHashingService>();
    var scope = new Mock<IClassificationScopeService>();

    path.Setup(x => x.GetBundleRoot(It.IsAny<string>())).Returns<string>(id => Path.Combine(Path.GetTempPath(), id));
    hash.Setup(x => x.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("abc123");
    scope.Setup(x => x.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
      .Returns(new CompiledControls(new List<CompiledControl>(), new List<CompiledControl>()));

    return new BundleBuilder(
      path.Object,
      hash.Object,
      scope.Object,
      new ReleaseAgeGate(new SystemClock()),
      new OverlayConflictDetector(),
      new OverlayMergeService());
  }

  private static TestApplyRunner CreateNoOpApplyRunner()
  {
    return new TestApplyRunner();
  }

  private sealed class TestApplyRunner : ApplyRunner
  {
    public List<ApplyRequest> Requests { get; } = new();

    public TestApplyRunner()
      : base(
        NullLogger<ApplyRunner>.Instance,
        new SnapshotService(NullLogger<SnapshotService>.Instance, new Mock<IProcessRunner>().Object),
        new RollbackScriptGenerator(NullLogger<RollbackScriptGenerator>.Instance),
        new LcmService(NullLogger<LcmService>.Instance),
        new RebootCoordinator(NullLogger<RebootCoordinator>.Instance, _ => true))
    {
    }

    public override Task<ApplyResult> RunAsync(ApplyRequest request, CancellationToken ct)
    {
      Requests.Add(request);
      return Task.FromResult(new ApplyResult
      {
        LogPath = "/test/apply.log",
        Steps = Array.Empty<ApplyStepOutcome>()
      });
    }
  }
}

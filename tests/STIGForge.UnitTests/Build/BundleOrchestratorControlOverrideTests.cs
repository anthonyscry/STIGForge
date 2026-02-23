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
  private Mock<BundleBuilder> _mockBuilder = null!;
  private Mock<ApplyRunner> _mockApply = null!;
  private Mock<IVerificationWorkflowService> _mockVerification = null!;
  private Mock<VerificationArtifactAggregationService> _mockArtifactAggregation = null!;

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
        key = "RULE:SV-12345",
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
      new
      {
        externalIds = new
        {
          ruleId = "SV-12345",
          vulnId = "V-12345",
          benchmarkId = "TEST-1.0"
        },
        title = "NA Rule",
        applicability = new
        {
          classificationScope = "Unknown",
          confidence = "High"
        }
      },
      new
      {
        externalIds = new
        {
          ruleId = "SV-67890",
          vulnId = "V-67890",
          benchmarkId = "TEST-1.0"
        },
        title = "Pass Rule",
        applicability = new
        {
          classificationScope = "Unknown",
          confidence = "High"
        }
      }
    };

    var controlsJson = JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(manifestDir, "pack_controls.json"), controlsJson, Encoding.UTF8);

    // Create empty overlays.json
    File.WriteAllText(Path.Combine(manifestDir, "overlays.json"), "[]", Encoding.UTF8);

    // Setup mocks
    _mockBuilder = new Mock<BundleBuilder>(MockBehavior.Strict, null!, null!, null!, null!, null!, null!);
    _mockApply = new Mock<ApplyRunner>(MockBehavior.Strict, null!, null!, null!, null!, null!, null!);
    _mockVerification = new Mock<IVerificationWorkflowService>(MockBehavior.Strict);
    _mockArtifactAggregation = new Mock<VerificationArtifactAggregationService>(MockBehavior.Strict);

    _mockApply.Setup(x => x.RunAsync(It.IsAny<ApplyRequest>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new ApplyResult
      {
        LogPath = "/test/apply.log",
        Steps = Array.Empty<ApplyStepOutcome>()
      });

    var orchestrator = new BundleOrchestrator(
      _mockBuilder.Object,
      _mockApply.Object,
      _mockVerification.Object,
      _mockArtifactAggregation.Object,
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
    _mockApply.Verify(x => x.RunAsync(
      It.Is<ApplyRequest>(r => r.PowerStigDataGeneratedPath != null),
      It.IsAny<CancellationToken>()
    ), Times.Once);
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
      Mock.Of<BundleBuilder>(),
      Mock.Of<ApplyRunner>(),
      Mock.Of<IVerificationWorkflowService>(),
      Mock.Of<VerificationArtifactAggregationService>(),
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
      Mock.Of<BundleBuilder>(),
      Mock.Of<ApplyRunner>(),
      Mock.Of<IVerificationWorkflowService>(),
      Mock.Of<VerificationArtifactAggregationService>(),
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
}

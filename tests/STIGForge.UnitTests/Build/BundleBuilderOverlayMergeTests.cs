using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Build;

namespace STIGForge.UnitTests.Build;

/// <summary>
/// Verifies overlay merge integration in bundle build:
/// overlay_conflicts.csv and overlay_decisions.json emission,
/// review queue excludes NotApplicable from overrides,
/// and deterministic ordering is maintained.
/// </summary>
public sealed class BundleBuilderOverlayMergeTests : IDisposable
{
  private readonly string _tempRoot;
  private Mock<IPathBuilder> _mockPath = null!;
  private Mock<IHashingService> _mockHash = null!;
  private Mock<IClassificationScopeService> _mockScope = null!;

  public BundleBuilderOverlayMergeTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-overlay-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    BuildTime.Reset();
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public async Task BuildAsync_WithOverlays_EmitsOverlayConflictsCsv()
  {
    // Arrange
    var request = CreateBasicRequest();
    request.OutputRoot = Path.Combine(_tempRoot, "bundle");
    request.Overlays = new List<Overlay>
    {
      new Overlay
      {
        OverlayId = "overlay1",
        Name = "Test Overlay 1",
        Overrides = new List<ControlOverride>
        {
          new ControlOverride { RuleId = "SV-12345", VulnId = "V-12345", StatusOverride = ControlStatus.NotApplicable, NaReason = "Test reason" }
        }
      }
    };

    var builder = CreateBuilder();

    // Setup scope compile
    var compiled = new CompiledControls(
      new List<CompiledControl>
      {
        CreateCompiledControl("SV-12345", "V-12345", "Test Rule 1", ControlStatus.Open)
      },
      new List<CompiledControl>()
    );
    _mockScope.Setup(x => x.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
      .Returns(compiled);

    // Act
    var result = await builder.BuildAsync(request, CancellationToken.None);

    // Assert
    var conflictsPath = Path.Combine(result.BundleRoot, "Reports", "overlay_conflicts.csv");
    File.Exists(conflictsPath).Should().BeTrue("overlay_conflicts.csv should exist");

    var content = File.ReadAllText(conflictsPath);
    content.Should().Contain("Key", "CSV should have header");
    content.Should().Contain("PreviousOverlayId", "CSV should have PreviousOverlayId column");
    content.Should().Contain("CurrentOverlayId", "CSV should have CurrentOverlayId column");
  }

  [Fact]
  public async Task BuildAsync_WithOverlays_EmitsOverlayDecisionsJson()
  {
    // Arrange
    var request = CreateBasicRequest();
    request.OutputRoot = Path.Combine(_tempRoot, "bundle");
    request.Overlays = new List<Overlay>
    {
      new Overlay
      {
        OverlayId = "overlay1",
        Name = "Test Overlay 1",
        Overrides = new List<ControlOverride>
        {
          new ControlOverride { RuleId = "SV-12345", VulnId = "V-12345", StatusOverride = ControlStatus.NotApplicable, NaReason = "Test NA" }
        }
      }
    };

    var builder = CreateBuilder();

    var compiled = new CompiledControls(
      new List<CompiledControl>
      {
        CreateCompiledControl("SV-12345", "V-12345", "Test Rule 1", ControlStatus.Open)
      },
      new List<CompiledControl>()
    );
    _mockScope.Setup(x => x.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
      .Returns(compiled);

    // Act
    var result = await builder.BuildAsync(request, CancellationToken.None);

    // Assert
    var decisionsPath = Path.Combine(result.BundleRoot, "Reports", "overlay_decisions.json");
    File.Exists(decisionsPath).Should().BeTrue("overlay_decisions.json should exist");

    var json = File.ReadAllText(decisionsPath);
    var decisions = JsonSerializer.Deserialize<JsonElement[]>(json);
    decisions.Should().NotBeNull();
    decisions.Should().HaveCountGreaterThan(0, "Should have at least one decision");

    var first = decisions[0];
    first.TryGetProperty("key", out var keyProp).Should().BeTrue("Should have key property");
    first.TryGetProperty("overlayId", out var overlayIdProp).Should().BeTrue("Should have overlayId property");
    first.TryGetProperty("outcome", out var outcomeProp).Should().BeTrue("Should have outcome property");
  }

  [Fact]
  public async Task BuildAsync_WithNotApplicableOverlay_ExcludesFromReviewQueue()
  {
    // Arrange
    var request = CreateBasicRequest();
    request.OutputRoot = Path.Combine(_tempRoot, "bundle");
    request.Overlays = new List<Overlay>
    {
      new Overlay
      {
        OverlayId = "overlay1",
        Name = "NA Overlay",
        Overrides = new List<ControlOverride>
        {
          new ControlOverride { RuleId = "SV-12345", VulnId = "V-12345", StatusOverride = ControlStatus.NotApplicable, NaReason = "Not in scope" }
        }
      }
    };

    var builder = CreateBuilder();

    var compiled = new CompiledControls(
      new List<CompiledControl>
      {
        CreateCompiledControl("SV-12345", "V-12345", "Rule to be NA", ControlStatus.Open),
        CreateCompiledControl("SV-67890", "V-67890", "Another rule", ControlStatus.Open)
      },
      new List<CompiledControl>()
    );
    _mockScope.Setup(x => x.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
      .Returns(compiled);

    // Act
    var result = await builder.BuildAsync(request, CancellationToken.None);

    // Assert
    var reviewQueuePath = Path.Combine(result.BundleRoot, "Reports", "review_required.csv");
    File.Exists(reviewQueuePath).Should().BeTrue("review_required.csv should exist");

    var content = File.ReadAllText(reviewQueuePath);
    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    // Header + 1 line (only SV-67890, SV-12345 should be excluded due to NA override)
    lines.Should().HaveCount(2, "Should have header and one rule in review queue");
    content.Should().NotContain("SV-12345", "NA override rule should not be in review queue");
    content.Should().Contain("SV-67890", "Non-NA rule should be in review queue");
  }

  [Fact]
  public void OverlayMergeService_DeterministicOrdering_ProducesConsistentResults()
  {
    // Arrange
    var mergeService = new OverlayMergeService();
    var controls = new List<CompiledControl>
    {
      CreateCompiledControl("SV-33333", "V-33333", "Rule C", ControlStatus.Open),
      CreateCompiledControl("SV-11111", "V-11111", "Rule A", ControlStatus.Open),
      CreateCompiledControl("SV-22222", "V-22222", "Rule B", ControlStatus.Open)
    };

    var overlays = new List<Overlay>
    {
      new Overlay
      {
        OverlayId = "overlay2",
        Name = "Second",
        Overrides = new List<ControlOverride>
        {
          new ControlOverride { RuleId = "SV-11111", StatusOverride = ControlStatus.NotApplicable },
          new ControlOverride { RuleId = "SV-22222", StatusOverride = ControlStatus.NotApplicable }
        }
      },
      new Overlay
      {
        OverlayId = "overlay1",
        Name = "First",
        Overrides = new List<ControlOverride>
        {
          new ControlOverride { RuleId = "SV-11111", StatusOverride = ControlStatus.Pass }
        }
      }
    };

    // Act
    var result1 = mergeService.Merge(controls, overlays);
    var result2 = mergeService.Merge(controls, overlays);

    // Assert
    result1.AppliedDecisions.Count.Should().Be(result2.AppliedDecisions.Count);
    for (int i = 0; i < result1.AppliedDecisions.Count; i++)
    {
      result1.AppliedDecisions[i].Key.Should().Be(result2.AppliedDecisions[i].Key);
      result1.AppliedDecisions[i].OverlayId.Should().Be(result2.AppliedDecisions[i].OverlayId);
    }
  }

  private BundleBuilder CreateBuilder()
  {
    _mockPath = new Mock<IPathBuilder>();
    _mockHash = new Mock<IHashingService>();
    _mockScope = new Mock<IClassificationScopeService>();
    _mockPath.Setup(x => x.GetBundleRoot(It.IsAny<string>())).Returns<string>(id => $"/test/bundles/{id}");
    _mockHash.Setup(x => x.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("abc123");
    var releaseGate = new ReleaseAgeGate(new SystemClock());
    var conflictDetector = new OverlayConflictDetector();

    return new BundleBuilder(
      _mockPath.Object,
      _mockHash.Object,
      _mockScope.Object,
      releaseGate,
      conflictDetector,
      new OverlayMergeService()
    );
  }

  private static BundleBuildRequest CreateBasicRequest()
  {
    return new BundleBuildRequest
    {
      BundleId = "test-bundle",
      ForceAutoApply = true,
      Pack = new ContentPack
      {
        PackId = "pack-1",
        Name = "Test Pack",
        ReleaseDate = DateTime.UtcNow,
        ImportedAt = DateTime.UtcNow
      },
      Profile = new Profile
      {
        ProfileId = "profile-1",
        Name = "Test Profile",
        OsTarget = OsTarget.Server2019,
        RoleTemplate = RoleTemplate.MemberServer,
        AutomationPolicy = new AutomationPolicy { NewRuleGraceDays = 30 }
      },
      Controls = new List<ControlRecord>
      {
        CreateControlRecord("SV-12345", "V-12345", "Test Rule 1")
      },
      Overlays = new List<Overlay>()
    };
  }

  private static CompiledControl CreateCompiledControl(string ruleId, string vulnId, string title, ControlStatus status)
  {
    return new CompiledControl(
      new ControlRecord
      {
        ExternalIds = new ExternalIds
        {
          RuleId = ruleId,
          VulnId = vulnId,
          BenchmarkId = "TEST-1.0"
        },
        Title = title,
        Applicability = new Applicability
        {
          ClassificationScope = ScopeTag.Unknown,
          Confidence = Confidence.High
        }
      },
      status,
      string.Empty,
      false,
      string.Empty
    );
  }

  private static ControlRecord CreateControlRecord(string ruleId, string vulnId, string title)
  {
    return new ControlRecord
    {
      ExternalIds = new ExternalIds
      {
        RuleId = ruleId,
        VulnId = vulnId,
        BenchmarkId = "TEST-1.0"
      },
      Title = title,
      Applicability = new Applicability
      {
        ClassificationScope = ScopeTag.Unknown,
        Confidence = Confidence.High
      }
    };
  }
}

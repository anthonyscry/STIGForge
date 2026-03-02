using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class ComplianceTrendServiceTests
{
  [Fact]
  public async Task RecordSnapshot_PersistsToRepository()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    var now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
    var service = new ComplianceTrendService(repo.Object, new TestClock(now));

    await service.RecordSnapshotAsync(
      passCount: 10,
      failCount: 2,
      errorCount: 1,
      notApplicableCount: 5,
      notReviewedCount: 3,
      bundleRoot: @"C:\bundle",
      runId: "run-1",
      packId: "pack-1",
      tool: "SCAP",
      ct: CancellationToken.None);

    repo.Verify(r => r.SaveSnapshotAsync(
      It.Is<ComplianceSnapshot>(s =>
        !string.IsNullOrWhiteSpace(s.SnapshotId)
        && s.BundleRoot == @"C:\bundle"
        && s.RunId == "run-1"
        && s.PackId == "pack-1"
        && s.CapturedAt == now
        && s.PassCount == 10
        && s.FailCount == 2
        && s.ErrorCount == 1
        && s.NotApplicableCount == 5
        && s.NotReviewedCount == 3
        && s.TotalCount == 21
        && s.Tool == "SCAP"),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task RecordSnapshot_CalculatesCompliancePercent()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    var service = new ComplianceTrendService(repo.Object, new TestClock(DateTimeOffset.UtcNow));

    ComplianceSnapshot? captured = null;
    repo.Setup(r => r.SaveSnapshotAsync(It.IsAny<ComplianceSnapshot>(), It.IsAny<CancellationToken>()))
      .Callback<ComplianceSnapshot, CancellationToken>((snapshot, _) => captured = snapshot)
      .Returns(Task.CompletedTask);

    await service.RecordSnapshotAsync(80, 10, 10, 0, 0, "bundle", "run", "pack", "tool", CancellationToken.None);

    captured.Should().NotBeNull();
    captured!.CompliancePercent.Should().Be(80.0);
  }

  [Fact]
  public async Task RecordSnapshot_ZeroEvaluated_ZeroPercent()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    var service = new ComplianceTrendService(repo.Object, new TestClock(DateTimeOffset.UtcNow));

    ComplianceSnapshot? captured = null;
    repo.Setup(r => r.SaveSnapshotAsync(It.IsAny<ComplianceSnapshot>(), It.IsAny<CancellationToken>()))
      .Callback<ComplianceSnapshot, CancellationToken>((snapshot, _) => captured = snapshot)
      .Returns(Task.CompletedTask);

    await service.RecordSnapshotAsync(0, 0, 0, 10, 0, "bundle", null, null, "tool", CancellationToken.None);

    captured.Should().NotBeNull();
    captured!.CompliancePercent.Should().Be(0.0);
  }

  [Fact]
  public async Task GetTrend_ReturnsOrderedSnapshots()
  {
    var snapshots = new List<ComplianceSnapshot>
    {
      new() { SnapshotId = "3", BundleRoot = "bundle", CompliancePercent = 90 },
      new() { SnapshotId = "2", BundleRoot = "bundle", CompliancePercent = 85 },
      new() { SnapshotId = "1", BundleRoot = "bundle", CompliancePercent = 80 }
    };

    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 3, It.IsAny<CancellationToken>()))
      .ReturnsAsync(snapshots);

    var service = new ComplianceTrendService(repo.Object);
    var trend = await service.GetTrendAsync("bundle", 3, CancellationToken.None);

    trend.Snapshots.Select(s => s.SnapshotId).Should().ContainInOrder("3", "2", "1");
  }

  [Fact]
  public async Task GetTrend_CalculatesDelta()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 2, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<ComplianceSnapshot>
      {
        new() { SnapshotId = "current", BundleRoot = "bundle", CompliancePercent = 85.0 },
        new() { SnapshotId = "prev", BundleRoot = "bundle", CompliancePercent = 80.0 }
      });

    var service = new ComplianceTrendService(repo.Object);
    var trend = await service.GetTrendAsync("bundle", 2, CancellationToken.None);

    trend.CurrentPercent.Should().Be(85.0);
    trend.PreviousPercent.Should().Be(80.0);
    trend.Delta.Should().Be(5.0);
    trend.IsRegression.Should().BeFalse();
  }

  [Fact]
  public async Task GetTrend_EmptyHistory_ReturnsZeros()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 10, It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ComplianceSnapshot>());

    var service = new ComplianceTrendService(repo.Object);
    var trend = await service.GetTrendAsync("bundle", 10, CancellationToken.None);

    trend.Snapshots.Should().BeEmpty();
    trend.CurrentPercent.Should().Be(0);
    trend.PreviousPercent.Should().Be(0);
    trend.Delta.Should().Be(0);
    trend.IsRegression.Should().BeFalse();
  }

  [Fact]
  public async Task DetectRegression_WhenDropExceedsThreshold_ReturnsTrue()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 2, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<ComplianceSnapshot>
      {
        new() { SnapshotId = "current", BundleRoot = "bundle", CompliancePercent = 70.0 },
        new() { SnapshotId = "previous", BundleRoot = "bundle", CompliancePercent = 80.0 }
      });

    var service = new ComplianceTrendService(repo.Object);
    var isRegression = await service.DetectRegressionAsync("bundle", 5.0, CancellationToken.None);

    isRegression.Should().BeTrue();
  }

  [Fact]
  public async Task DetectRegression_WhenStableOrImproved_ReturnsFalse()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 2, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<ComplianceSnapshot>
      {
        new() { SnapshotId = "current", BundleRoot = "bundle", CompliancePercent = 85.0 },
        new() { SnapshotId = "previous", BundleRoot = "bundle", CompliancePercent = 80.0 }
      });

    var service = new ComplianceTrendService(repo.Object);
    var isRegression = await service.DetectRegressionAsync("bundle", 5.0, CancellationToken.None);

    isRegression.Should().BeFalse();
  }

  [Fact]
  public async Task DetectRegression_NoSnapshots_ReturnsFalse()
  {
    var repo = new Mock<IComplianceTrendRepository>();
    repo.Setup(r => r.GetSnapshotsAsync("bundle", 2, It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ComplianceSnapshot>());

    var service = new ComplianceTrendService(repo.Object);
    var isRegression = await service.DetectRegressionAsync("bundle", 1.0, CancellationToken.None);

    isRegression.Should().BeFalse();
  }

  private sealed class TestClock : IClock
  {
    public TestClock(DateTimeOffset now)
    {
      Now = now;
    }

    public DateTimeOffset Now { get; }
  }
}

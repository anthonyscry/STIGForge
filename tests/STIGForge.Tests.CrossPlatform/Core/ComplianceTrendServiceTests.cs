using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public class ComplianceTrendServiceTests
{
    private readonly Mock<IComplianceTrendRepository> _mockRepo;
    private readonly Mock<IClock> _mockClock;
    private readonly ComplianceTrendService _sut;
    private static readonly DateTimeOffset _fixedNow = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public ComplianceTrendServiceTests()
    {
        _mockRepo = new Mock<IComplianceTrendRepository>();
        _mockClock = new Mock<IClock>();
        _mockClock.SetupGet(c => c.Now).Returns(_fixedNow);
        _sut = new ComplianceTrendService(_mockRepo.Object, _mockClock.Object);
    }

    private static ComplianceSnapshot Snap(double percent, DateTimeOffset? at = null) => new()
    {
        SnapshotId = Guid.NewGuid().ToString("N"),
        BundleRoot = "bundle",
        CompliancePercent = percent,
        CapturedAt = at ?? _fixedNow,
        Tool = "test"
    };

    private void SetupSnapshots(string bundleRoot, params ComplianceSnapshot[] snapshots)
    {
        _mockRepo
            .Setup(r => r.GetSnapshotsAsync(bundleRoot, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots.ToList());
    }

    // ── DetectRegressionAsync ──────────────────────────────────────────────

    [Fact]
    public async Task DetectRegression_DropExceedsThreshold_ReturnsTrue()
    {
        SetupSnapshots("bundle", Snap(90), Snap(96)); // delta = -6
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DetectRegression_DropBelowThreshold_ReturnsFalse()
    {
        SetupSnapshots("bundle", Snap(92), Snap(95)); // delta = -3
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DetectRegression_Improvement_ReturnsFalse()
    {
        SetupSnapshots("bundle", Snap(95), Snap(90)); // delta = +5
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DetectRegression_WithMinimumFloor_AboveFloor_DeltaExceedsThreshold_ReturnsTrue()
    {
        // current=85, previous=91, delta=-6; floor=80 → 85 >= 80 so floor check passes;
        // delta=-6 < -threshold=-5 → true (floor suppresses immediate-regression check but NOT the delta check)
        SetupSnapshots("bundle", Snap(85), Snap(91));
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None, minimumFloor: 80.0);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DetectRegression_WithMinimumFloor_BelowFloor_UsesThreshold()
    {
        // current=75, previous=81, delta=-6; floor=80 → 75 < 80 → uses threshold → -6 < -5 → true
        SetupSnapshots("bundle", Snap(75), Snap(81));
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None, minimumFloor: 80.0);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DetectRegression_FloorZero_BehavesAsNoFloor()
    {
        // floor=0 → minimumFloor > 0.0 is false → uses threshold; delta=-6 > threshold=5 → true
        SetupSnapshots("bundle", Snap(85), Snap(91));
        var result = await _sut.DetectRegressionAsync("bundle", 5, CancellationToken.None, minimumFloor: 0.0);
        result.Should().BeTrue();
    }

    // ── RecordSnapshotAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RecordSnapshot_ZeroEvaluated_SetsCompliancePercentZero()
    {
        _mockRepo
            .Setup(r => r.SaveSnapshotAsync(It.IsAny<ComplianceSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.RecordSnapshotAsync(0, 0, 0, 5, 3, "bundle", null, null, "tool", CancellationToken.None);

        _mockRepo.Verify(r => r.SaveSnapshotAsync(
            It.Is<ComplianceSnapshot>(s => s.CompliancePercent == 0.0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordSnapshot_RoundsToTwoDecimalPlaces()
    {
        _mockRepo
            .Setup(r => r.SaveSnapshotAsync(It.IsAny<ComplianceSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // pass=1, fail=2, error=0 → evaluated=3 → 1/3*100 = 33.333... → rounds to 33.33
        await _sut.RecordSnapshotAsync(1, 2, 0, 0, 0, "bundle", null, null, "tool", CancellationToken.None);

        _mockRepo.Verify(r => r.SaveSnapshotAsync(
            It.Is<ComplianceSnapshot>(s => s.CompliancePercent == 33.33),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetTrendAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetTrend_LimitLessThanOne_NormalizesToTen()
    {
        int capturedLimit = -1;
        _mockRepo
            .Setup(r => r.GetSnapshotsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, CancellationToken>((_, l, _) => capturedLimit = l)
            .ReturnsAsync(new List<ComplianceSnapshot>());

        await _sut.GetTrendAsync("bundle", 0, CancellationToken.None);

        capturedLimit.Should().Be(10);
    }

    [Fact]
    public async Task GetTrend_SingleSnapshot_DeltaIsZero()
    {
        SetupSnapshots("bundle", Snap(80));

        var trend = await _sut.GetTrendAsync("bundle", 1, CancellationToken.None);

        trend.Delta.Should().Be(0);
        trend.CurrentPercent.Should().Be(80);
    }
}

using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ComplianceTrendRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly SqliteConnection _keepAlive;
  private readonly IComplianceTrendRepository _repo;

  public ComplianceTrendRepositoryTests()
  {
    var csb = new SqliteConnectionStringBuilder
    {
      DataSource = $"compliance-trend-{Guid.NewGuid():N}",
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared
    };

    _cs = csb.ToString();
    _keepAlive = new SqliteConnection(_cs);
    _keepAlive.Open();

    DbBootstrap.EnsureCreated(_cs);
    _repo = new SqliteComplianceTrendRepository(new DbConnectionString(_cs));
  }

  public void Dispose()
  {
    _keepAlive.Dispose();
  }

  [Fact]
  public async Task SaveAndGetLatest_RoundTrips()
  {
    var expected = BuildSnapshot(
      snapshotId: "snap-1",
      bundleRoot: "bundle-a",
      capturedAt: new DateTimeOffset(2026, 2, 1, 12, 30, 0, TimeSpan.Zero),
      passCount: 80,
      failCount: 10,
      errorCount: 5,
      notApplicableCount: 3,
      notReviewedCount: 2,
      totalCount: 100,
      compliancePercent: 84.21,
      runId: "run-1",
      packId: "pack-1",
      tool: "DISA SCAP");

    await _repo.SaveSnapshotAsync(expected, CancellationToken.None);
    var actual = await _repo.GetLatestSnapshotAsync("bundle-a", CancellationToken.None);

    actual.Should().NotBeNull();
    actual!.SnapshotId.Should().Be(expected.SnapshotId);
    actual.BundleRoot.Should().Be(expected.BundleRoot);
    actual.RunId.Should().Be(expected.RunId);
    actual.PackId.Should().Be(expected.PackId);
    actual.CapturedAt.Should().Be(expected.CapturedAt);
    actual.PassCount.Should().Be(expected.PassCount);
    actual.FailCount.Should().Be(expected.FailCount);
    actual.ErrorCount.Should().Be(expected.ErrorCount);
    actual.NotApplicableCount.Should().Be(expected.NotApplicableCount);
    actual.NotReviewedCount.Should().Be(expected.NotReviewedCount);
    actual.TotalCount.Should().Be(expected.TotalCount);
    actual.CompliancePercent.Should().Be(expected.CompliancePercent);
    actual.Tool.Should().Be(expected.Tool);
  }

  [Fact]
  public async Task GetSnapshots_ReturnsDescendingOrder()
  {
    await _repo.SaveSnapshotAsync(BuildSnapshot("s1", "bundle-a", new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), 50, 25, 25, 0, 0, 100, 50.0), CancellationToken.None);
    await _repo.SaveSnapshotAsync(BuildSnapshot("s2", "bundle-a", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 60, 20, 20, 0, 0, 100, 60.0), CancellationToken.None);
    await _repo.SaveSnapshotAsync(BuildSnapshot("s3", "bundle-a", new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), 55, 25, 20, 0, 0, 100, 55.0), CancellationToken.None);

    var snapshots = await _repo.GetSnapshotsAsync("bundle-a", 10, CancellationToken.None);

    snapshots.Select(s => s.SnapshotId).Should().ContainInOrder("s2", "s3", "s1");
  }

  [Fact]
  public async Task GetSnapshots_FiltersByBundleRoot()
  {
    await _repo.SaveSnapshotAsync(BuildSnapshot("a1", "bundle-a", new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), 80, 10, 10, 0, 0, 100, 80.0), CancellationToken.None);
    await _repo.SaveSnapshotAsync(BuildSnapshot("b1", "bundle-b", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 70, 20, 10, 0, 0, 100, 70.0), CancellationToken.None);

    var bundleA = await _repo.GetSnapshotsAsync("bundle-a", 10, CancellationToken.None);
    var bundleB = await _repo.GetSnapshotsAsync("bundle-b", 10, CancellationToken.None);

    bundleA.Should().HaveCount(1);
    bundleA[0].SnapshotId.Should().Be("a1");
    bundleB.Should().HaveCount(1);
    bundleB[0].SnapshotId.Should().Be("b1");
  }

  [Fact]
  public async Task GetLatest_NoData_ReturnsNull()
  {
    var latest = await _repo.GetLatestSnapshotAsync("bundle-none", CancellationToken.None);

    latest.Should().BeNull();
  }

  private static ComplianceSnapshot BuildSnapshot(
    string snapshotId,
    string bundleRoot,
    DateTimeOffset capturedAt,
    int passCount,
    int failCount,
    int errorCount,
    int notApplicableCount,
    int notReviewedCount,
    int totalCount,
    double compliancePercent,
    string? runId = null,
    string? packId = null,
    string tool = "Tool")
  {
    return new ComplianceSnapshot
    {
      SnapshotId = snapshotId,
      BundleRoot = bundleRoot,
      RunId = runId,
      PackId = packId,
      CapturedAt = capturedAt,
      PassCount = passCount,
      FailCount = failCount,
      ErrorCount = errorCount,
      NotApplicableCount = notApplicableCount,
      NotReviewedCount = notReviewedCount,
      TotalCount = totalCount,
      CompliancePercent = compliancePercent,
      Tool = tool
    };
  }
}

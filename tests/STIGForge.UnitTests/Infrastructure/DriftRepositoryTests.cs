using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class DriftRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly SqliteConnection _keepAlive;
  private readonly IDriftRepository _repo;

  public DriftRepositoryTests()
  {
    var csb = new SqliteConnectionStringBuilder
    {
      DataSource = "drift-repo-" + Guid.NewGuid().ToString("N"),
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared
    };

    _cs = csb.ToString();
    _keepAlive = new SqliteConnection(_cs);
    _keepAlive.Open();

    DbBootstrap.EnsureCreated(_cs);
    _repo = new SqliteDriftRepository(_cs);
  }

  public void Dispose()
  {
    _keepAlive.Dispose();
  }

  [Fact]
  public async Task SaveAndGetLatest_RoundTrips()
  {
    var expected = new DriftSnapshot
    {
      SnapshotId = "snap-1",
      BundleRoot = "bundle-a",
      RuleId = "SV-1001",
      PreviousState = "Pass",
      CurrentState = "Fail",
      ChangeType = DriftChangeTypes.StateChanged,
      DetectedAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero)
    };

    await _repo.SaveAsync(expected, CancellationToken.None);
    var actual = await _repo.GetLatestSnapshotAsync("bundle-a", "SV-1001", CancellationToken.None);

    actual.Should().NotBeNull();
    actual!.SnapshotId.Should().Be(expected.SnapshotId);
    actual.BundleRoot.Should().Be(expected.BundleRoot);
    actual.RuleId.Should().Be(expected.RuleId);
    actual.PreviousState.Should().Be(expected.PreviousState);
    actual.CurrentState.Should().Be(expected.CurrentState);
    actual.ChangeType.Should().Be(expected.ChangeType);
    actual.DetectedAt.Should().Be(expected.DetectedAt);
  }

  [Fact]
  public async Task GetDriftHistory_FiltersByBundleAndRule()
  {
    await _repo.SaveAsync(BuildSnapshot("a1", "bundle-a", "SV-1001", new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero)), CancellationToken.None);
    await _repo.SaveAsync(BuildSnapshot("a2", "bundle-a", "SV-1002", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero)), CancellationToken.None);
    await _repo.SaveAsync(BuildSnapshot("a3", "bundle-a", "SV-1001", new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero)), CancellationToken.None);
    await _repo.SaveAsync(BuildSnapshot("b1", "bundle-b", "SV-1001", new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)), CancellationToken.None);

    var bundleHistory = await _repo.GetDriftHistoryAsync("bundle-a", null, 10, CancellationToken.None);
    var ruleHistory = await _repo.GetDriftHistoryAsync("bundle-a", "SV-1001", 10, CancellationToken.None);

    bundleHistory.Should().HaveCount(3);
    bundleHistory.Select(s => s.SnapshotId).Should().ContainInOrder("a3", "a2", "a1");

    ruleHistory.Should().HaveCount(2);
    ruleHistory.Select(s => s.SnapshotId).Should().ContainInOrder("a3", "a1");
  }

  [Fact]
  public async Task GetLatest_NoData_ReturnsNull()
  {
    var latest = await _repo.GetLatestSnapshotAsync("bundle-none", "SV-9999", CancellationToken.None);
    latest.Should().BeNull();
  }

  private static DriftSnapshot BuildSnapshot(string snapshotId, string bundleRoot, string ruleId, DateTimeOffset detectedAt) => new()
  {
    SnapshotId = snapshotId,
    BundleRoot = bundleRoot,
    RuleId = ruleId,
    PreviousState = "Pass",
    CurrentState = "Fail",
    ChangeType = DriftChangeTypes.StateChanged,
    DetectedAt = detectedAt
  };
}

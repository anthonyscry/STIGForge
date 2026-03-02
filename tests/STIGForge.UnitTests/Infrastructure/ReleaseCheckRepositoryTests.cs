using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ReleaseCheckRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly SqliteConnection _keepAlive;
  private readonly IReleaseCheckRepository _repo;

  public ReleaseCheckRepositoryTests()
  {
    var csb = new SqliteConnectionStringBuilder
    {
      DataSource = $"release-checks-{Guid.NewGuid():N}",
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared
    };

    _cs = csb.ToString();
    _keepAlive = new SqliteConnection(_cs);
    _keepAlive.Open();

    DbBootstrap.EnsureCreated(_cs);
    _repo = new SqliteReleaseCheckRepository(_cs);
  }

  public void Dispose()
  {
    _keepAlive.Dispose();
  }

  [Fact]
  public async Task SaveAndGet_RoundTrips()
  {
    var expected = BuildCheck(
      checkId: "check-1",
      checkedAt: new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
      baselinePackId: "pack-a",
      targetPackId: "pack-b",
      status: "NewReleaseFound",
      summaryJson: "{\"ok\":true}",
      releaseNotesPath: "notes/pack-b.json");

    await _repo.SaveAsync(expected, CancellationToken.None);
    var actual = await _repo.GetAsync("check-1", CancellationToken.None);

    actual.Should().NotBeNull();
    actual!.CheckId.Should().Be(expected.CheckId);
    actual.CheckedAt.Should().Be(expected.CheckedAt);
    actual.BaselinePackId.Should().Be(expected.BaselinePackId);
    actual.TargetPackId.Should().Be(expected.TargetPackId);
    actual.Status.Should().Be(expected.Status);
    actual.SummaryJson.Should().Be(expected.SummaryJson);
    actual.ReleaseNotesPath.Should().Be(expected.ReleaseNotesPath);
  }

  [Fact]
  public async Task ListByBaseline_FiltersCorrectly()
  {
    var now = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    await _repo.SaveAsync(BuildCheck("a1", now.AddMinutes(1), "pack-a", "pack-b", "NewReleaseFound", null, null), CancellationToken.None);
    await _repo.SaveAsync(BuildCheck("a2", now.AddMinutes(2), "pack-a", "pack-c", "DiffGenerated", "{}", null), CancellationToken.None);
    await _repo.SaveAsync(BuildCheck("b1", now.AddMinutes(3), "pack-b", "pack-c", "NoNewRelease", null, null), CancellationToken.None);

    var checks = await _repo.ListByBaselineAsync("pack-a", 10, CancellationToken.None);

    checks.Should().HaveCount(2);
    checks.Select(c => c.CheckId).Should().ContainInOrder("a2", "a1");
  }

  [Fact]
  public async Task GetLatest_ReturnsNewest()
  {
    var now = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    await _repo.SaveAsync(BuildCheck("old", now, "pack-a", "pack-b", "NewReleaseFound", null, null), CancellationToken.None);
    await _repo.SaveAsync(BuildCheck("new", now.AddMinutes(5), "pack-a", "pack-c", "DiffGenerated", null, null), CancellationToken.None);

    var latest = await _repo.GetLatestAsync("pack-a", CancellationToken.None);

    latest.Should().NotBeNull();
    latest!.CheckId.Should().Be("new");
    latest.TargetPackId.Should().Be("pack-c");
  }

  [Fact]
  public async Task GetLatest_NoData_ReturnsNull()
  {
    var latest = await _repo.GetLatestAsync("missing-pack", CancellationToken.None);
    latest.Should().BeNull();
  }

  private static ReleaseCheck BuildCheck(
    string checkId,
    DateTimeOffset checkedAt,
    string baselinePackId,
    string? targetPackId,
    string status,
    string? summaryJson,
    string? releaseNotesPath)
  {
    return new ReleaseCheck
    {
      CheckId = checkId,
      CheckedAt = checkedAt,
      BaselinePackId = baselinePackId,
      TargetPackId = targetPackId,
      Status = status,
      SummaryJson = summaryJson,
      ReleaseNotesPath = releaseNotesPath
    };
  }
}

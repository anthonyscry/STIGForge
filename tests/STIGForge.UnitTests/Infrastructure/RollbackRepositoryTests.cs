using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class RollbackRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly SqliteConnection _keepAlive;
  private readonly IRollbackRepository _repo;

  public RollbackRepositoryTests()
  {
    var csb = new SqliteConnectionStringBuilder
    {
      DataSource = "rollback-repo-" + Guid.NewGuid().ToString("N"),
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared
    };

    _cs = csb.ToString();
    _keepAlive = new SqliteConnection(_cs);
    _keepAlive.Open();

    DbBootstrap.EnsureCreated(_cs);
    _repo = new SqliteRollbackRepository(_cs);
  }

  public void Dispose()
  {
    _keepAlive.Dispose();
  }

  [Fact]
  public async Task SaveAndGet_RoundTrips()
  {
    var expected = BuildSnapshot("snap-1", "bundle-a", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));

    await _repo.SaveAsync(expected, CancellationToken.None);
    var actual = await _repo.GetAsync("snap-1", CancellationToken.None);

    actual.Should().NotBeNull();
    actual!.SnapshotId.Should().Be(expected.SnapshotId);
    actual.BundleRoot.Should().Be(expected.BundleRoot);
    actual.Description.Should().Be(expected.Description);
    actual.RollbackScriptPath.Should().Be(expected.RollbackScriptPath);
    actual.RegistryKeys.Should().HaveCount(1);
    actual.FilePaths.Should().HaveCount(1);
    actual.ServiceStates.Should().HaveCount(1);
    actual.GpoSettings.Should().HaveCount(1);
  }

  [Fact]
  public async Task ListByBundle_ReturnsNewestFirst()
  {
    await _repo.SaveAsync(BuildSnapshot("a1", "bundle-a", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero)), CancellationToken.None);
    await _repo.SaveAsync(BuildSnapshot("a2", "bundle-a", new DateTimeOffset(2026, 2, 1, 11, 0, 0, TimeSpan.Zero)), CancellationToken.None);
    await _repo.SaveAsync(BuildSnapshot("b1", "bundle-b", new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)), CancellationToken.None);

    var list = await _repo.ListByBundleAsync("bundle-a", 10, CancellationToken.None);

    list.Should().HaveCount(2);
    list.Select(s => s.SnapshotId).Should().ContainInOrder("a2", "a1");
  }

  [Fact]
  public async Task Get_NoData_ReturnsNull()
  {
    var snapshot = await _repo.GetAsync("missing", CancellationToken.None);
    snapshot.Should().BeNull();
  }

  private static RollbackSnapshot BuildSnapshot(string id, string bundleRoot, DateTimeOffset createdAt) => new()
  {
    SnapshotId = id,
    BundleRoot = bundleRoot,
    Description = "desc",
    CreatedAt = createdAt,
    RegistryKeys = new[]
    {
      new RollbackRegistryKeyState
      {
        Path = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\System",
        ValueName = "EnableSmartScreen",
        Value = "1",
        ValueType = "DWord",
        Exists = true
      }
    },
    FilePaths = new[]
    {
      new RollbackFilePathState
      {
        Path = @"C:\bundle\Manifest\manifest.json",
        Exists = true,
        Sha256 = "abc"
      }
    },
    ServiceStates = new[]
    {
      new RollbackServiceState
      {
        ServiceName = "RemoteRegistry",
        StartupType = "Auto",
        Status = "Running"
      }
    },
    GpoSettings = new[]
    {
      new RollbackGpoSettingState
      {
        SettingPath = "AppliedGpo",
        Value = "Applied",
        GpoName = "Default Domain Policy"
      }
    },
    RollbackScriptPath = @"C:\bundle\Apply\RollbackSnapshots\snap\rollback-apply.ps1"
  };
}

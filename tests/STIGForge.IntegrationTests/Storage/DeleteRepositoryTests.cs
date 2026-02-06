using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.IntegrationTests.Storage;

public class DeleteRepositoryTests : IDisposable
{
  private readonly string _dbPath;
  private readonly string _cs;

  public DeleteRepositoryTests()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), "stigforge-test-" + Guid.NewGuid().ToString("N")[..8] + ".db");
    _cs = $"Data Source={_dbPath}";
    DbBootstrap.EnsureCreated(_cs);
  }

  public void Dispose()
  {
    try { File.Delete(_dbPath); } catch { }
  }

  [Fact]
  public async Task DeletePack_RemovesPack()
  {
    var repo = new SqliteContentPackRepository(_cs);
    var pack = new ContentPack
    {
      PackId = "pack-del-1",
      Name = "DeleteMe",
      ImportedAt = DateTimeOffset.UtcNow,
      SourceLabel = "test",
      HashAlgorithm = "SHA256",
      ManifestSha256 = "abc123"
    };

    await repo.SaveAsync(pack, CancellationToken.None);

    // Verify it exists via raw query (avoids Dapper DateTimeOffset parsing)
    using (var conn = new SqliteConnection(_cs))
    {
      var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM content_packs WHERE pack_id='pack-del-1'");
      count.Should().Be(1);
    }

    // Delete
    await repo.DeleteAsync("pack-del-1", CancellationToken.None);

    // Verify it's gone
    using (var conn = new SqliteConnection(_cs))
    {
      var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM content_packs WHERE pack_id='pack-del-1'");
      count.Should().Be(0);
    }
  }

  [Fact]
  public async Task DeletePack_CascadesControls()
  {
    var packRepo = new SqliteContentPackRepository(_cs);
    var controlRepo = new SqliteJsonControlRepository(_cs);

    var pack = new ContentPack
    {
      PackId = "pack-cascade-1",
      Name = "CascadeMe",
      ImportedAt = DateTimeOffset.UtcNow,
      SourceLabel = "test",
      HashAlgorithm = "SHA256",
      ManifestSha256 = "abc123"
    };

    await packRepo.SaveAsync(pack, CancellationToken.None);
    await controlRepo.SaveControlsAsync("pack-cascade-1", new List<ControlRecord>
    {
      new()
      {
        ControlId = "ctrl1",
        Title = "Control 1",
        Severity = "medium",
        ExternalIds = new ExternalIds { VulnId = "V-1", RuleId = "SV-1" },
        Applicability = new Applicability { OsTarget = OsTarget.Win11, RoleTags = Array.Empty<RoleTemplate>() },
        Revision = new RevisionInfo { PackName = "test" }
      }
    }, CancellationToken.None);

    // Verify controls exist
    var controlsBefore = await controlRepo.ListControlsAsync("pack-cascade-1", CancellationToken.None);
    controlsBefore.Should().HaveCount(1);

    // Delete pack
    await packRepo.DeleteAsync("pack-cascade-1", CancellationToken.None);

    // Verify controls are also gone
    var controlsAfter = await controlRepo.ListControlsAsync("pack-cascade-1", CancellationToken.None);
    controlsAfter.Should().BeEmpty();
  }

  [Fact]
  public async Task DeleteProfile_RemovesProfile()
  {
    var repo = new SqliteJsonProfileRepository(_cs);
    var profile = new Profile
    {
      ProfileId = "prof-del-1",
      Name = "DeleteMe",
      OsTarget = OsTarget.Win11,
      RoleTemplate = RoleTemplate.Workstation,
      HardeningMode = HardeningMode.Safe,
      ClassificationMode = ClassificationMode.Classified,
      NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = true,
        ConfidenceThreshold = Confidence.High,
        DefaultNaCommentTemplate = "NA"
      },
      OverlayIds = Array.Empty<string>()
    };

    await repo.SaveAsync(profile, CancellationToken.None);

    // Verify it exists
    var found = await repo.GetAsync("prof-del-1", CancellationToken.None);
    found.Should().NotBeNull();

    // Delete
    await repo.DeleteAsync("prof-del-1", CancellationToken.None);

    // Verify it's gone
    var deleted = await repo.GetAsync("prof-del-1", CancellationToken.None);
    deleted.Should().BeNull();
  }

  [Fact]
  public async Task DeletePack_NonExistent_DoesNotThrow()
  {
    var repo = new SqliteContentPackRepository(_cs);

    var act = () => repo.DeleteAsync("nonexistent-pack", CancellationToken.None);

    await act.Should().NotThrowAsync();
  }
}

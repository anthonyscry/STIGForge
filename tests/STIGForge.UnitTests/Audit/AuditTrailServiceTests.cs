using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Audit;

public sealed class AuditTrailServiceTests : IDisposable
{
  private readonly string _dbPath;
  private readonly string _connectionString;
  private readonly AuditTrailService _sut;

  public AuditTrailServiceTests()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), "stigforge_audit_test_" + Guid.NewGuid().ToString("n") + ".db");
    _connectionString = "Data Source=" + _dbPath;
    DbBootstrap.EnsureCreated(_connectionString);
    _sut = new AuditTrailService(_connectionString, new SystemClock());
  }

  public void Dispose()
  {
    try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
  }

  [Fact]
  public async Task RecordAsync_SetsTimestampUserAndMachine()
  {
    var entry = new AuditEntry { Action = "apply", Target = "bundle-123", Result = "success", Detail = "Applied STIG" };

    await _sut.RecordAsync(entry, CancellationToken.None);

    var results = await _sut.QueryAsync(new AuditQuery { Limit = 10 }, CancellationToken.None);
    results.Should().HaveCount(1);
    results[0].User.Should().NotBeNullOrWhiteSpace();
    results[0].Machine.Should().NotBeNullOrWhiteSpace();
    results[0].Timestamp.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
    results[0].EntryHash.Should().NotBeNullOrWhiteSpace();
    results[0].PreviousHash.Should().Be("genesis");
  }

  [Fact]
  public async Task RecordAsync_ChainsHashes()
  {
    await _sut.RecordAsync(new AuditEntry { Action = "apply", Target = "b1", Result = "ok", Detail = "First" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "verify", Target = "b1", Result = "ok", Detail = "Second" }, CancellationToken.None);

    var results = await _sut.QueryAsync(new AuditQuery { Limit = 10 }, CancellationToken.None);
    results.Should().HaveCount(2);

    // Results are ordered DESC, so [0] is the second entry
    var second = results[0];
    var first = results[1];

    first.PreviousHash.Should().Be("genesis");
    second.PreviousHash.Should().Be(first.EntryHash);
  }

  [Fact]
  public async Task QueryAsync_FiltersOnAction()
  {
    await _sut.RecordAsync(new AuditEntry { Action = "apply", Target = "b1", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "verify", Target = "b1", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "apply", Target = "b2", Result = "ok", Detail = "" }, CancellationToken.None);

    var results = await _sut.QueryAsync(new AuditQuery { Action = "apply" }, CancellationToken.None);
    results.Should().HaveCount(2);
    results.Should().OnlyContain(e => e.Action == "apply");
  }

  [Fact]
  public async Task QueryAsync_FiltersOnTarget()
  {
    await _sut.RecordAsync(new AuditEntry { Action = "apply", Target = "bundle-abc", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "apply", Target = "bundle-xyz", Result = "ok", Detail = "" }, CancellationToken.None);

    var results = await _sut.QueryAsync(new AuditQuery { Target = "abc" }, CancellationToken.None);
    results.Should().HaveCount(1);
    results[0].Target.Should().Be("bundle-abc");
  }

  [Fact]
  public async Task VerifyIntegrityAsync_ReturnsTrueForValidChain()
  {
    await _sut.RecordAsync(new AuditEntry { Action = "a1", Target = "t1", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "a2", Target = "t2", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "a3", Target = "t3", Result = "ok", Detail = "" }, CancellationToken.None);

    var valid = await _sut.VerifyIntegrityAsync(CancellationToken.None);
    valid.Should().BeTrue();
  }

  [Fact]
  public async Task VerifyIntegrityAsync_ReturnsFalseForTamperedEntry()
  {
    await _sut.RecordAsync(new AuditEntry { Action = "a1", Target = "t1", Result = "ok", Detail = "" }, CancellationToken.None);
    await _sut.RecordAsync(new AuditEntry { Action = "a2", Target = "t2", Result = "ok", Detail = "" }, CancellationToken.None);

    // Tamper with an entry
    using var conn = new SqliteConnection(_connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "UPDATE audit_trail SET result = 'tampered' WHERE id = 1";
    cmd.ExecuteNonQuery();

    var valid = await _sut.VerifyIntegrityAsync(CancellationToken.None);
    valid.Should().BeFalse();
  }

  [Fact]
  public async Task VerifyIntegrityAsync_ReturnsTrueForEmptyTrail()
  {
    var valid = await _sut.VerifyIntegrityAsync(CancellationToken.None);
    valid.Should().BeTrue();
  }

  [Fact]
  public void ComputeEntryHash_IsDeterministic()
  {
    var entry = new AuditEntry
    {
      Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
      User = "admin", Machine = "SRV01", Action = "apply", Target = "bundle-1",
      Result = "ok", Detail = "test", PreviousHash = "genesis"
    };

    var hash1 = AuditTrailService.ComputeEntryHash(entry);
    var hash2 = AuditTrailService.ComputeEntryHash(entry);

    hash1.Should().Be(hash2);
    hash1.Should().HaveLength(64); // SHA-256 = 64 hex chars
  }
}

using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ExceptionRepositoryTests : IDisposable
{
  private readonly string _cs;
  private readonly SqliteConnection _keeper;
  private readonly SqliteExceptionRepository _repo;

  public ExceptionRepositoryTests()
  {
    _cs = "Data Source=exception-repo-tests;Mode=Memory;Cache=Shared";
    _keeper = new SqliteConnection(_cs);
    _keeper.Open();
    DbBootstrap.EnsureCreated(_cs);
    _repo = new SqliteExceptionRepository(new DbConnectionString(_cs));
  }

  public void Dispose()
  {
    _keeper.Dispose();
  }

  [Fact]
  public async Task SaveAndGet_RoundTrips()
  {
    var createdAt = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    var expiresAt = createdAt.AddDays(45);
    var exception = new ControlException
    {
      ExceptionId = "ex-1",
      BundleRoot = @"C:\BundleA",
      RuleId = "SV-1111",
      VulnId = "V-1111",
      ExceptionType = "Waiver",
      Status = "Active",
      RiskLevel = "High",
      ApprovedBy = "approver",
      Justification = "Mission impact",
      JustificationDoc = @"C:\Docs\waiver.pdf",
      CreatedAt = createdAt,
      ExpiresAt = expiresAt
    };

    await _repo.SaveAsync(exception, CancellationToken.None);
    var loaded = await _repo.GetAsync("ex-1", CancellationToken.None);

    loaded.Should().NotBeNull();
    loaded!.ExceptionId.Should().Be("ex-1");
    loaded.BundleRoot.Should().Be(@"C:\BundleA");
    loaded.RuleId.Should().Be("SV-1111");
    loaded.VulnId.Should().Be("V-1111");
    loaded.ExceptionType.Should().Be("Waiver");
    loaded.Status.Should().Be("Active");
    loaded.RiskLevel.Should().Be("High");
    loaded.ApprovedBy.Should().Be("approver");
    loaded.Justification.Should().Be("Mission impact");
    loaded.JustificationDoc.Should().Be(@"C:\Docs\waiver.pdf");
    loaded.CreatedAt.Should().Be(createdAt);
    loaded.ExpiresAt.Should().Be(expiresAt);
    loaded.RevokedAt.Should().BeNull();
    loaded.RevokedBy.Should().BeNull();
  }

  [Fact]
  public async Task ListByBundle_FiltersCorrectly()
  {
    var now = DateTimeOffset.UtcNow;
    await _repo.SaveAsync(BuildException("ex-a", @"C:\BundleA", "SV-1", now), CancellationToken.None);
    await _repo.SaveAsync(BuildException("ex-b", @"C:\BundleA", "SV-2", now.AddMinutes(1)), CancellationToken.None);
    await _repo.SaveAsync(BuildException("ex-c", @"C:\BundleB", "SV-3", now), CancellationToken.None);

    var results = await _repo.ListByBundleAsync(@"C:\BundleA", CancellationToken.None);

    results.Should().HaveCount(2);
    results.Select(e => e.ExceptionId).Should().BeEquivalentTo(new[] { "ex-a", "ex-b" });
  }

  [Fact]
  public async Task ListActiveByRule_ExcludesExpiredAndRevoked()
  {
    var now = DateTimeOffset.UtcNow;
    await _repo.SaveAsync(new ControlException
    {
      ExceptionId = "active",
      BundleRoot = @"C:\BundleA",
      RuleId = "SV-5000",
      ExceptionType = "TechnicalException",
      Status = "Active",
      RiskLevel = "Medium",
      ApprovedBy = "approver",
      CreatedAt = now,
      ExpiresAt = now.AddDays(3)
    }, CancellationToken.None);

    await _repo.SaveAsync(new ControlException
    {
      ExceptionId = "expired",
      BundleRoot = @"C:\BundleA",
      RuleId = "SV-5000",
      ExceptionType = "Waiver",
      Status = "Active",
      RiskLevel = "Low",
      ApprovedBy = "approver",
      CreatedAt = now,
      ExpiresAt = now.AddMinutes(-1)
    }, CancellationToken.None);

    await _repo.SaveAsync(new ControlException
    {
      ExceptionId = "revoked",
      BundleRoot = @"C:\BundleA",
      RuleId = "SV-5000",
      ExceptionType = "RiskAcceptance",
      Status = "Revoked",
      RiskLevel = "High",
      ApprovedBy = "approver",
      CreatedAt = now,
      ExpiresAt = now.AddDays(2)
    }, CancellationToken.None);

    await _repo.SaveAsync(BuildException("other-rule", @"C:\BundleA", "SV-9999", now), CancellationToken.None);

    var results = await _repo.ListActiveByRuleAsync(@"C:\BundleA", "SV-5000", CancellationToken.None);

    results.Should().HaveCount(1);
    results[0].ExceptionId.Should().Be("active");
  }

  [Fact]
  public async Task Revoke_UpdatesStatus()
  {
    var now = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    await _repo.SaveAsync(BuildException("to-revoke", @"C:\BundleA", "SV-7000", now), CancellationToken.None);

    var revokedAt = now.AddHours(1);
    await _repo.RevokeAsync("to-revoke", "ao", revokedAt, CancellationToken.None);
    var updated = await _repo.GetAsync("to-revoke", CancellationToken.None);

    updated.Should().NotBeNull();
    updated!.Status.Should().Be("Revoked");
    updated.RevokedBy.Should().Be("ao");
    updated.RevokedAt.Should().Be(revokedAt);
  }

  private static ControlException BuildException(string exceptionId, string bundleRoot, string ruleId, DateTimeOffset createdAt) => new()
  {
    ExceptionId = exceptionId,
    BundleRoot = bundleRoot,
    RuleId = ruleId,
    ExceptionType = "Waiver",
    Status = "Active",
    RiskLevel = "Medium",
    ApprovedBy = "approver",
    CreatedAt = createdAt,
    ExpiresAt = createdAt.AddDays(10)
  };
}

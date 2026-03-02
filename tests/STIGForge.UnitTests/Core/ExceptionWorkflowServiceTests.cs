using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class ExceptionWorkflowServiceTests
{
  [Fact]
  public async Task CreateException_AssignsIdAndTimestamp()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);

    ControlException? saved = null;
    repo.Setup(r => r.SaveAsync(It.IsAny<ControlException>(), It.IsAny<CancellationToken>()))
      .Callback<ControlException, CancellationToken>((e, _) => saved = e)
      .Returns(Task.CompletedTask);

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    var request = new CreateExceptionRequest
    {
      BundleRoot = @"C:\Bundle",
      RuleId = "SV-1000",
      ExceptionType = "Waiver",
      RiskLevel = "Medium",
      ApprovedBy = "approver"
    };

    var result = await service.CreateExceptionAsync(request, CancellationToken.None);

    result.ExceptionId.Should().NotBeNullOrWhiteSpace();
    result.CreatedAt.Should().Be(now);
    saved.Should().NotBeNull();
    saved!.CreatedAt.Should().Be(now);
  }

  [Fact]
  public async Task CreateException_WithExpiration_SetsExpiresAt()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var expiresAt = now.AddDays(14);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);
    repo.Setup(r => r.SaveAsync(It.IsAny<ControlException>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    var request = new CreateExceptionRequest
    {
      BundleRoot = @"C:\Bundle",
      RuleId = "SV-2000",
      ExceptionType = "RiskAcceptance",
      RiskLevel = "Low",
      ApprovedBy = "approver",
      ExpiresAt = expiresAt
    };

    var result = await service.CreateExceptionAsync(request, CancellationToken.None);

    result.ExpiresAt.Should().Be(expiresAt);
  }

  [Fact]
  public async Task CreateException_RequiresRuleId_Throws()
  {
    var repo = new Mock<IExceptionRepository>();
    var service = new ExceptionWorkflowService(repo.Object);
    var request = new CreateExceptionRequest
    {
      BundleRoot = @"C:\Bundle",
      RuleId = string.Empty,
      ExceptionType = "Waiver",
      RiskLevel = "High",
      ApprovedBy = "approver"
    };

    var act = () => service.CreateExceptionAsync(request, CancellationToken.None);

    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task GetExpiredExceptions_ReturnsOnlyExpired()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);
    repo.Setup(r => r.ListByBundleAsync("bundle", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlException>
    {
      new() { ExceptionId = "exp", Status = "Active", ExpiresAt = now.AddMinutes(-1) },
      new() { ExceptionId = "active", Status = "Active", ExpiresAt = now.AddMinutes(10) },
      new() { ExceptionId = "revoked", Status = "Revoked", ExpiresAt = now.AddMinutes(-5) },
      new() { ExceptionId = "none", Status = "Active", ExpiresAt = null }
    });

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    var result = await service.GetExpiredExceptionsAsync("bundle", CancellationToken.None);

    result.Select(e => e.ExceptionId).Should().Equal("exp");
  }

  [Fact]
  public async Task GetActiveExceptions_ExcludesExpiredAndRevoked()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);
    repo.Setup(r => r.ListByBundleAsync("bundle", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlException>
    {
      new() { ExceptionId = "future", Status = "Active", ExpiresAt = now.AddDays(1) },
      new() { ExceptionId = "no-expiry", Status = "Active", ExpiresAt = null },
      new() { ExceptionId = "expired", Status = "Active", ExpiresAt = now.AddMinutes(-1) },
      new() { ExceptionId = "revoked", Status = "Revoked", ExpiresAt = now.AddDays(1) }
    });

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    var result = await service.GetActiveExceptionsAsync("bundle", CancellationToken.None);

    result.Select(e => e.ExceptionId).Should().BeEquivalentTo(new[] { "future", "no-expiry" });
  }

  [Fact]
  public async Task IsRuleCovered_ActiveException_ReturnsTrue()
  {
    var repo = new Mock<IExceptionRepository>();
    repo.Setup(r => r.ListActiveByRuleAsync("bundle", "SV-3000", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlException>
    {
      new() { ExceptionId = "e1", Status = "Active" }
    });

    var service = new ExceptionWorkflowService(repo.Object);
    var covered = await service.IsRuleCoveredByActiveExceptionAsync("bundle", "SV-3000", CancellationToken.None);

    covered.Should().BeTrue();
  }

  [Fact]
  public async Task IsRuleCovered_NoException_ReturnsFalse()
  {
    var repo = new Mock<IExceptionRepository>();
    repo.Setup(r => r.ListActiveByRuleAsync("bundle", "SV-3001", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ControlException>());

    var service = new ExceptionWorkflowService(repo.Object);
    var covered = await service.IsRuleCoveredByActiveExceptionAsync("bundle", "SV-3001", CancellationToken.None);

    covered.Should().BeFalse();
  }

  [Fact]
  public async Task RevokeException_CallsRepository()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    await service.RevokeExceptionAsync("ex-1", "security-officer", CancellationToken.None);

    repo.Verify(r => r.RevokeAsync("ex-1", "security-officer", now, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task AuditExceptions_ReturnsCorrectCounts()
  {
    var now = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var repo = new Mock<IExceptionRepository>();
    var clock = new Mock<IClock>();
    clock.Setup(c => c.Now).Returns(now);
    repo.Setup(r => r.ListByBundleAsync("bundle", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlException>
    {
      new() { ExceptionId = "a1", Status = "Active", RiskLevel = "HIGH", ExpiresAt = now.AddDays(10) },
      new() { ExceptionId = "a2", Status = "Active", RiskLevel = "Medium", ExpiresAt = now.AddDays(60) },
      new() { ExceptionId = "a3", Status = "Active", RiskLevel = "Low", ExpiresAt = null },
      new() { ExceptionId = "e1", Status = "Active", RiskLevel = "High", ExpiresAt = now.AddMinutes(-1) },
      new() { ExceptionId = "e2", Status = "Active", RiskLevel = "Low", ExpiresAt = now },
      new() { ExceptionId = "r1", Status = "Revoked", RiskLevel = "High", ExpiresAt = now.AddDays(15) }
    });

    var service = new ExceptionWorkflowService(repo.Object, clock.Object);
    var report = await service.AuditExceptionsAsync("bundle", CancellationToken.None);

    report.ActiveCount.Should().Be(3);
    report.ExpiredCount.Should().Be(2);
    report.RevokedCount.Should().Be(1);
    report.ExpiringWithin30Days.Should().Be(1);
    report.HighRiskActiveCount.Should().Be(1);
  }
}

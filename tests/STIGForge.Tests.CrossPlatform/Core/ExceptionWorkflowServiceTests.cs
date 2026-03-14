using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public class ExceptionWorkflowServiceTests
{
    private readonly Mock<IExceptionRepository> _mockRepo;
    private readonly Mock<IClock> _mockClock;
    private readonly ExceptionWorkflowService _sut;
    private static readonly DateTimeOffset _now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public ExceptionWorkflowServiceTests()
    {
        _mockRepo = new Mock<IExceptionRepository>();
        _mockClock = new Mock<IClock>();
        _mockClock.SetupGet(c => c.Now).Returns(_now);
        _sut = new ExceptionWorkflowService(_mockRepo.Object, _mockClock.Object);
    }

    private ControlException Active(DateTimeOffset? expires = null) => new()
    {
        ExceptionId = Guid.NewGuid().ToString("N"),
        BundleRoot = "bundle",
        RuleId = "V-1",
        Status = "Active",
        RiskLevel = "Medium",
        ApprovedBy = "admin",
        ExpiresAt = expires
    };

    private ControlException Revoked(DateTimeOffset? expires = null) => new()
    {
        ExceptionId = Guid.NewGuid().ToString("N"),
        BundleRoot = "bundle",
        RuleId = "V-2",
        Status = "Revoked",
        RiskLevel = "Low",
        ApprovedBy = "admin",
        ExpiresAt = expires
    };

    private void SetupList(params ControlException[] exceptions)
    {
        _mockRepo
            .Setup(r => r.ListByBundleAsync("bundle", It.IsAny<CancellationToken>()))
            .ReturnsAsync(exceptions.ToList());
    }

    // ── GetExpiredExceptionsAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetExpiredExceptions_ReturnsOnlyActiveWithPastExpiry()
    {
        var expired = Active(expires: _now.AddDays(-1));
        var notExpired = Active(expires: _now.AddDays(10));
        SetupList(expired, notExpired);

        var result = await _sut.GetExpiredExceptionsAsync("bundle", CancellationToken.None);

        result.Should().ContainSingle().Which.ExceptionId.Should().Be(expired.ExceptionId);
    }

    [Fact]
    public async Task GetExpiredExceptions_IgnoresRevoked()
    {
        var revokedWithPastExpiry = Revoked(expires: _now.AddDays(-1));
        SetupList(revokedWithPastExpiry);

        var result = await _sut.GetExpiredExceptionsAsync("bundle", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpiredExceptions_IgnoresNoExpiry()
    {
        var activeNoExpiry = Active(expires: null);
        SetupList(activeNoExpiry);

        var result = await _sut.GetExpiredExceptionsAsync("bundle", CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── GetActiveExceptionsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetActiveExceptions_ReturnsActiveWithFutureOrNoExpiry()
    {
        var futureExpiry = Active(expires: _now.AddDays(5));
        var noExpiry = Active(expires: null);
        SetupList(futureExpiry, noExpiry);

        var result = await _sut.GetActiveExceptionsAsync("bundle", CancellationToken.None);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveExceptions_ExcludesExpiredAndRevoked()
    {
        var expired = Active(expires: _now.AddDays(-1));
        var revoked = Revoked(expires: _now.AddDays(10));
        SetupList(expired, revoked);

        var result = await _sut.GetActiveExceptionsAsync("bundle", CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── AuditExceptionsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task AuditExceptions_CountsActiveExpiredRevoked()
    {
        var active1 = Active(expires: _now.AddDays(10));
        var active2 = Active(expires: null);
        var expired = Active(expires: _now.AddDays(-1));
        var revoked = Revoked();
        SetupList(active1, active2, expired, revoked);

        var report = await _sut.AuditExceptionsAsync("bundle", CancellationToken.None);

        report.ActiveCount.Should().Be(2);
        report.ExpiredCount.Should().Be(1);
        report.RevokedCount.Should().Be(1);
    }

    [Fact]
    public async Task AuditExceptions_FlagsHighRiskActive()
    {
        var highRisk = Active(expires: _now.AddDays(10));
        highRisk.RiskLevel = "High";
        var lowRisk = Active(expires: null);
        lowRisk.RiskLevel = "Low";
        SetupList(highRisk, lowRisk);

        var report = await _sut.AuditExceptionsAsync("bundle", CancellationToken.None);

        report.HighRiskActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task AuditExceptions_IdentifiesExpiringWithin30Days()
    {
        var expiringSoon = Active(expires: _now.AddDays(15));
        var expiringSoon2 = Active(expires: _now.AddDays(29));
        var farFuture = Active(expires: _now.AddDays(60));
        SetupList(expiringSoon, expiringSoon2, farFuture);

        var report = await _sut.AuditExceptionsAsync("bundle", CancellationToken.None);

        report.ExpiringWithin30Days.Should().Be(2);
        report.ExpiringExceptions.Should().HaveCount(2);
    }

    // ── CreateExceptionAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateException_MissingRuleId_ThrowsArgumentException()
    {
        var req = new CreateExceptionRequest
        {
            RuleId = "",
            ApprovedBy = "admin",
            ExceptionType = "Waiver",
            RiskLevel = "Low"
        };

        await _sut.Invoking(s => s.CreateExceptionAsync(req, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*RuleId*");
    }

    [Fact]
    public async Task CreateException_MissingApprovedBy_ThrowsArgumentException()
    {
        var req = new CreateExceptionRequest
        {
            RuleId = "V-Rule-1",
            ApprovedBy = "",
            ExceptionType = "Waiver",
            RiskLevel = "Low"
        };

        await _sut.Invoking(s => s.CreateExceptionAsync(req, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ApprovedBy*");
    }

    // ── RevokeExceptionAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RevokeException_EmptyExceptionId_Throws()
    {
        await _sut.Invoking(s => s.RevokeExceptionAsync("", "revoker", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── IsRuleCoveredByActiveExceptionAsync ───────────────────────────────

    [Fact]
    public async Task IsRuleCovered_ActiveExceptionExists_ReturnsTrue()
    {
        _mockRepo
            .Setup(r => r.ListActiveByRuleAsync("bundle", "V-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ControlException> { Active() });

        var result = await _sut.IsRuleCoveredByActiveExceptionAsync("bundle", "V-1", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRuleCovered_NoActiveException_ReturnsFalse()
    {
        _mockRepo
            .Setup(r => r.ListActiveByRuleAsync("bundle", "V-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ControlException>());

        var result = await _sut.IsRuleCoveredByActiveExceptionAsync("bundle", "V-1", CancellationToken.None);

        result.Should().BeFalse();
    }
}

using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.IntegrationTests.Storage;

public class AuditTrailIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _cs;

    public AuditTrailIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "stigforge-test-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        _cs = $"Data Source={_dbPath}";
        DbBootstrap.EnsureCreated(_cs);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task RecordAndQuery_RoundTrip()
    {
        var clock = new FakeClock();
        var svc = new AuditTrailService(_cs, clock);

        var entry = new AuditEntry
        {
            Action = "apply",
            Target = "bundle-001",
            Result = "success",
            Detail = "Applied 42 controls"
        };

        await svc.RecordAsync(entry, CancellationToken.None);

        var results = await svc.QueryAsync(new AuditQuery { Action = "apply" }, CancellationToken.None);

        results.Should().HaveCount(1);
        var recorded = results[0];
        recorded.Action.Should().Be("apply");
        recorded.Target.Should().Be("bundle-001");
        recorded.Result.Should().Be("success");
        recorded.Detail.Should().Be("Applied 42 controls");
        recorded.EntryHash.Should().NotBeNullOrWhiteSpace();
        recorded.PreviousHash.Should().Be("genesis");
    }

    [Fact]
    public async Task VerifyIntegrity_ValidChain_ReturnsTrue()
    {
        var clock = new FakeClock();
        var svc = new AuditTrailService(_cs, clock);

        await svc.RecordAsync(new AuditEntry { Action = "apply", Target = "t1", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "verify", Target = "t2", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "export", Target = "t3", Result = "ok" }, CancellationToken.None);

        var valid = await svc.VerifyIntegrityAsync(CancellationToken.None);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrity_EmptyTrail_ReturnsTrue()
    {
        var clock = new FakeClock();
        var svc = new AuditTrailService(_cs, clock);

        var valid = await svc.VerifyIntegrityAsync(CancellationToken.None);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task Query_FilterByAction_ReturnsOnlyMatching()
    {
        var clock = new FakeClock();
        var svc = new AuditTrailService(_cs, clock);

        await svc.RecordAsync(new AuditEntry { Action = "apply", Target = "t1", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "verify", Target = "t2", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "apply", Target = "t3", Result = "fail" }, CancellationToken.None);

        var results = await svc.QueryAsync(new AuditQuery { Action = "apply" }, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(e => e.Action == "apply");
    }

    [Fact]
    public async Task Query_FilterByTarget_ReturnsOnlyMatching()
    {
        var clock = new FakeClock();
        var svc = new AuditTrailService(_cs, clock);

        await svc.RecordAsync(new AuditEntry { Action = "apply", Target = "bundle-alpha", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "apply", Target = "bundle-beta", Result = "ok" }, CancellationToken.None);
        await svc.RecordAsync(new AuditEntry { Action = "verify", Target = "bundle-alpha-2", Result = "ok" }, CancellationToken.None);

        var results = await svc.QueryAsync(new AuditQuery { Target = "alpha" }, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(e => e.Target.Contains("alpha"));
    }
}

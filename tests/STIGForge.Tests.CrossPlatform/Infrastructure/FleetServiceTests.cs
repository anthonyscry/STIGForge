using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.System;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

/// <summary>
/// Cross-platform tests for FleetService.
/// WinRM/PowerShell paths fail gracefully on Linux — the service wraps all
/// process-launch exceptions and returns failure results, so we can verify
/// request validation, result aggregation, and audit integration.
/// </summary>
public sealed class FleetServiceTests
{
    private static FleetService CreateService(
        ICredentialStore? credentialStore = null,
        IAuditTrailService? audit = null)
        => new FleetService(new ProcessRunner(), credentialStore, audit);

    // ── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullTargetsList_ThrowsArgumentException()
    {
        var svc = CreateService();
        var request = new FleetRequest { Targets = null! };

        var act = () => svc.ExecuteAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyTargets_ThrowsArgumentException()
    {
        var svc = CreateService();
        var request = new FleetRequest { Targets = [] };

        var act = () => svc.ExecuteAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*target*");
    }

    // ── Cross-platform execution (process launch failure → graceful result) ──

    [Fact]
    public async Task ExecuteAsync_SingleTarget_InvalidHostChars_ReturnsFailureResult()
    {
        var svc = CreateService();
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "invalid host!" }],
            Operation = "orchestrate"
        };

        // ValidateHostIdentifier rejects the hostname → exception caught by ExecuteOnMachineAsync
        var result = await svc.ExecuteAsync(request, CancellationToken.None);

        result.TotalMachines.Should().Be(1);
        result.FailureCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.MachineResults[0].Success.Should().BeFalse();
        result.MachineResults[0].ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task ExecuteAsync_ValidHostName_ProcessLaunchFails_ReturnsFailureGracefully()
    {
        // On Linux powershell.exe does not exist; the exception is caught and wrapped
        var svc = CreateService();
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "server01" }],
            Operation = "orchestrate",
            TimeoutSeconds = 5
        };

        var result = await svc.ExecuteAsync(request, CancellationToken.None);

        result.TotalMachines.Should().Be(1);
        result.MachineResults.Should().HaveCount(1);
        result.MachineResults[0].MachineName.Should().Be("server01");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleTargets_AggregatesAllResults()
    {
        var svc = CreateService();
        var request = new FleetRequest
        {
            Targets =
            [
                new FleetTarget { HostName = "host-a" },
                new FleetTarget { HostName = "host-b" },
                new FleetTarget { HostName = "host-c" }
            ],
            MaxConcurrency = 3,
            TimeoutSeconds = 5
        };

        var result = await svc.ExecuteAsync(request, CancellationToken.None);

        result.TotalMachines.Should().Be(3);
        result.MachineResults.Should().HaveCount(3);
        (result.SuccessCount + result.FailureCount).Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_SetsOperationOnResult()
    {
        var svc = CreateService();
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "srv01" }],
            Operation = "apply",
            TimeoutSeconds = 5
        };

        var result = await svc.ExecuteAsync(request, CancellationToken.None);

        result.Operation.Should().Be("apply");
    }

    [Fact]
    public async Task ExecuteAsync_SetsTimestamps()
    {
        var svc = CreateService();
        var before = DateTimeOffset.Now;

        var result = await svc.ExecuteAsync(
            new FleetRequest
            {
                Targets = [new FleetTarget { HostName = "ts-host" }],
                TimeoutSeconds = 5
            },
            CancellationToken.None);

        result.StartedAt.Should().BeOnOrAfter(before);
        result.FinishedAt.Should().BeOnOrAfter(result.StartedAt);
    }

    // ── Audit integration ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RecordsPerHostAuditEntry()
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var svc = CreateService(audit: audit.Object);
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "audit-host" }],
            Operation = "apply",
            TimeoutSeconds = 5
        };

        await svc.ExecuteAsync(request, CancellationToken.None);

        // At minimum 2 audit calls: one per-host + one summary
        audit.Verify(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_AuditFailure_DoesNotPropagateException()
    {
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("audit unavailable"));

        var svc = CreateService(audit: audit.Object);
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "host-noaudit" }],
            TimeoutSeconds = 5
        };

        // Should NOT throw even if audit service is broken
        var act = () => svc.ExecuteAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ── CheckStatusAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CheckStatusAsync_SingleTarget_ReturnsStatusResult()
    {
        var svc = CreateService();
        var targets = new List<FleetTarget> { new() { HostName = "status-host" } };

        var result = await svc.CheckStatusAsync(targets, CancellationToken.None);

        result.TotalMachines.Should().Be(1);
        result.MachineStatuses.Should().HaveCount(1);
        result.MachineStatuses[0].MachineName.Should().Be("status-host");
        (result.ReachableCount + result.UnreachableCount).Should().Be(1);
    }

    [Fact]
    public async Task CheckStatusAsync_MultipleTargets_ReturnsAllStatuses()
    {
        var svc = CreateService();
        var targets = new List<FleetTarget>
        {
            new() { HostName = "host1" },
            new() { HostName = "host2" }
        };

        var result = await svc.CheckStatusAsync(targets, CancellationToken.None);

        result.TotalMachines.Should().Be(2);
        result.MachineStatuses.Should().HaveCount(2);
    }

    // ── Credential resolution ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CredentialStore_QueriedForTargetWithoutCreds()
    {
        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.Load("cred-host"))
             .Returns(("admin", "secret"));

        var svc = CreateService(credentialStore: creds.Object);
        var request = new FleetRequest
        {
            Targets = [new FleetTarget { HostName = "cred-host" }],
            TimeoutSeconds = 5
        };

        await svc.ExecuteAsync(request, CancellationToken.None);

        creds.Verify(c => c.Load("cred-host"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TargetWithExplicitCreds_SkipsCredentialStore()
    {
        var creds = new Mock<ICredentialStore>();

        var svc = CreateService(credentialStore: creds.Object);
        var request = new FleetRequest
        {
            Targets =
            [
                new FleetTarget
                {
                    HostName = "explicit-host",
                    CredentialUser = "user",
                    CredentialPassword = "pass"
                }
            ],
            TimeoutSeconds = 5
        };

        await svc.ExecuteAsync(request, CancellationToken.None);

        creds.Verify(c => c.Load(It.IsAny<string>()), Times.Never);
    }
}

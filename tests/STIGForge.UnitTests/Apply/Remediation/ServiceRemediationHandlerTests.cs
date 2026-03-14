using System.Diagnostics;
using FluentAssertions;
using Moq;
using STIGForge.Apply.Remediation;
using STIGForge.Apply.Remediation.Handlers;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Apply.Remediation;

public sealed class ServiceRemediationHandlerTests
{
    // ── Constructor / Properties ─────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsRuleId()
    {
        var h = MakeHandler("SV-123");

        h.RuleId.Should().Be("SV-123");
    }

    [Fact]
    public void Constructor_CategoryIsService()
    {
        var h = MakeHandler();

        h.Category.Should().Be("Service");
    }

    [Fact]
    public void Constructor_SetsDescription()
    {
        var h = MakeHandler(description: "Disable XYZ");

        h.Description.Should().Be("Disable XYZ");
    }

    // ── TestAsync (no process runner – simulation) ───────────────────────────

    [Fact]
    public async Task TestAsync_NoProcessRunner_ReturnsSuccessWithSimulationMessage()
    {
        var h = MakeHandler();
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
    }

    // ── ApplyAsync (dry-run delegates to TestAsync) ──────────────────────────

    [Fact]
    public async Task ApplyAsync_DryRun_ReturnsTestResult()
    {
        var h = MakeHandler();
        var ctx = MakeContext(dryRun: true);

        var result = await h.ApplyAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    // ── TestAsync with mock process runner ───────────────────────────────────

    [Fact]
    public async Task TestAsync_ServiceNotFound_ReturnsFailureResult()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = string.Empty });

        var h = MakeHandler(processRunner: runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task TestAsync_ServiceReturnsCompliantState_DetailContainsCompliant()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "Disabled|Stopped" });

        var h = new ServiceRemediationHandler(
            "SV-T1", "RemoteRegistry", "Disabled", "Stopped", "test desc", runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Detail.Should().Contain("compliant");
    }

    [Fact]
    public async Task TestAsync_ServiceNonCompliant_DetailContainsNonCompliant()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "Automatic|Running" });

        var h = new ServiceRemediationHandler(
            "SV-T2", "RemoteRegistry", "Disabled", "Stopped", "test desc", runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Detail.Should().Contain("Non-compliant");
    }

    [Fact]
    public async Task ApplyAsync_ProcessRunnerFails_ReturnsFailureResult()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult { ExitCode = 1, StandardError = "Access denied" });

        var h = MakeHandler(processRunner: runner.Object);
        var ctx = MakeContext(dryRun: false);

        // Process runner returns exit code 1 → RunPowerShellAsync throws → BuildResult returns failure
        var act = () => h.ApplyAsync(ctx, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ServiceRemediationHandler MakeHandler(
        string ruleId = "SV-999",
        string serviceName = "TestSvc",
        string startType = "Disabled",
        string status = "Stopped",
        string description = "Test service handler",
        IProcessRunner? processRunner = null)
        => new(ruleId, serviceName, startType, status, description, processRunner);

    private static RemediationContext MakeContext(bool dryRun = false)
        => new() { BundleRoot = @"C:\bundle", Mode = STIGForge.Core.Models.HardeningMode.Safe, DryRun = dryRun };
}

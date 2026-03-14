using System.Diagnostics;
using FluentAssertions;
using Moq;
using STIGForge.Apply.Remediation;
using STIGForge.Apply.Remediation.Handlers;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Apply.Remediation;

public sealed class AuditPolicyRemediationHandlerTests
{
    // ── Constructor / Properties ─────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsRuleId()
    {
        var h = MakeHandler("SV-APH-1");

        h.RuleId.Should().Be("SV-APH-1");
    }

    [Fact]
    public void Constructor_CategoryIsAuditPolicy()
    {
        var h = MakeHandler();

        h.Category.Should().Be("AuditPolicy");
    }

    [Fact]
    public void Constructor_SetsDescription()
    {
        var h = MakeHandler(description: "Enable logon auditing");

        h.Description.Should().Be("Enable logon auditing");
    }

    // ── TestAsync (no process runner – simulation) ───────────────────────────

    [Fact]
    public async Task TestAsync_NoProcessRunner_ReturnsSuccess()
    {
        var h = MakeHandler();
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    // ── ApplyAsync dry-run ───────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_DryRun_DelegatesToTest()
    {
        var h = MakeHandler();
        var ctx = MakeContext(dryRun: true);

        var result = await h.ApplyAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    // ── TestAsync with mock process runner ───────────────────────────────────

    [Fact]
    public async Task TestAsync_AuditpolReturnsNoOutput_ReturnsFailure()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = string.Empty });

        var h = MakeHandler(processRunner: runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no output");
    }

    [Fact]
    public async Task TestAsync_CompliantAuditLine_ReturnsAlreadyCompliant()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult
              {
                  ExitCode = 0,
                  StandardOutput = "  Logon                           Success and Failure"
              });

        var h = new AuditPolicyRemediationHandler(
            "SV-APH-C", "Logon", "Success and Failure", "desc", runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Detail.Should().Contain("compliant");
    }

    [Fact]
    public async Task TestAsync_NonCompliantAuditLine_ReturnsNonCompliant()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult
              {
                  ExitCode = 0,
                  StandardOutput = "  Logon                           No Auditing"
              });

        var h = new AuditPolicyRemediationHandler(
            "SV-APH-N", "Logon", "Success and Failure", "desc", runner.Object);
        var ctx = MakeContext();

        var result = await h.TestAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Detail.Should().Contain("Non-compliant");
    }

    [Fact]
    public void Constructor_InvalidSubcategory_ThrowsOnFirstCall()
    {
        // Subcategory with invalid characters – should throw during TestAsync
        var h = new AuditPolicyRemediationHandler(
            "SV-APH-INV", "Invalid;Subcategory!", "Failure", "bad subcat", null);

        Func<Task> act = () => h.TestAsync(MakeContext(), CancellationToken.None);
        act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ApplyAsync non-dry-run, unsupported setting ──────────────────────────

    [Fact]
    public async Task ApplyAsync_UnsupportedSetting_ReturnsFailure()
    {
        var runner = new Mock<IProcessRunner>();
        // First call (TestAsync): returns non-compliant output
        runner.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProcessResult
              {
                  ExitCode = 0,
                  StandardOutput = "  Logon  No Auditing"
              });

        var h = new AuditPolicyRemediationHandler(
            "SV-APH-U", "Logon", "UnsupportedValue", "desc", runner.Object);
        var ctx = MakeContext(dryRun: false);

        var result = await h.ApplyAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuditPolicyRemediationHandler MakeHandler(
        string ruleId = "SV-AP-1",
        string subcategory = "Logon",
        string expectedSetting = "Failure",
        string description = "Audit policy test",
        IProcessRunner? processRunner = null)
        => new(ruleId, subcategory, expectedSetting, description, processRunner);

    private static RemediationContext MakeContext(bool dryRun = false)
        => new() { BundleRoot = @"C:\bundle", Mode = STIGForge.Core.Models.HardeningMode.Safe, DryRun = dryRun };
}

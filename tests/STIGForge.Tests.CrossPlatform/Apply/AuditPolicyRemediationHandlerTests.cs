using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using STIGForge.Apply.Remediation.Handlers;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Apply;

public sealed class AuditPolicyRemediationHandlerTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static AuditPolicyRemediationHandler CreateHandler(
        string subcategory,
        string expectedSetting,
        IProcessRunner? runner = null)
        => new("SV-000001", subcategory, expectedSetting, "Unit test handler", runner);

    private static RemediationContext CreateContext(bool dryRun = false)
        => new()
        {
            BundleRoot = "C:\\bundle",
            Mode = HardeningMode.Safe,
            DryRun = dryRun,
            Control = new ControlRecord
            {
                ControlId = "CTRL-1",
                SourcePackId = "pack",
                ExternalIds = new ExternalIds { RuleId = "SV-000001" },
                Title = "Audit test"
            }
        };

    private static Mock<IProcessRunner> MockRunner(string standardOutput)
    {
        var mock = new Mock<IProcessRunner>();
        mock.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = standardOutput });
        return mock;
    }

    private static string AuditpolLine(string subcategory, string setting)
        => $"  {subcategory}                    {setting}";

    // ── Constructor / validation ─────────────────────────────────────────────

    [Theory]
    [InlineData("evil;subcategory")]
    [InlineData("evil&subcategory")]
    [InlineData("evil|subcategory")]
    public async Task Constructor_InvalidSubcategory_WithBadChars_Throws(string badSubcategory)
    {
        var handler = CreateHandler(badSubcategory, "Success");

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.TestAsync(CreateContext(), CancellationToken.None));
    }

    [Theory]
    [InlineData("Logon Events")]
    [InlineData("Account Logon")]
    [InlineData("Privilege Use/Others")]
    public void Constructor_ValidSubcategory_DoesNotThrow(string subcategory)
    {
        var act = () => CreateHandler(subcategory, "Success");
        act.Should().NotThrow();
    }

    // ── TestAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestAsync_AuditpolOutputIndicatesCompliant_ReturnsSuccessNotChanged()
    {
        var output = AuditpolLine("Logon Events", "Success");
        var handler = CreateHandler("Logon Events", "Success", MockRunner(output).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.NewValue.Should().BeNull();
        result.Detail.Should().Contain("Already compliant");
    }

    [Fact]
    public async Task TestAsync_AuditpolOutputIndicatesNonCompliant_ReturnsSuccessWithNewValue()
    {
        var output = AuditpolLine("Logon Events", "No Auditing");
        var handler = CreateHandler("Logon Events", "Success", MockRunner(output).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.NewValue.Should().Be("Success");
        result.Detail.Should().Contain("Non-compliant");
    }

    [Fact]
    public async Task TestAsync_EmptyAuditpolOutput_ReturnsFailure()
    {
        var handler = CreateHandler("Logon Events", "Success", MockRunner(string.Empty).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to query audit policy");
    }

    [Fact]
    public async Task TestAsync_SubcategoryNotFoundInOutput_ReturnsCurrentSettingAsRawOutput()
    {
        // Output doesn't mention "Logon Events" — ParseAuditSetting returns null,
        // so currentSetting falls back to output.Trim()
        const string rawOutput = "Some other category    Success";
        var handler = CreateHandler("Logon Events", "No Auditing", MockRunner(rawOutput).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PreviousValue.Should().Be(rawOutput.Trim());
    }

    // ── ApplyAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_DryRun_CallsTestInstead()
    {
        var output = AuditpolLine("Logon Events", "No Auditing");
        var handler = CreateHandler("Logon Events", "Success", MockRunner(output).Object);

        var result = await handler.ApplyAsync(CreateContext(dryRun: true), CancellationToken.None);

        // DryRun delegates to TestAsync — returns non-compliant test result, not a change
        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.NewValue.Should().Be("Success");
    }

    [Fact]
    public async Task ApplyAsync_AlreadyCompliant_DoesNotSetPolicy()
    {
        var output = AuditpolLine("Logon Events", "Success");
        var mock = MockRunner(output);
        var handler = CreateHandler("Logon Events", "Success", mock.Object);

        await handler.ApplyAsync(CreateContext(), CancellationToken.None);

        // Only one RunAsync call for TestAsync (/get), no /set call
        mock.Verify(r => r.RunAsync(
            It.Is<ProcessStartInfo>(p => p.Arguments.Contains("/set")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_NonCompliantSuccessAndFailureSetting_RunsCorrectAuditpolArgs()
    {
        // Round 1 (test): non-compliant; Round 2 (set): empty ok; Round 3 (verify): compliant
        var responses = new Queue<ProcessResult>(new[]
        {
            new ProcessResult { ExitCode = 0, StandardOutput = AuditpolLine("Logon Events", "No Auditing") },
            new ProcessResult { ExitCode = 0, StandardOutput = "" },
            new ProcessResult { ExitCode = 0, StandardOutput = AuditpolLine("Logon Events", "Success and Failure") }
        });
        var capturedArgs = new List<string>();
        var mock = new Mock<IProcessRunner>();
        mock.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => capturedArgs.Add(psi.Arguments ?? ""))
            .Returns(() => Task.FromResult(responses.Dequeue()));

        var handler = CreateHandler("Logon Events", "Success and Failure", mock.Object);
        var result = await handler.ApplyAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeTrue();
        capturedArgs.Should().Contain(a =>
            a.Contains("/set") &&
            a.Contains("/success:enable") &&
            a.Contains("/failure:enable"));
    }

    [Fact]
    public async Task ApplyAsync_UnsupportedSetting_ReturnsFailure()
    {
        var output = AuditpolLine("Logon Events", "No Auditing");
        var handler = CreateHandler("Logon Events", "WeirdUnknownSetting", MockRunner(output).Object);

        var result = await handler.ApplyAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    // ── NormalizeSetting (via TestAsync compliance check) ────────────────────

    [Fact]
    public async Task NormalizeSetting_StripsSpacesAndLowercases()
    {
        // expectedSetting "  Success and Failure  " should match output "Success and Failure"
        var output = AuditpolLine("Logon Events", "Success and Failure");
        var handler = CreateHandler("Logon Events", "  Success and Failure  ", MockRunner(output).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.NewValue.Should().BeNull("'  Success and Failure  ' should normalize to same as 'Success and Failure'");
        result.Detail.Should().Contain("Already compliant");
    }

    // ── ParseAuditSetting (via TestAsync) ─────────────────────────────────────

    [Fact]
    public async Task ParseAuditSetting_SuccessAndFailureLine_ParsesCorrectly()
    {
        var output = AuditpolLine("Logon Events", "Success and Failure");
        var handler = CreateHandler("Logon Events", "No Auditing", MockRunner(output).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.PreviousValue.Should().Be("Success and Failure");
    }

    [Fact]
    public async Task ParseAuditSetting_NoAuditingLine_ParsesCorrectly()
    {
        var output = AuditpolLine("Logon Events", "No Auditing");
        var handler = CreateHandler("Logon Events", "Success", MockRunner(output).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.PreviousValue.Should().Be("No Auditing");
    }

    [Fact]
    public async Task ParseAuditSetting_LineMissingSubcategory_ReturnsNull_FallsBackToRawOutput()
    {
        // Output has no line matching the subcategory — fallback is raw output.Trim()
        const string rawOutput = "Object Access    Success";
        var handler = CreateHandler("Logon Events", "Success", MockRunner(rawOutput).Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        // ParseAuditSetting returns null; currentSetting = output.Trim()
        result.PreviousValue.Should().Be(rawOutput.Trim());
    }
}

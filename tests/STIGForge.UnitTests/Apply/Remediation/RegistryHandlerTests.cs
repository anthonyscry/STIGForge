using FluentAssertions;
using STIGForge.Apply.Remediation.Handlers;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.Remediation;

public sealed class RegistryHandlerTests
{
    [Fact]
    public async Task Test_ReturnsResult_WithSimulationMessage()
    {
        var handler = CreateHandler();
        var context = CreateContext(dryRun: false);

        var result = await handler.TestAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.PreviousValue.Should().Contain("simulation");
        result.Detail.Should().Contain("Non-compliant");
    }

    [Fact]
    public async Task Apply_DryRun_CallsTestInstead()
    {
        var handler = CreateHandler();
        var context = CreateContext(dryRun: true);

        var result = await handler.ApplyAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.PreviousValue.Should().Contain("simulation");
        result.NewValue.Should().Be("1");
    }

    [Fact]
    public void RuleId_MatchesConstructorArg()
    {
        var handler = CreateHandler();

        handler.RuleId.Should().Be("SV-200001");
    }

    [Fact]
    public void Category_IsRegistry()
    {
        var handler = CreateHandler();

        handler.Category.Should().Be("Registry");
    }

    private static IRemediationHandler CreateHandler()
    {
        return new RegistryRemediationHandler(
            "SV-200001",
            @"HKLM:\SOFTWARE\Policies\Example",
            "Enabled",
            "1",
            "DWord",
            "Enable policy",
            processRunner: null);
    }

    private static RemediationContext CreateContext(bool dryRun)
    {
        return new RemediationContext
        {
            BundleRoot = "C:\\bundle",
            Mode = HardeningMode.Safe,
            DryRun = dryRun,
            Control = new ControlRecord
            {
                ControlId = "CTRL-1",
                SourcePackId = "pack",
                ExternalIds = new ExternalIds { RuleId = "SV-200001" },
                Title = "Registry control"
            }
        };
    }
}

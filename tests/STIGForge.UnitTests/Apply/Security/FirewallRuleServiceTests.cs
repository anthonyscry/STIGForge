using FluentAssertions;
using STIGForge.Apply.Security;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.Security;

public sealed class FirewallRuleServiceTests
{
    [Fact]
    public async Task GetStatus_NullProcessRunner_ReturnsUnknown()
    {
        var service = new FirewallRuleService();

        var result = await service.GetStatusAsync(CancellationToken.None);

        result.FeatureName.Should().Be("Firewall");
        result.IsEnabled.Should().BeFalse();
        result.CurrentState.Should().Be("Unknown (no process runner)");
    }

    [Fact]
    public async Task Test_ReturnsCurrentState()
    {
        var service = new FirewallRuleService();
        var request = new SecurityFeatureRequest
        {
            Mode = HardeningMode.Full,
            DryRun = true
        };

        var result = await service.TestAsync(request, CancellationToken.None);

        result.FeatureName.Should().Be("Firewall");
        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.PreviousState.Should().Be("Unknown (no process runner)");
        result.NewState.Should().Be("Enabled");
    }

    [Fact]
    public async Task Apply_DryRun_CallsTestInstead()
    {
        var service = new FirewallRuleService();
        var request = new SecurityFeatureRequest
        {
            Mode = HardeningMode.Safe,
            DryRun = true
        };

        var applyResult = await service.ApplyAsync(request, CancellationToken.None);
        var testResult = await service.TestAsync(request, CancellationToken.None);

        applyResult.FeatureName.Should().Be(testResult.FeatureName);
        applyResult.Success.Should().Be(testResult.Success);
        applyResult.Changed.Should().Be(testResult.Changed);
        applyResult.PreviousState.Should().Be(testResult.PreviousState);
        applyResult.NewState.Should().Be(testResult.NewState);
    }

    [Fact]
    public void FeatureName_IsFirewall()
    {
        var service = new FirewallRuleService();

        service.FeatureName.Should().Be("Firewall");
    }
}

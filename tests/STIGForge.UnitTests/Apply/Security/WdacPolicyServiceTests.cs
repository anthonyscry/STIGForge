using FluentAssertions;
using STIGForge.Apply.Security;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.Security;

public sealed class WdacPolicyServiceTests
{
    [Fact]
    public async Task GetStatus_NullProcessRunner_ReturnsUnknown()
    {
        var service = new WdacPolicyService();

        var result = await service.GetStatusAsync(CancellationToken.None);

        result.FeatureName.Should().Be("WDAC");
        result.IsEnabled.Should().BeFalse();
        result.CurrentState.Should().Be("Unknown (no process runner)");
    }

    [Fact]
    public async Task Test_ReturnsCurrentState()
    {
        var service = new WdacPolicyService();
        var request = new SecurityFeatureRequest
        {
            Mode = HardeningMode.Safe,
            DryRun = true
        };

        var result = await service.TestAsync(request, CancellationToken.None);

        result.FeatureName.Should().Be("WDAC");
        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.PreviousState.Should().Be("Unknown (no process runner)");
        result.NewState.Should().Be("Audit");
    }

    [Fact]
    public async Task Apply_DryRun_CallsTestInstead()
    {
        var service = new WdacPolicyService();
        var request = new SecurityFeatureRequest
        {
            Mode = HardeningMode.Full,
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
    public void FeatureName_IsWDAC()
    {
        var service = new WdacPolicyService();

        service.FeatureName.Should().Be("WDAC");
    }
}

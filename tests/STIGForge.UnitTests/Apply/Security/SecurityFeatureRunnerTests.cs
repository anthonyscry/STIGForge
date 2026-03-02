using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using STIGForge.Apply.Security;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.Security;

public sealed class SecurityFeatureRunnerTests
{
    [Fact]
    public async Task RunAll_ExecutesAllRegisteredServices()
    {
        var serviceA = CreateServiceMock("WDAC", applyResult: SuccessResult("WDAC"));
        var serviceB = CreateServiceMock("BitLocker", applyResult: SuccessResult("BitLocker"));
        var serviceC = CreateServiceMock("Firewall", applyResult: SuccessResult("Firewall"));
        var logger = new Mock<ILogger<SecurityFeatureRunner>>();

        var runner = new SecurityFeatureRunner(new[] { serviceA.Object, serviceB.Object, serviceC.Object }, logger.Object);

        var result = await runner.RunAllAsync(new SecurityFeatureRequest { DryRun = false }, CancellationToken.None);

        result.TotalFeatures.Should().Be(3);
        result.Results.Should().HaveCount(3);
        serviceA.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        serviceB.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        serviceC.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAll_ServiceFails_ReportsErrorContinuesOthers()
    {
        var failing = new Mock<ISecurityFeatureService>();
        failing.SetupGet(x => x.FeatureName).Returns("WDAC");
        failing
            .Setup(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var succeedingA = CreateServiceMock("BitLocker", applyResult: SuccessResult("BitLocker"));
        var succeedingB = CreateServiceMock("Firewall", applyResult: SuccessResult("Firewall"));

        var runner = new SecurityFeatureRunner(new[] { failing.Object, succeedingA.Object, succeedingB.Object });

        var result = await runner.RunAllAsync(new SecurityFeatureRequest { DryRun = false }, CancellationToken.None);

        result.TotalFeatures.Should().Be(3);
        result.FailedCount.Should().Be(1);
        result.SuccessCount.Should().Be(2);
        result.Results.Should().ContainSingle(r => r.FeatureName == "WDAC" && !r.Success && r.ErrorMessage == "boom");
        succeedingA.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        succeedingB.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAll_DryRun_CallsTestNotApply()
    {
        var serviceA = CreateServiceMock("WDAC", testResult: SuccessResult("WDAC"));
        var serviceB = CreateServiceMock("BitLocker", testResult: SuccessResult("BitLocker"));

        var runner = new SecurityFeatureRunner(new[] { serviceA.Object, serviceB.Object });

        var result = await runner.RunAllAsync(new SecurityFeatureRequest { DryRun = true }, CancellationToken.None);

        result.TotalFeatures.Should().Be(2);
        serviceA.Verify(x => x.TestAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        serviceA.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        serviceB.Verify(x => x.TestAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        serviceB.Verify(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAll_NoServices_ReturnsEmptyResult()
    {
        var runner = new SecurityFeatureRunner(Array.Empty<ISecurityFeatureService>());

        var result = await runner.RunAllAsync(new SecurityFeatureRequest { DryRun = false }, CancellationToken.None);

        result.TotalFeatures.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAll_CountsCorrectly()
    {
        var successA = CreateServiceMock("WDAC", applyResult: SuccessResult("WDAC", changed: true));
        var successB = CreateServiceMock("BitLocker", applyResult: SuccessResult("BitLocker", changed: false));
        var fail = CreateServiceMock("Firewall", applyResult: FailResult("Firewall"));

        var runner = new SecurityFeatureRunner(new[] { successA.Object, successB.Object, fail.Object });

        var result = await runner.RunAllAsync(new SecurityFeatureRequest { DryRun = false }, CancellationToken.None);

        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(1);
        result.ChangedCount.Should().Be(1);
    }

    private static Mock<ISecurityFeatureService> CreateServiceMock(
        string featureName,
        SecurityFeatureResult? applyResult = null,
        SecurityFeatureResult? testResult = null)
    {
        var mock = new Mock<ISecurityFeatureService>();
        mock.SetupGet(x => x.FeatureName).Returns(featureName);
        mock.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SecurityFeatureStatus
        {
            FeatureName = featureName,
            IsEnabled = true,
            CurrentState = "Enabled",
            CheckedAt = DateTimeOffset.UtcNow
        });

        if (applyResult != null)
            mock.Setup(x => x.ApplyAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(applyResult);

        if (testResult != null)
            mock.Setup(x => x.TestAsync(It.IsAny<SecurityFeatureRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(testResult);

        return mock;
    }

    private static SecurityFeatureResult SuccessResult(string featureName, bool changed = false)
    {
        return new SecurityFeatureResult
        {
            FeatureName = featureName,
            Success = true,
            Changed = changed,
            PreviousState = "Disabled",
            NewState = "Enabled"
        };
    }

    private static SecurityFeatureResult FailResult(string featureName)
    {
        return new SecurityFeatureResult
        {
            FeatureName = featureName,
            Success = false,
            Changed = false,
            ErrorMessage = "failure"
        };
    }
}

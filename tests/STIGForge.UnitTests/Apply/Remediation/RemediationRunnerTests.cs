using FluentAssertions;
using Moq;
using STIGForge.Apply.Remediation;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.Remediation;

public sealed class RemediationRunnerTests
{
    [Fact]
    public async Task RunAsync_NoHandlers_ReturnsEmptyResult()
    {
        var runner = new RemediationRunner(Array.Empty<IRemediationHandler>());
        var controls = new[] { CreateControl("SV-000001") };
        var context = new RemediationContext { BundleRoot = "C:\\bundle", Mode = HardeningMode.Safe, DryRun = false };

        var result = await runner.RunAsync(controls, context, CancellationToken.None);

        result.TotalHandled.Should().Be(0);
        result.Results.Should().BeEmpty();
        result.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WithHandler_ExecutesAndReturnsResult()
    {
        var handler = new Mock<IRemediationHandler>();
        handler.SetupGet(h => h.RuleId).Returns("SV-000002");
        handler.SetupGet(h => h.Category).Returns("Registry");
        handler.Setup(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationResult
            {
                RuleId = "SV-000002",
                HandlerCategory = "Registry",
                Success = true,
                Changed = true,
                Detail = "Applied"
            });

        var runner = new RemediationRunner(new[] { handler.Object });
        var controls = new[] { CreateControl("SV-000002") };
        var context = new RemediationContext { BundleRoot = "C:\\bundle", Mode = HardeningMode.Full, DryRun = false };

        var result = await runner.RunAsync(controls, context, CancellationToken.None);

        handler.Verify(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()), Times.Once);
        result.TotalHandled.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.ChangedCount.Should().Be(1);
        result.Results.Should().ContainSingle(r => r.RuleId == "SV-000002" && r.Success);
    }

    [Fact]
    public async Task RunAsync_DryRun_CallsTestNotApply()
    {
        var handler = new Mock<IRemediationHandler>();
        handler.SetupGet(h => h.RuleId).Returns("SV-000003");
        handler.SetupGet(h => h.Category).Returns("Service");
        handler.Setup(h => h.TestAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationResult
            {
                RuleId = "SV-000003",
                HandlerCategory = "Service",
                Success = true,
                Changed = false,
                Detail = "Compliant"
            });

        var runner = new RemediationRunner(new[] { handler.Object });
        var controls = new[] { CreateControl("SV-000003") };
        var context = new RemediationContext { BundleRoot = "C:\\bundle", Mode = HardeningMode.Safe, DryRun = true };

        var result = await runner.RunAsync(controls, context, CancellationToken.None);

        handler.Verify(h => h.TestAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()), Times.Once);
        handler.Verify(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Results.Should().ContainSingle();
        result.Results[0].Detail.Should().StartWith("[DRY-RUN] ");
    }

    [Fact]
    public async Task RunAsync_HandlerFails_ReportsErrorContinuesOthers()
    {
        var badHandler = new Mock<IRemediationHandler>();
        badHandler.SetupGet(h => h.RuleId).Returns("SV-000004");
        badHandler.SetupGet(h => h.Category).Returns("AuditPolicy");
        badHandler.Setup(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var goodHandler = new Mock<IRemediationHandler>();
        goodHandler.SetupGet(h => h.RuleId).Returns("SV-000005");
        goodHandler.SetupGet(h => h.Category).Returns("Registry");
        goodHandler.Setup(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationResult
            {
                RuleId = "SV-000005",
                HandlerCategory = "Registry",
                Success = true,
                Changed = false
            });

        var runner = new RemediationRunner(new[] { badHandler.Object, goodHandler.Object });
        var controls = new[] { CreateControl("SV-000004"), CreateControl("SV-000005") };
        var context = new RemediationContext { BundleRoot = "C:\\bundle", Mode = HardeningMode.Safe, DryRun = false };

        var result = await runner.RunAsync(controls, context, CancellationToken.None);

        result.TotalHandled.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Results.Should().HaveCount(2);
        result.Results.Should().Contain(r => r.RuleId == "SV-000004" && !r.Success && r.ErrorMessage == "boom");
        result.Results.Should().Contain(r => r.RuleId == "SV-000005" && r.Success);
    }

    [Fact]
    public async Task RunAsync_ControlWithNoHandler_SkipsAndCounts()
    {
        var handler = new Mock<IRemediationHandler>();
        handler.SetupGet(h => h.RuleId).Returns("SV-000006");
        handler.SetupGet(h => h.Category).Returns("Registry");
        handler.Setup(h => h.ApplyAsync(It.IsAny<RemediationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationResult
            {
                RuleId = "SV-000006",
                HandlerCategory = "Registry",
                Success = true,
                Changed = false
            });

        var runner = new RemediationRunner(new[] { handler.Object });
        var controls = new[] { CreateControl("SV-000006"), CreateControl("SV-404040") };
        var context = new RemediationContext { BundleRoot = "C:\\bundle", Mode = HardeningMode.Safe, DryRun = false };

        var result = await runner.RunAsync(controls, context, CancellationToken.None);

        result.TotalHandled.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.Results.Should().ContainSingle(r => r.RuleId == "SV-000006");
    }

    [Fact]
    public void GetSupportedRuleIds_ReturnsRegisteredHandlers()
    {
        var handlerA = new Mock<IRemediationHandler>();
        handlerA.SetupGet(h => h.RuleId).Returns("SV-100001");
        var handlerB = new Mock<IRemediationHandler>();
        handlerB.SetupGet(h => h.RuleId).Returns("SV-100002");

        var runner = new RemediationRunner(new[] { handlerA.Object, handlerB.Object });

        var supported = runner.GetSupportedRuleIds();

        supported.Should().Contain("SV-100001");
        supported.Should().Contain("SV-100002");
    }

    [Fact]
    public void HasHandler_KnownRule_ReturnsTrue()
    {
        var handler = new Mock<IRemediationHandler>();
        handler.SetupGet(h => h.RuleId).Returns("SV-100003");

        var runner = new RemediationRunner(new[] { handler.Object });

        runner.HasHandler("SV-100003").Should().BeTrue();
    }

    [Fact]
    public void HasHandler_UnknownRule_ReturnsFalse()
    {
        var handler = new Mock<IRemediationHandler>();
        handler.SetupGet(h => h.RuleId).Returns("SV-100004");

        var runner = new RemediationRunner(new[] { handler.Object });

        runner.HasHandler("SV-404040").Should().BeFalse();
    }

    private static ControlRecord CreateControl(string ruleId)
    {
        return new ControlRecord
        {
            ControlId = Guid.NewGuid().ToString("N"),
            SourcePackId = "pack",
            ExternalIds = new ExternalIds { RuleId = ruleId },
            Title = "control"
        };
    }
}

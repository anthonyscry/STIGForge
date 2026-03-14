using FluentAssertions;
using STIGForge.Apply.Remediation;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Apply.Remediation;

public sealed class RemediationHandlerRegistryTests
{
    [Fact]
    public void CreateHandlers_WithNullProcessRunner_ReturnsTenHandlers()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().HaveCount(10);
    }

    [Fact]
    public void CreateHandlers_AllImplementIRemediationHandler()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().AllBeAssignableTo<IRemediationHandler>();
    }

    [Fact]
    public void CreateHandlers_AllHaveNonEmptyRuleId()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().AllSatisfy(h => h.RuleId.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void CreateHandlers_RuleIdsAreUnique()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        var ruleIds = handlers.Select(h => h.RuleId).ToList();
        ruleIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreateHandlers_ContainsRegistryHandlers()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().Contain(h => h.Category == "Registry");
    }

    [Fact]
    public void CreateHandlers_ContainsServiceHandlers()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().Contain(h => h.Category == "Service");
    }

    [Fact]
    public void CreateHandlers_ContainsAuditPolicyHandlers()
    {
        var handlers = RemediationHandlerRegistry.CreateHandlers(null);

        handlers.Should().Contain(h => h.Category == "AuditPolicy");
    }
}

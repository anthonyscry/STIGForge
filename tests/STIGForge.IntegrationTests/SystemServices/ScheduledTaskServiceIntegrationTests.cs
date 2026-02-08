using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.IntegrationTests.SystemServices;

public class ScheduledTaskServiceIntegrationTests
{
    [Fact]
    public void Register_MissingTaskName_Throws()
    {
        var svc = new ScheduledTaskService();

        var act = () => svc.Register(new ScheduledTaskRequest
        {
            TaskName = string.Empty,
            BundleRoot = @"C:\fake\bundle"
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_MissingBundleRoot_Throws()
    {
        var svc = new ScheduledTaskService();

        var act = () => svc.Register(new ScheduledTaskRequest
        {
            TaskName = "TestTask",
            BundleRoot = string.Empty
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unregister_ReturnsResult()
    {
        var svc = new ScheduledTaskService();

        // Unregistering a non-existent task should not throw — it returns a result
        var result = svc.Unregister("NonExistentTask_" + Guid.NewGuid().ToString("N")[..8]);

        result.Should().NotBeNull();
        result.TaskName.Should().NotBeNullOrWhiteSpace();
    }
}

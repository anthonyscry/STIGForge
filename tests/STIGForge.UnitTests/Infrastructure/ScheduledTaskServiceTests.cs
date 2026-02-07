using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ScheduledTaskServiceTests
{
  [Fact]
  public void Register_ThrowsForEmptyTaskName()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "", BundleRoot = "C:\\bundle" });
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void Register_ThrowsForEmptyBundleRoot()
  {
    var svc = new ScheduledTaskService();
    var act = () => svc.Register(new ScheduledTaskRequest { TaskName = "test", BundleRoot = "" });
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void Register_ReturnsResultWithTaskName()
  {
    // This will fail on CI (no admin rights), but validates the code path
    var svc = new ScheduledTaskService();
    var result = svc.Register(new ScheduledTaskRequest
    {
      TaskName = "STIGForge_UnitTest_" + Guid.NewGuid().ToString("n").Substring(0, 8),
      BundleRoot = "C:\\test\\bundle",
      Frequency = "ONCE",
      StartTime = "23:59",
      CliPath = "echo"  // Use echo to avoid needing actual CLI
    });

    // Task name should always be set regardless of success/failure
    result.TaskName.Should().Contain("STIGForge");
    result.ScriptPath.Should().NotBeNullOrWhiteSpace();

    // Cleanup: try to remove the task if it was created
    if (result.Success)
    {
      var taskName = result.TaskName.Replace("STIGForge\\", "");
      svc.Unregister(taskName);
    }
  }
}

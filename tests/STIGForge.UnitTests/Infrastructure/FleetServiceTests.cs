using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class FleetServiceTests
{
  [Fact]
  public async Task ExecuteAsync_ThrowsForEmptyTargets()
  {
    var svc = new FleetService();
    var act = () => svc.ExecuteAsync(new FleetRequest { Targets = new List<FleetTarget>() }, CancellationToken.None);
    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task ExecuteAsync_ReturnsResultForUnreachableTarget()
  {
    var svc = new FleetService();
    var result = await svc.ExecuteAsync(new FleetRequest
    {
      Targets = new List<FleetTarget> { new() { HostName = "nonexistent-host-" + Guid.NewGuid().ToString("n").Substring(0, 8) } },
      Operation = "apply",
      RemoteCliPath = "echo",
      TimeoutSeconds = 10
    }, CancellationToken.None);

    result.TotalMachines.Should().Be(1);
    result.MachineResults.Should().HaveCount(1);
    result.Operation.Should().Be("apply");
    // Machine should fail because it's unreachable
    result.MachineResults[0].Success.Should().BeFalse();
  }

  [Fact]
  public async Task CheckStatusAsync_ReturnsStatusForUnreachableHost()
  {
    var svc = new FleetService();
    var result = await svc.CheckStatusAsync(
      new List<FleetTarget> { new() { HostName = "nonexistent-host-" + Guid.NewGuid().ToString("n").Substring(0, 8) } },
      CancellationToken.None);

    result.TotalMachines.Should().Be(1);
    result.UnreachableCount.Should().Be(1);
    result.ReachableCount.Should().Be(0);
  }

  [Fact]
  public async Task ExecuteAsync_SetsTimestamps()
  {
    var before = DateTimeOffset.Now;
    var svc = new FleetService();
    var result = await svc.ExecuteAsync(new FleetRequest
    {
      Targets = new List<FleetTarget> { new() { HostName = "localhost" } },
      Operation = "verify",
      RemoteCliPath = "echo",
      TimeoutSeconds = 5
    }, CancellationToken.None);

    result.StartedAt.Should().BeOnOrAfter(before);
    result.FinishedAt.Should().BeOnOrAfter(result.StartedAt);
    result.MachineResults[0].StartedAt.Should().BeOnOrAfter(before);
  }
}

using FluentAssertions;
using STIGForge.Infrastructure.System;

namespace STIGForge.IntegrationTests.SystemServices;

public class FleetServiceIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_EmptyTargets_Throws()
    {
        var svc = new FleetService();

        var act = () => svc.ExecuteAsync(new FleetRequest
        {
            Targets = Array.Empty<FleetTarget>(),
            Operation = "apply"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_UnreachableTarget_ReturnsFailure()
    {
        var svc = new FleetService();

        var result = await svc.ExecuteAsync(new FleetRequest
        {
            Targets = new List<FleetTarget>
            {
                new() { HostName = "nonexistent-host-12345" }
            },
            Operation = "apply",
            TimeoutSeconds = 5
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.TotalMachines.Should().Be(1);
        result.FailureCount.Should().BeGreaterThan(0);
        result.MachineResults.Should().HaveCount(1);
        result.MachineResults[0].Success.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStatusAsync_UnreachableTarget_ReturnsUnreachable()
    {
        var svc = new FleetService();

        var targets = new List<FleetTarget>
        {
            new() { HostName = "nonexistent-host-12345" }
        };

        var result = await svc.CheckStatusAsync(targets, CancellationToken.None);

        result.Should().NotBeNull();
        result.TotalMachines.Should().Be(1);
        result.UnreachableCount.Should().Be(1);
        result.MachineStatuses.Should().HaveCount(1);
        result.MachineStatuses[0].IsReachable.Should().BeFalse();
    }
}

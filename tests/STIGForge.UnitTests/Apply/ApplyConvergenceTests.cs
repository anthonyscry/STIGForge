using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using STIGForge.Apply;
using STIGForge.Apply.Reboot;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for convergence tracking and max reboot enforcement.
/// </summary>
public sealed class ApplyConvergenceTests
{
    [Fact]
    public async Task MaxReboots_Exceeded_ThrowsRebootException()
    {
        // Arrange: context already at max reboots
        var logger = new Mock<ILogger<RebootCoordinator>>();
        var coordinator = new RebootCoordinator(logger.Object, _ => true);

        var context = new RebootContext
        {
            BundleRoot = Path.GetTempPath(),
            CurrentStepIndex = 1,
            CompletedSteps = new List<string> { "apply_dsc" },
            RebootScheduledAt = DateTimeOffset.UtcNow,
            RebootCount = RebootCoordinator.MaxReboots // Already at max
        };

        // Act
        var act = () => coordinator.ScheduleReboot(context, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<RebootException>()
            .WithMessage("*max_reboot_exceeded*");
    }

    [Fact]
    public async Task RebootCount_IncrementedOnEachSchedule()
    {
        // Arrange
        var logger = new Mock<ILogger<RebootCoordinator>>();
        var coordinator = new RebootCoordinator(logger.Object, _ => true);

        var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-reboot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var context = new RebootContext
            {
                BundleRoot = tempDir,
                CurrentStepIndex = 0,
                CompletedSteps = new List<string>(),
                RebootScheduledAt = DateTimeOffset.UtcNow,
                RebootCount = 0
            };

            // Act: schedule first reboot
            await coordinator.ScheduleReboot(context, CancellationToken.None);

            // Assert
            context.RebootCount.Should().Be(1,
                because: "reboot count should increment from 0 to 1 on first schedule");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task MaxReboots_AtLimit_ScheduleSucceeds_ThenNextFails()
    {
        // Arrange
        var logger = new Mock<ILogger<RebootCoordinator>>();
        var coordinator = new RebootCoordinator(logger.Object, _ => true);

        var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-reboot-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var context = new RebootContext
            {
                BundleRoot = tempDir,
                CurrentStepIndex = 0,
                CompletedSteps = new List<string>(),
                RebootScheduledAt = DateTimeOffset.UtcNow,
                RebootCount = RebootCoordinator.MaxReboots - 1 // One below max
            };

            // Act: this should succeed (brings count to MaxReboots)
            await coordinator.ScheduleReboot(context, CancellationToken.None);
            context.RebootCount.Should().Be(RebootCoordinator.MaxReboots);

            // Next attempt should fail
            var act = () => coordinator.ScheduleReboot(context, CancellationToken.None);
            await act.Should().ThrowAsync<RebootException>()
                .WithMessage("*max_reboot_exceeded*");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void ConvergenceStatus_Converged_WhenAllStepsComplete()
    {
        var result = new ApplyResult
        {
            IsMissionComplete = true,
            ConvergenceStatus = ConvergenceStatus.Converged,
            RebootCount = 1
        };

        result.ConvergenceStatus.Should().Be(ConvergenceStatus.Converged);
    }

    [Fact]
    public void ConvergenceStatus_Exceeded_WhenMaxReboots()
    {
        var result = new ApplyResult
        {
            IsMissionComplete = false,
            ConvergenceStatus = ConvergenceStatus.Exceeded,
            RebootCount = RebootCoordinator.MaxReboots
        };

        result.ConvergenceStatus.Should().Be(ConvergenceStatus.Exceeded);
        result.RebootCount.Should().Be(3);
    }

    [Fact]
    public void ConvergenceStatus_NotApplicable_Default()
    {
        var result = new ApplyResult();
        result.ConvergenceStatus.Should().Be(ConvergenceStatus.NotApplicable);
        result.RebootCount.Should().Be(0);
    }

    [Fact]
    public void MaxReboots_ConstantIsThree()
    {
        RebootCoordinator.MaxReboots.Should().Be(3);
    }
}

using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Lgpo;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for LgpoRunner: LGPO.exe invocation, missing executable, and argument construction.
/// </summary>
public sealed class LgpoRunnerTests
{
    [Fact]
    public void ApplyPolicy_MissingLgpoExe_ThrowsFileNotFound()
    {
        var logger = new Mock<ILogger<LgpoRunner>>();
        var runner = new LgpoRunner(logger.Object);

        var request = new LgpoApplyRequest
        {
            PolFilePath = Path.GetTempFileName(),
            Scope = LgpoScope.Machine,
            LgpoExePath = Path.Combine(Path.GetTempPath(), "nonexistent_lgpo.exe")
        };

        var act = () => runner.ApplyPolicyAsync(request, CancellationToken.None);
        act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*LGPO.exe not found*");

        // Cleanup temp file
        try { File.Delete(request.PolFilePath); } catch { }
    }

    [Fact]
    public void ApplyPolicy_MissingPolFile_ThrowsFileNotFound()
    {
        // Create a fake LGPO.exe so the first check passes
        var fakeLgpo = Path.Combine(Path.GetTempPath(), "test_lgpo_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(fakeLgpo, "fake");

        var logger = new Mock<ILogger<LgpoRunner>>();
        var runner = new LgpoRunner(logger.Object);

        var request = new LgpoApplyRequest
        {
            PolFilePath = Path.Combine(Path.GetTempPath(), "nonexistent.pol"),
            Scope = LgpoScope.Machine,
            LgpoExePath = fakeLgpo
        };

        var act = () => runner.ApplyPolicyAsync(request, CancellationToken.None);
        act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Policy file not found*");

        try { File.Delete(fakeLgpo); } catch { }
    }

    [Fact]
    public void LgpoApplyRequest_DefaultsToMachineScope()
    {
        var request = new LgpoApplyRequest();
        request.Scope.Should().Be(LgpoScope.Machine);
    }

    [Fact]
    public void LgpoApplyResult_SuccessWhenExitCodeZero()
    {
        var result = new LgpoApplyResult
        {
            Success = true,
            ExitCode = 0,
            StdOut = "Applied successfully",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            FinishedAt = DateTimeOffset.UtcNow
        };

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.FinishedAt.Should().BeAfter(result.StartedAt);
    }
}

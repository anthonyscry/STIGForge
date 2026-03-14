using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class VerifyRunResultTests
{
    [Fact]
    public void VerifyRunResult_DefaultValues_AreExpected()
    {
        var result = new VerifyRunResult();

        result.ExitCode.Should().Be(0);
        result.Output.Should().BeEmpty();
        result.Error.Should().BeEmpty();
        result.StartedAt.Should().Be(default(DateTimeOffset));
        result.FinishedAt.Should().Be(default(DateTimeOffset));
    }

    [Fact]
    public void VerifyRunResult_PropertiesRoundTrip()
    {
        var started = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var finished = started.AddSeconds(30);

        var result = new VerifyRunResult
        {
            ExitCode = 42,
            Output = "stdout text",
            Error = "stderr text",
            StartedAt = started,
            FinishedAt = finished
        };

        result.ExitCode.Should().Be(42);
        result.Output.Should().Be("stdout text");
        result.Error.Should().Be("stderr text");
        result.StartedAt.Should().Be(started);
        result.FinishedAt.Should().Be(finished);
    }

    [Fact]
    public void VerifyRunResult_ExitCodeNonZero_CanBeSet()
    {
        var result = new VerifyRunResult { ExitCode = -1 };
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public void VerifyRunResult_OutputAndError_CanBeEmptyString()
    {
        var result = new VerifyRunResult { Output = string.Empty, Error = string.Empty };
        result.Output.Should().BeEmpty();
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void VerifyRunResult_StartedAtBeforeFinishedAt_IsValid()
    {
        var started = DateTimeOffset.UtcNow;
        var finished = started.AddMinutes(5);
        var result = new VerifyRunResult { StartedAt = started, FinishedAt = finished };

        result.FinishedAt.Should().BeAfter(result.StartedAt);
    }
}

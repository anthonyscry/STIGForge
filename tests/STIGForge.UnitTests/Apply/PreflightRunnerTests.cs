using System.Text.Json;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using STIGForge.Apply;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for PreflightRunner: script-not-found, JSON parse, and exit code handling.
/// These tests exercise the C# wrapper logic without requiring PowerShell execution.
/// </summary>
public sealed class PreflightRunnerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PreflightRunner _runner;

    public PreflightRunnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-preflight-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var logger = new Mock<ILogger<PreflightRunner>>();
        _runner = new PreflightRunner(logger.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    [Fact]
    public async Task MissingScript_ReturnsNotOkWithDescriptiveIssue()
    {
        // Arrange: empty bundle root — no Preflight.ps1
        var request = new PreflightRequest { BundleRoot = _tempRoot };

        // Act
        var result = await _runner.RunPreflightAsync(request, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.Issues.Should().ContainSingle()
            .Which.Should().Contain("Preflight script not found");
    }

    [Fact]
    public void ParseResult_ValidJson_ReturnsPreflightResult()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            Ok = true,
            Issues = Array.Empty<string>(),
            Timestamp = "2026-01-15T12:00:00+00:00",
            ExitCode = 0
        });

        // Act
        var result = PreflightRunner.ParseResult(json, 0);

        // Assert
        result.Ok.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.ExitCode.Should().Be(0);
        result.Timestamp.Should().Be("2026-01-15T12:00:00+00:00");
    }

    [Fact]
    public void ParseResult_ValidJsonWithIssues_ReturnsIssues()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            Ok = false,
            Issues = new[] { "Admin rights required", "PowerSTIG module not available" },
            Timestamp = "2026-01-15T12:00:00+00:00",
            ExitCode = 1
        });

        // Act
        var result = PreflightRunner.ParseResult(json, 1);

        // Assert
        result.Ok.Should().BeFalse();
        result.Issues.Should().HaveCount(2);
        result.Issues.Should().Contain("Admin rights required");
        result.Issues.Should().Contain("PowerSTIG module not available");
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void ParseResult_NonZeroExitCode_OverridesJsonExitCode()
    {
        // Arrange: JSON says ExitCode=0 but process exited with 1
        var json = JsonSerializer.Serialize(new
        {
            Ok = true,
            Issues = Array.Empty<string>(),
            Timestamp = "2026-01-15T12:00:00+00:00",
            ExitCode = 0
        });

        // Act
        var result = PreflightRunner.ParseResult(json, 1);

        // Assert: process exit code wins
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void ParseResult_InvalidJson_FallsBackToRawOutput()
    {
        // Arrange
        var rawOutput = "ERROR: something went wrong";

        // Act
        var result = PreflightRunner.ParseResult(rawOutput, 1);

        // Assert
        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Issues.Should().ContainSingle()
            .Which.Should().Contain("something went wrong");
    }

    [Fact]
    public void ParseResult_EmptyOutput_ZeroExitCode_ReturnsOk()
    {
        var result = PreflightRunner.ParseResult("", 0);
        result.Ok.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void ParseResult_EmptyOutput_NonZeroExitCode_ReturnsNotOk()
    {
        var result = PreflightRunner.ParseResult("", 1);
        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Issues.Should().ContainSingle()
            .Which.Should().Contain("no output");
    }
}

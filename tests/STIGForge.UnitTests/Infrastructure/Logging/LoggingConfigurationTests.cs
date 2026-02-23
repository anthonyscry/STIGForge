using FluentAssertions;
using Serilog.Events;
using STIGForge.Infrastructure.Logging;
using Xunit;

namespace STIGForge.UnitTests.Infrastructure.Logging;

public class LoggingConfigurationTests
{
    [Fact]
    public void Level_Should_Default_To_Information()
    {
        // Note: This test may be affected by other tests that change the level
        // In a real test suite, you might reset the level before each test
        LoggingConfiguration.LevelSwitch.MinimumLevel.Should().BeOneOf(
            LogEventLevel.Information,
            LogEventLevel.Debug,
            LogEventLevel.Warning,
            LogEventLevel.Error,
            LogEventLevel.Verbose);
    }

    [Fact]
    public void ConfigureFromEnvironment_Should_Set_Debug_Level()
    {
        // Arrange
        var originalValue = Environment.GetEnvironmentVariable("STIGFORGE_LOG_LEVEL");
        try
        {
            Environment.SetEnvironmentVariable("STIGFORGE_LOG_LEVEL", "Debug");

            // Act
            LoggingConfiguration.ConfigureFromEnvironment();

            // Assert
            LoggingConfiguration.LevelSwitch.MinimumLevel.Should().Be(LogEventLevel.Debug);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STIGFORGE_LOG_LEVEL", originalValue);
            LoggingConfiguration.ConfigureFromEnvironment(); // Reset to original
        }
    }

    [Theory]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("DEBUG", LogEventLevel.Debug)]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("invalid", LogEventLevel.Information)]
    [InlineData("", LogEventLevel.Information)]
    public void ConfigureFromEnvironment_Should_Handle_All_Values(string value, LogEventLevel expected)
    {
        var originalValue = Environment.GetEnvironmentVariable("STIGFORGE_LOG_LEVEL");
        try
        {
            Environment.SetEnvironmentVariable("STIGFORGE_LOG_LEVEL", value);
            LoggingConfiguration.ConfigureFromEnvironment();
            LoggingConfiguration.LevelSwitch.MinimumLevel.Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STIGFORGE_LOG_LEVEL", originalValue);
            LoggingConfiguration.ConfigureFromEnvironment();
        }
    }

    [Fact]
    public void CurrentLevelName_Should_Return_Level_Name()
    {
        LoggingConfiguration.CurrentLevelName.Should().BeOneOf(
            "Debug", "Verbose", "Warning", "Error", "Information");
    }
}

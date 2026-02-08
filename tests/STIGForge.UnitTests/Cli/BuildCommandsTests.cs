using System.Reflection;
using FluentAssertions;

namespace STIGForge.UnitTests.Cli;

public sealed class BuildCommandsTests
{
  private static readonly MethodInfo ValidateBreakGlassArguments =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.BuildCommands", throwOnError: true)!
      .GetMethod("ValidateBreakGlassArguments", BindingFlags.NonPublic | BindingFlags.Static)!;

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithoutAck_ReturnsError()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: false, breakGlassReason: "Emergency maintenance", optionName: "--skip-snapshot");

    error.Should().NotBeNull();
    error.Should().Contain("--break-glass-ack");
  }

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithoutReason_ReturnsError()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: true, breakGlassReason: " ", optionName: "--force-auto-apply");

    error.Should().NotBeNull();
    error.Should().Contain("--break-glass-reason");
  }

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithReasonAndAck_Passes()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: true, breakGlassReason: "Quarterly emergency baseline drift", optionName: "--skip-snapshot");

    error.Should().BeNull();
  }

  [Fact]
  public void ValidateBreakGlassArguments_NonHighRisk_DoesNotRequireBreakGlass()
  {
    var error = InvokeValidate(highRiskOptionEnabled: false, breakGlassAck: false, breakGlassReason: null, optionName: "--skip-snapshot");

    error.Should().BeNull();
  }

  private static string? InvokeValidate(bool highRiskOptionEnabled, bool breakGlassAck, string? breakGlassReason, string optionName)
  {
    return (string?)ValidateBreakGlassArguments.Invoke(
      obj: null,
      parameters: new object?[] { highRiskOptionEnabled, breakGlassAck, breakGlassReason, optionName });
  }
}

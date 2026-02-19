using System.Reflection;
using FluentAssertions;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Cli;

public sealed class VerifyCommandsTests
{
  private static readonly MethodInfo ResolveExitCodeMethod =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.VerifyCommands", throwOnError: true)!
      .GetMethod("ResolveExitCode", BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo ResolveOutputRootMethod =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.VerifyCommands", throwOnError: true)!
      .GetMethod("ResolveOutputRoot", BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo BuildNoResultExitMessageMethod =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.VerifyCommands", throwOnError: true)!
      .GetMethod("BuildNoResultExitMessage", BindingFlags.NonPublic | BindingFlags.Static)!;

  [Fact]
  public void ResolveExitCode_WhenNoResults_ReturnsFailureCode()
  {
    var outputRoot = Path.Combine(Path.GetTempPath(), "no-result-exit");
    var workflow = new VerificationWorkflowResult
    {
      ConsolidatedResultCount = 0,
      ToolRuns = new[]
      {
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 0
        }
      }
    };

    var exitCode = (int)ResolveExitCodeMethod.Invoke(
      obj: null,
      parameters: new object?[] { new VerificationToolRunResult(), workflow, "Evaluate-STIG", outputRoot })!;

    exitCode.Should().Be(1);
  }

  [Fact]
  public void ResolveExitCode_WhenResultsExist_ReturnsToolExitCode()
  {
    var workflow = new VerificationWorkflowResult
    {
      ConsolidatedResultCount = 3,
      ToolRuns = new[] { new VerificationToolRunResult() }
    };

    var exitCode = (int)ResolveExitCodeMethod.Invoke(
      obj: null,
      parameters: new object?[] { new VerificationToolRunResult { ExitCode = 7 }, workflow, "SCAP", "C:/temp/verify" })!;

    exitCode.Should().Be(7);
  }

  [Fact]
  public void BuildNoResultExitMessage_UsesRequestedToolAndExecutedTools()
  {
    var outputRoot = Path.Combine(Path.GetTempPath(), "no-result-msg");
    var workflow = new VerificationWorkflowResult
    {
      ConsolidatedResultCount = 0,
      ToolRuns = new[]
      {
        new VerificationToolRunResult
        {
          Tool = "Evaluate-STIG",
          Executed = true,
          ExitCode = 0
        },
        new VerificationToolRunResult
        {
          Tool = "Custom-SCAP",
          Executed = false,
          ExitCode = 0
        }
      }
    };

    var message = (string)BuildNoResultExitMessageMethod.Invoke(
      obj: null,
      parameters: new object?[] { "Evaluate-STIG", workflow, outputRoot })!;

    message.Should().Contain("Evaluate-STIG");
    message.Should().Contain(outputRoot);
    message.Should().Contain("Evaluate-STIG");
    message.Should().NotContain("Custom-SCAP");
  }

  [Fact]
  public void ResolveOutputRoot_UsesRequestedWhenPresent()
  {
    var requestedRoot = Path.Combine(Path.GetTempPath(), "requested-output-root");
    var workflow = new VerificationWorkflowResult { ConsolidatedJsonPath = Path.Combine(Path.GetTempPath(), "fromresult", "consolidated-results.json") };

    var outputRoot = (string)ResolveOutputRootMethod.Invoke(
      obj: null,
      parameters: new object?[] { requestedRoot, workflow })!;

    outputRoot.Should().Be(requestedRoot);
  }

  [Fact]
  public void ResolveOutputRoot_UsesWorkflowResultWhenNoRequestProvided()
  {
    var outputFromResult = Path.Combine(Path.GetTempPath(), "fromresult", "consolidated-results.json");
    var workflow = new VerificationWorkflowResult { ConsolidatedJsonPath = outputFromResult };

    var outputRoot = (string)ResolveOutputRootMethod.Invoke(
      obj: null,
      parameters: new object?[] { null, workflow })!;

    outputRoot.Should().Be(Path.Combine(Path.GetTempPath(), "fromresult"));
  }
}

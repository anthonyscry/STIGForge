using System.Reflection;
using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Cli;

public sealed class LocalWorkflowCommandsTests
{
  [Fact]
  public async Task WorkflowLocal_WhenRootsOmitted_UsesCommandDefaults()
  {
    var commandsType = Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.LocalWorkflowCommands", throwOnError: true)!;
    var registerMethod = commandsType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;

    var service = new Mock<ILocalWorkflowService>();
    LocalWorkflowRequest? capturedRequest = null;
    service
      .Setup(s => s.RunAsync(It.IsAny<LocalWorkflowRequest>(), It.IsAny<CancellationToken>()))
      .Callback<LocalWorkflowRequest, CancellationToken>((request, _) => capturedRequest = request)
      .ReturnsAsync(new LocalWorkflowResult());

    using var host = Host.CreateDefaultBuilder()
      .ConfigureServices(services => services.AddSingleton(service.Object))
      .Build();

    var root = new RootCommand();
    registerMethod.Invoke(null, new object?[] { root, (Func<IHost>)(() => host) });

    var originalOut = Console.Out;
    using var writer = new StringWriter();
    Console.SetOut(writer);

    try
    {
      var exitCode = await root.InvokeAsync("workflow-local");

      exitCode.Should().Be(0);
      capturedRequest.Should().NotBeNull();

      var expectedImportRoot = Path.GetFullPath(Path.Combine(".stigforge", "import"));
      var expectedToolRoot = Path.GetFullPath(Path.Combine(".stigforge", "tools", "Evaluate-STIG", "Evaluate-STIG"));
      var expectedOutputRoot = Path.GetFullPath(Path.Combine(".stigforge", "local-workflow"));
      var expectedMissionPath = Path.Combine(expectedOutputRoot, "mission.json");

      capturedRequest!.ImportRoot.Should().Be(expectedImportRoot);
      capturedRequest.ToolRoot.Should().Be(expectedToolRoot);
      capturedRequest.OutputRoot.Should().Be(expectedOutputRoot);
      writer.ToString().Should().Contain(expectedMissionPath);
      service.Verify(s => s.RunAsync(It.IsAny<LocalWorkflowRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    finally
    {
      Console.SetOut(originalOut);
    }
  }

  [Fact]
  public async Task ExecuteLocalWorkflowAsync_RunsServiceAndPrintsMissionPath()
  {
    var executeMethod = Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.LocalWorkflowCommands", throwOnError: true)!
      .GetMethod("ExecuteLocalWorkflowAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    var service = new Mock<ILocalWorkflowService>();
    LocalWorkflowRequest? capturedRequest = null;
    service
      .Setup(s => s.RunAsync(It.IsAny<LocalWorkflowRequest>(), It.IsAny<CancellationToken>()))
      .Callback<LocalWorkflowRequest, CancellationToken>((request, _) => capturedRequest = request)
      .ReturnsAsync(new LocalWorkflowResult());

    var host = Host.CreateDefaultBuilder()
      .ConfigureServices(services => services.AddSingleton(service.Object))
      .Build();

    var originalOut = Console.Out;
    using var writer = new StringWriter();
    Console.SetOut(writer);

    var outputRoot = Path.Combine(Path.GetTempPath(), "stigforge-local-workflow-cli-test", Guid.NewGuid().ToString("N"));
    var importRoot = Path.Combine(outputRoot, "import");
    var toolRoot = Path.Combine(outputRoot, "tools");

    try
    {
      var task = (Task<string>)executeMethod.Invoke(
        null,
        new object?[]
        {
          (Func<IHost>)(() => host),
          importRoot,
          toolRoot,
          outputRoot,
          CancellationToken.None
        })!;

      var missionPath = await task;
      var printed = writer.ToString();

      capturedRequest.Should().NotBeNull();
      capturedRequest!.ImportRoot.Should().Be(Path.GetFullPath(importRoot));
      capturedRequest.ToolRoot.Should().Be(Path.GetFullPath(toolRoot));
      capturedRequest.OutputRoot.Should().Be(Path.GetFullPath(outputRoot));
      missionPath.Should().Be(Path.Combine(Path.GetFullPath(outputRoot), "mission.json"));
      printed.Should().Contain(missionPath);
      service.Verify(s => s.RunAsync(It.IsAny<LocalWorkflowRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    finally
    {
      Console.SetOut(originalOut);
      await host.StopAsync();
      host.Dispose();
    }
  }
}

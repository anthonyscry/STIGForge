using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STIGForge.Core.Abstractions;

namespace STIGForge.Cli.Commands;

internal static class LocalWorkflowCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("workflow-local", "Run local setup-import-scan workflow and emit mission.json");
    var importRootOpt = new Option<string>("--import-root", () => Path.GetFullPath(".\\.stigforge\\import"), "Import root containing local content");
    var toolRootOpt = new Option<string>("--tool-root", () => Path.GetFullPath(".\\.stigforge\\tools\\Evaluate-STIG\\Evaluate-STIG"), "Root folder containing Evaluate-STIG.ps1");
    var outputRootOpt = new Option<string>("--output-root", () => Path.GetFullPath(".\\.stigforge\\local-workflow"), "Output root for mission artifacts");

    cmd.AddOption(importRootOpt);
    cmd.AddOption(toolRootOpt);
    cmd.AddOption(outputRootOpt);

    cmd.SetHandler(async (importRoot, toolRoot, outputRoot) =>
    {
      await ExecuteLocalWorkflowAsync(buildHost, importRoot, toolRoot, outputRoot, CancellationToken.None);
    }, importRootOpt, toolRootOpt, outputRootOpt);

    rootCmd.AddCommand(cmd);
  }

  private static async Task<string> ExecuteLocalWorkflowAsync(
    Func<IHost> buildHost,
    string importRoot,
    string toolRoot,
    string outputRoot,
    CancellationToken ct)
  {
    using var host = buildHost();
    await host.StartAsync(ct);
    try
    {
      var request = new LocalWorkflowRequest
      {
        ImportRoot = Helpers.ResolveAbsolutePath(importRoot),
        ToolRoot = Helpers.ResolveAbsolutePath(toolRoot),
        OutputRoot = Helpers.ResolveAbsolutePath(outputRoot)
      };

      var service = host.Services.GetRequiredService<ILocalWorkflowService>();
      await service.RunAsync(request, ct);

      var missionPath = Helpers.ResolveMissionPath(request.OutputRoot);
      Console.WriteLine("Wrote mission: " + missionPath);
      return missionPath;
    }
    finally
    {
      await host.StopAsync(ct);
    }
  }
}

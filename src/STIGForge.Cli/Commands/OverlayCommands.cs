using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class OverlayCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var overlayCmd = new Command("overlay", "Manage and inspect overlays");

    RegisterOverlayList(overlayCmd, buildHost);
    RegisterOverlayDiff(overlayCmd, buildHost);

    rootCmd.AddCommand(overlayCmd);
  }

  private static void RegisterOverlayList(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("list", "List all overlays");

    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();

      var overlays = host.Services.GetRequiredService<IOverlayRepository>();
      var list = await overlays.ListAsync(CancellationToken.None);

      if (list.Count == 0)
      {
        Console.WriteLine("No overlays found.");
        await host.StopAsync();
        return;
      }

      Console.WriteLine($"{"OverlayId",-36} {"Name",-30} {"Overrides",-12} {"Updated",-24}");
      Console.WriteLine(new string('-', 102));

      foreach (var o in list)
      {
        Console.WriteLine($"{o.OverlayId,-36} {o.Name,-30} {o.Overrides.Count,-12} {o.UpdatedAt:yyyy-MM-dd HH:mm}");
      }

      Console.WriteLine($"\n{list.Count} overlay(s) found.");
      await host.StopAsync();
    });

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterOverlayDiff(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("diff", "Diff two overlays to show field-level conflicts");
    var overlayAArg = new Argument<string>("overlay-a", "First overlay ID (lower precedence)");
    var overlayBArg = new Argument<string>("overlay-b", "Second overlay ID (higher precedence)");
    cmd.AddArgument(overlayAArg);
    cmd.AddArgument(overlayBArg);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var idA = ctx.ParseResult.GetValueForArgument(overlayAArg);
      var idB = ctx.ParseResult.GetValueForArgument(overlayBArg);

      using var host = buildHost();
      await host.StartAsync();

      var repo = host.Services.GetRequiredService<IOverlayRepository>();
      var overlayA = await repo.GetAsync(idA, CancellationToken.None);
      if (overlayA == null)
        throw new ArgumentException($"Overlay not found: {idA}");

      var overlayB = await repo.GetAsync(idB, CancellationToken.None);
      if (overlayB == null)
        throw new ArgumentException($"Overlay not found: {idB}");

      var detector = new OverlayConflictDetector();
      var report = detector.DetectConflicts(new List<Overlay> { overlayA, overlayB });

      if (report.Conflicts.Count == 0)
      {
        Console.WriteLine($"No conflicts found between overlays {idA} and {idB}.");
        await host.StopAsync();
        return;
      }

      Console.WriteLine($"{"ControlKey",-20} {"Winner",-20} {"Overridden",-20} {"Winning Value",-35} {"Overridden Value",-35} {"Blocking",-8}");
      Console.WriteLine(new string('-', 138));

      foreach (var c in report.Conflicts)
      {
        Console.WriteLine($"{c.ControlKey,-20} {c.WinningOverlayId,-20} {c.OverriddenOverlayId,-20} {c.WinningValue,-35} {c.OverriddenValue,-35} {(c.IsBlockingConflict ? "YES" : "no"),-8}");
      }

      Console.WriteLine($"\n{report.Conflicts.Count} conflict(s) found.");

      if (report.HasBlockingConflicts)
      {
        Console.Error.WriteLine($"\nWARNING: {report.BlockingConflictCount} blocking conflict(s) would halt build.");
      }

      await host.StopAsync();
    });

    parentCmd.AddCommand(cmd);
  }
}

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal class GpoConflictCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("gpo-conflicts", "Detect GPO conflicts with local STIG settings");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<GpoConflictCommands>>();
      var detector = host.Services.GetRequiredService<GpoConflictDetector>();

      try
      {
        var conflicts = await detector.DetectConflictsAsync(bundle, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(conflicts, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"GPO Conflict Detection: {bundle}");
          Console.WriteLine($"  Conflicts Found: {conflicts.Count}\n");

          if (conflicts.Count == 0)
          {
            Console.WriteLine("  No conflicts detected. Local STIG settings align with applied GPOs.");
          }
          else
          {
            foreach (var conflict in conflicts)
            {
              Console.WriteLine($"  Setting: {conflict.SettingPath}");
              Console.WriteLine($"    Conflict Type: {conflict.ConflictType}");
              Console.WriteLine($"    Local STIG Value: {conflict.LocalValue}");
              Console.WriteLine($"    GPO Value: {conflict.GpoValue}");
              Console.WriteLine($"    Applied By GPO: {conflict.GpoName}");
              Console.WriteLine();
            }

            Console.WriteLine("  WARNING: Domain GPOs will override local STIG settings.");
            Console.WriteLine("  Consider applying STIGs via GPO instead of local hardening.");
          }
        }

        ctx.ExitCode = conflicts.Count > 0 ? 1 : 0;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "GPO conflict detection failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

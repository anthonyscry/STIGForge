using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class ReleaseCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterCheckRelease(rootCmd, buildHost);
    RegisterReleaseNotes(rootCmd, buildHost);
  }

  private static void RegisterCheckRelease(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("check-release", "Check for new STIG releases");
    var packIdOpt = new Option<string>("--pack-id", "Current pack ID to check against") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(packIdOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var packId = ctx.ParseResult.GetValueForOption(packIdOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReleaseCommands");
      var service = host.Services.GetRequiredService<StigReleaseMonitorService>();

      logger.LogInformation("check-release started: packId={PackId}", packId);
      var check = await service.CheckForNewReleasesAsync(packId, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(check, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Status: {check.Status}");
        Console.WriteLine($"Baseline: {check.BaselinePackId}");
        if (!string.IsNullOrWhiteSpace(check.TargetPackId))
          Console.WriteLine($"Target: {check.TargetPackId}");
        if (!string.IsNullOrWhiteSpace(check.SummaryJson))
          Console.WriteLine($"Summary: {check.SummaryJson}");
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterReleaseNotes(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("release-notes", "Generate release notes diff");
    var baselineOpt = new Option<string>("--baseline", "Baseline pack ID") { IsRequired = true };
    var targetOpt = new Option<string>("--target", "Target pack ID") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(baselineOpt);
    cmd.AddOption(targetOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var baseline = ctx.ParseResult.GetValueForOption(baselineOpt) ?? string.Empty;
      var target = ctx.ParseResult.GetValueForOption(targetOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReleaseCommands");
      var service = host.Services.GetRequiredService<StigReleaseMonitorService>();

      logger.LogInformation("release-notes started: baseline={Baseline}, target={Target}", baseline, target);
      var notes = await service.GenerateReleaseNotesAsync(baseline, target, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(notes, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Baseline: {notes.BaselinePackId}");
        Console.WriteLine($"Target: {notes.TargetPackId}");
        Console.WriteLine($"Added: {notes.AddedCount}  Removed: {notes.RemovedCount}  Modified: {notes.ModifiedCount}");
        Console.WriteLine($"Severity changes: {notes.SeverityChangedCount}");
        Console.WriteLine($"Highlights: {notes.HighlightedChanges.Count}");
        foreach (var highlight in notes.HighlightedChanges)
          Console.WriteLine("  " + highlight);
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

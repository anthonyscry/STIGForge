using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal class DriftCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterDriftCheck(rootCmd, buildHost);
    RegisterDriftHistory(rootCmd, buildHost);
  }

  private static void RegisterDriftCheck(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("drift-check", "Check for baseline drift in bundle compliance");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var autoRemediateOpt = new Option<bool>("--auto-remediate", () => false, "Auto-remediate drifted controls");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(autoRemediateOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var autoRemediate = ctx.ParseResult.GetValueForOption(autoRemediateOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<DriftCommands>>();
      var driftService = host.Services.GetRequiredService<DriftDetectionService>();

      try
      {
        var result = await driftService.CheckBundleAsync(bundle, autoRemediate, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"Drift Check Complete: {result.BundleRoot}");
          Console.WriteLine($"  Checked At: {result.CheckedAt}");
          Console.WriteLine($"  Baseline Rules: {result.BaselineRuleCount}");
          Console.WriteLine($"  Current Rules: {result.CurrentRuleCount}");
          Console.WriteLine($"  Drift Events: {result.DriftEvents.Count}");

          if (result.DriftEvents.Count > 0)
          {
            Console.WriteLine("\n  Drift Events:");
            foreach (var drift in result.DriftEvents.Take(20))
            {
              Console.WriteLine($"    - {drift.RuleId}: {drift.ChangeType}");
              if (!string.IsNullOrWhiteSpace(drift.PreviousState))
                Console.WriteLine($"      Previous: {drift.PreviousState}");
              Console.WriteLine($"      Current: {drift.CurrentState}");
            }
            if (result.DriftEvents.Count > 20)
              Console.WriteLine($"    ... and {result.DriftEvents.Count - 20} more");
          }

          if (result.AutoRemediatedRuleIds.Count > 0)
          {
            Console.WriteLine($"\n  Auto-Remediated: {result.AutoRemediatedRuleIds.Count}");
            foreach (var ruleId in result.AutoRemediatedRuleIds)
              Console.WriteLine($"    - {ruleId}");
          }

          if (result.RemediationErrors.Count > 0)
          {
            Console.WriteLine($"\n  Remediation Errors: {result.RemediationErrors.Count}");
            foreach (var error in result.RemediationErrors)
              Console.WriteLine($"    - {error}");
          }
        }

        ctx.ExitCode = result.DriftEvents.Count > 0 ? 1 : 0;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Drift check failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterDriftHistory(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("drift-history", "Show drift detection history");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var ruleIdOpt = new Option<string?>("--rule-id", () => null, "Filter to specific rule ID");
    var limitOpt = new Option<int>("--limit", () => 50, "Max events to show");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(ruleIdOpt);
    cmd.AddOption(limitOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var ruleId = ctx.ParseResult.GetValueForOption(ruleIdOpt);
      var limit = ctx.ParseResult.GetValueForOption(limitOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<DriftCommands>>();
      var driftService = host.Services.GetRequiredService<DriftDetectionService>();

      try
      {
        var history = await driftService.GetHistoryAsync(bundle, ruleId, limit, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"Drift History: {bundle}");
          if (!string.IsNullOrWhiteSpace(ruleId))
            Console.WriteLine($"  Filtered to Rule ID: {ruleId}");
          Console.WriteLine($"  Events: {history.Count}\n");

          foreach (var drift in history)
          {
            Console.WriteLine($"  [{drift.DetectedAt:yyyy-MM-dd HH:mm:ss}] {drift.RuleId}");
            Console.WriteLine($"    Change Type: {drift.ChangeType}");
            if (!string.IsNullOrWhiteSpace(drift.PreviousState))
              Console.WriteLine($"    Previous: {drift.PreviousState}");
            Console.WriteLine($"    Current: {drift.CurrentState}");
            Console.WriteLine();
          }
        }

        ctx.ExitCode = 0;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Drift history failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

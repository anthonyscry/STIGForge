using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Remediation;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Cli.Commands;

internal static class RemediationCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterRemediate(rootCmd, buildHost);
    RegisterRemediateList(rootCmd, buildHost);
  }

  private static void RegisterRemediate(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("remediate", "Run per-rule remediation");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var modeOpt = new Option<string>("--mode", () => "Safe", "Hardening mode (AuditOnly/Safe/Full)");
    var dryRunOpt = new Option<bool>("--dry-run", "Test only, no changes");
    var ruleIdOpt = new Option<string[]>("--rule-id", "Filter to specific rule IDs") { AllowMultipleArgumentsPerToken = true };
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(modeOpt);
    cmd.AddOption(dryRunOpt);
    cmd.AddOption(ruleIdOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var modeStr = ctx.ParseResult.GetValueForOption(modeOpt) ?? "Safe";
      var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
      var ruleIds = ctx.ParseResult.GetValueForOption(ruleIdOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RemediationCommands");
      var runner = host.Services.GetRequiredService<RemediationRunner>();
      var controls = host.Services.GetRequiredService<IControlRepository>();

      var manifestPath = Path.Combine(bundle, "manifest.json");
      var mode = HardeningMode.Safe;
      if (!string.IsNullOrWhiteSpace(modeStr) && Enum.TryParse<HardeningMode>(modeStr, true, out var parsedMode))
        mode = parsedMode;

      var manifestJson = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
      using var doc = JsonDocument.Parse(manifestJson);
      var packId = doc.RootElement.GetProperty("Pack").GetProperty("PackId").GetString() ?? string.Empty;
      var controlList = await controls.ListControlsAsync(packId, ct);

      if (ruleIds != null && ruleIds.Length > 0)
      {
        var filterSet = new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase);
        controlList = controlList
          .Where(c => !string.IsNullOrWhiteSpace(c.ExternalIds.RuleId)
            && filterSet.Contains(c.ExternalIds.RuleId))
          .ToList();
      }

      logger.LogInformation("remediate started: bundle={Bundle}, mode={Mode}, dryRun={DryRun}", bundle, mode, dryRun);
      var result = await runner.RunAsync(controlList, new RemediationContext
      {
        BundleRoot = bundle,
        Mode = mode,
        DryRun = dryRun
      }, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Handled={result.TotalHandled} Success={result.SuccessCount} Fail={result.FailedCount} Changed={result.ChangedCount} Skipped={result.SkippedCount}");
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterRemediateList(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("remediate-list", "List supported remediation rules");

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var runner = host.Services.GetRequiredService<RemediationRunner>();
      var ruleIds = runner.GetSupportedRuleIds();
      Console.WriteLine($"Supported remediation rules ({ruleIds.Count}):");
      foreach (var id in ruleIds) Console.WriteLine("  " + id);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

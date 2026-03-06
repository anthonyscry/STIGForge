using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Security;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Cli.Commands;

internal static class SecurityCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterSecurityStatus(rootCmd, buildHost);
    RegisterSecurityApply(rootCmd, buildHost);
  }

  private static void RegisterSecurityStatus(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("security-status", "Check security feature status");
    var jsonOpt = new Option<bool>("--json", "JSON output");
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SecurityCommands");

      var wdac = host.Services.GetRequiredService<WdacPolicyService>();
      var bitlocker = host.Services.GetRequiredService<BitLockerService>();
      var firewall = host.Services.GetRequiredService<FirewallRuleService>();
      var statuses = new List<SecurityFeatureStatus>();
      statuses.Add(await wdac.GetStatusAsync(ct));
      statuses.Add(await bitlocker.GetStatusAsync(ct));
      statuses.Add(await firewall.GetStatusAsync(ct));

      logger.LogInformation("security-status completed");

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(statuses, JsonOptions.Indented));
      }
      else
      {
        foreach (var status in statuses)
          Console.WriteLine($"{status.FeatureName}: Enabled={status.IsEnabled} State={status.CurrentState}");
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterSecurityApply(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("security-apply", "Apply security features");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var modeOpt = new Option<string>("--mode", () => "Safe", "Hardening mode");
    var dryRunOpt = new Option<bool>("--dry-run", "Test only");
    var configOpt = new Option<string>("--config", () => string.Empty, "Config file path");
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(modeOpt);
    cmd.AddOption(dryRunOpt);
    cmd.AddOption(configOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var modeStr = ctx.ParseResult.GetValueForOption(modeOpt) ?? "Safe";
      var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
      var config = ctx.ParseResult.GetValueForOption(configOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SecurityCommands");
      var runner = host.Services.GetRequiredService<SecurityFeatureRunner>();

      var mode = HardeningMode.Safe;
      if (!string.IsNullOrWhiteSpace(modeStr) && Enum.TryParse<HardeningMode>(modeStr, true, out var parsedMode))
        mode = parsedMode;

      logger.LogInformation("security-apply started: bundle={Bundle}, mode={Mode}, dryRun={DryRun}", bundle, mode, dryRun);
      var result = await runner.RunAllAsync(new SecurityFeatureRequest
      {
        BundleRoot = bundle,
        DryRun = dryRun,
        Mode = mode,
        ConfigPath = string.IsNullOrWhiteSpace(config) ? null : config
      }, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Total={result.TotalFeatures} Success={result.SuccessCount} Fail={result.FailedCount} Changed={result.ChangedCount}");
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

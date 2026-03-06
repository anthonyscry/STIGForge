using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class ComplianceCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterComplianceScore(rootCmd, buildHost);
    RegisterComplianceTrend(rootCmd, buildHost);
  }

  private static void RegisterComplianceScore(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("compliance-score", "Record a compliance snapshot");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var passOpt = new Option<int>("--pass", "Pass count") { IsRequired = true };
    var failOpt = new Option<int>("--fail", "Fail count") { IsRequired = true };
    var errorOpt = new Option<int>("--error", () => 0, "Error count");
    var notApplicableOpt = new Option<int>("--not-applicable", () => 0, "Not-applicable count");
    var notReviewedOpt = new Option<int>("--not-reviewed", () => 0, "Not-reviewed count");
    var toolOpt = new Option<string>("--tool", "Tool name (e.g., \"Evaluate-STIG\")") { IsRequired = true };
    var packIdOpt = new Option<string>("--pack-id", () => string.Empty, "Pack ID");
    var runIdOpt = new Option<string>("--run-id", () => string.Empty, "Run ID");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(passOpt);
    cmd.AddOption(failOpt);
    cmd.AddOption(errorOpt);
    cmd.AddOption(notApplicableOpt);
    cmd.AddOption(notReviewedOpt);
    cmd.AddOption(toolOpt);
    cmd.AddOption(packIdOpt);
    cmd.AddOption(runIdOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var pass = ctx.ParseResult.GetValueForOption(passOpt);
      var fail = ctx.ParseResult.GetValueForOption(failOpt);
      var error = ctx.ParseResult.GetValueForOption(errorOpt);
      var notApplicable = ctx.ParseResult.GetValueForOption(notApplicableOpt);
      var notReviewed = ctx.ParseResult.GetValueForOption(notReviewedOpt);
      var tool = ctx.ParseResult.GetValueForOption(toolOpt) ?? string.Empty;
      var packId = ctx.ParseResult.GetValueForOption(packIdOpt) ?? string.Empty;
      var runId = ctx.ParseResult.GetValueForOption(runIdOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ComplianceCommands");
      var service = host.Services.GetRequiredService<ComplianceTrendService>();

      logger.LogInformation("compliance-score started: bundle={Bundle}", bundle);
      await service.RecordSnapshotAsync(pass, fail, error, notApplicable, notReviewed, bundle, runId, packId, tool, ct);
      Console.WriteLine("Compliance snapshot recorded.");
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterComplianceTrend(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("compliance-trend", "Show compliance trend");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var limitOpt = new Option<int>("--limit", () => 10, "Max snapshots");
    var jsonOpt = new Option<bool>("--json", "JSON output");
    var thresholdOpt = new Option<double>("--threshold", () => 0, "Regression threshold percent");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(limitOpt);
    cmd.AddOption(jsonOpt);
    cmd.AddOption(thresholdOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var limit = ctx.ParseResult.GetValueForOption(limitOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);
      var threshold = ctx.ParseResult.GetValueForOption(thresholdOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ComplianceCommands");
      var service = host.Services.GetRequiredService<ComplianceTrendService>();

      logger.LogInformation("compliance-trend started: bundle={Bundle}, limit={Limit}", bundle, limit);
      var trend = await service.GetTrendAsync(bundle, limit, ct);
      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(trend, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Current: {trend.CurrentPercent:F1}%  Previous: {trend.PreviousPercent:F1}%  Delta: {trend.Delta:+0.0;-0.0}%");
        if (trend.IsRegression) Console.Error.WriteLine("REGRESSION DETECTED: " + trend.RegressionSummary);
        Console.WriteLine($"Snapshots: {trend.Snapshots.Count}");
        foreach (var s in trend.Snapshots)
          Console.WriteLine($"  {s.CapturedAt:yyyy-MM-dd HH:mm}  {s.CompliancePercent:F1}%  P={s.PassCount} F={s.FailCount} E={s.ErrorCount}  [{s.Tool}]");
      }
      if (threshold > 0 && trend.Delta < -threshold)
      {
        Console.Error.WriteLine($"Regression exceeds threshold ({threshold}%).");
        Environment.ExitCode = 1;
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Services;
using STIGForge.Export;

namespace STIGForge.Cli.Commands;

internal static class ReportCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterExportReport(rootCmd, buildHost);
    RegisterComplianceDiff(rootCmd, buildHost);
  }

  private static void RegisterExportReport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-report", "Generate a self-contained HTML compliance report from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outputOpt = new Option<string>("--output", () => string.Empty, "Output file path (default: {bundle}/Export/compliance-report.html)");
    var formatOpt = new Option<string>("--format", () => "html", "Output format: html or json");
    var audienceOpt = new Option<string>("--audience", () => "executive", "Report audience: executive, admin, or auditor");
    var systemNameOpt = new Option<string>("--system-name", () => string.Empty, "System name override");
    var trendLimitOpt = new Option<int>("--trend-limit", () => 10, "Maximum trend snapshots to include");

    formatOpt.AddValidator(result =>
    {
      var value = result.GetValueOrDefault<string>() ?? string.Empty;
      if (!string.Equals(value, "html", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
      {
        result.ErrorMessage = "Invalid --format value '" + value + "'. Allowed values: html, json.";
      }
    });

    audienceOpt.AddValidator(result =>
    {
      var value = result.GetValueOrDefault<string>() ?? string.Empty;
      if (!string.Equals(value, "executive", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "admin", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "auditor", StringComparison.OrdinalIgnoreCase))
      {
        result.ErrorMessage = "Invalid --audience value '" + value + "'. Allowed values: executive, admin, auditor.";
      }
    });

    cmd.AddOption(bundleOpt); cmd.AddOption(outputOpt); cmd.AddOption(formatOpt);
    cmd.AddOption(audienceOpt); cmd.AddOption(systemNameOpt); cmd.AddOption(trendLimitOpt);

    cmd.SetHandler(async (InvocationContext context) =>
    {
      var bundle = context.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var output = context.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var format = context.ParseResult.GetValueForOption(formatOpt) ?? "html";
      var audience = context.ParseResult.GetValueForOption(audienceOpt) ?? "executive";
      var systemName = context.ParseResult.GetValueForOption(systemNameOpt) ?? string.Empty;
      var trendLimit = context.ParseResult.GetValueForOption(trendLimitOpt);
      var ct = context.GetCancellationToken();

      using var host = buildHost();
      await host.StartAsync(ct);
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReportCommands");
      logger.LogInformation("export-report started: bundle={Bundle}, format={Format}, audience={Audience}", bundle, format, audience);

      // Get trend snapshots from DB
      var trendService = host.Services.GetRequiredService<ComplianceTrendService>();
      var trend = await trendService.GetTrendAsync(bundle, trendLimit, ct);

      var data = HtmlReportGenerator.BuildReportData(
        bundle,
        trend.Snapshots,
        string.IsNullOrWhiteSpace(systemName) ? null : systemName,
        audience);

      string outputPath;
      if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
      {
        outputPath = string.IsNullOrWhiteSpace(output)
          ? Path.Combine(bundle, "Export", "compliance-report.json")
          : output;
        HtmlReportGenerator.WriteReportJson(data, outputPath);
      }
      else
      {
        outputPath = string.IsNullOrWhiteSpace(output)
          ? Path.Combine(bundle, "Export", "compliance-report.html")
          : output;
        HtmlReportGenerator.WriteReport(data, outputPath);
      }

      Console.WriteLine("Compliance report:");
      Console.WriteLine("  Output: " + outputPath);
      Console.WriteLine($"  Compliance: {data.OverallCompliancePercent:F1}%  ({data.PassCount} pass / {data.FailCount} fail / {data.ErrorCount} error)");
      Console.WriteLine($"  Open findings: {data.OpenFindings.Count}  |  Audience: {data.Audience}");

      logger.LogInformation("export-report completed: {OutputPath}", outputPath);
      await host.StopAsync(ct);
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterComplianceDiff(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("compliance-diff", "Compare two verification runs and show regressions and remediations");
    var baselineOpt = new Option<string>("--baseline", "Path to baseline bundle root or consolidated-results.json") { IsRequired = true };
    var targetOpt = new Option<string>("--target", "Path to target bundle root or consolidated-results.json") { IsRequired = true };
    var formatOpt = new Option<string>("--format", () => "console", "Output format: console, json, or csv");
    var outputOpt = new Option<string>("--output", () => string.Empty, "Output file path (required for json/csv)");
    var regressionsOnlyOpt = new Option<bool>("--regressions-only", () => false, "Show only regressions");

    formatOpt.AddValidator(result =>
    {
      var value = result.GetValueOrDefault<string>() ?? string.Empty;
      if (!string.Equals(value, "console", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "json", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "csv", StringComparison.OrdinalIgnoreCase))
      {
        result.ErrorMessage = "Invalid --format value '" + value + "'. Allowed values: console, json, csv.";
      }
    });

    cmd.AddOption(baselineOpt); cmd.AddOption(targetOpt); cmd.AddOption(formatOpt);
    cmd.AddOption(outputOpt); cmd.AddOption(regressionsOnlyOpt);

    cmd.SetHandler(async (InvocationContext context) =>
    {
      var baseline = context.ParseResult.GetValueForOption(baselineOpt) ?? string.Empty;
      var target = context.ParseResult.GetValueForOption(targetOpt) ?? string.Empty;
      var format = context.ParseResult.GetValueForOption(formatOpt) ?? "console";
      var output = context.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var regressionsOnly = context.ParseResult.GetValueForOption(regressionsOnlyOpt);
      var ct = context.GetCancellationToken();

      using var host = buildHost();
      await host.StartAsync(ct);
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReportCommands");
      logger.LogInformation("compliance-diff started: baseline={Baseline}, target={Target}", baseline, target);

      var diff = ComplianceDiffGenerator.ComputeDiffFromPaths(baseline, target);

      if (regressionsOnly)
      {
        diff = new Core.Models.ComplianceDiff
        {
          BaselineLabel = diff.BaselineLabel,
          TargetLabel = diff.TargetLabel,
          BaselineTimestamp = diff.BaselineTimestamp,
          TargetTimestamp = diff.TargetTimestamp,
          BaselineCompliancePercent = diff.BaselineCompliancePercent,
          TargetCompliancePercent = diff.TargetCompliancePercent,
          DeltaPercent = diff.DeltaPercent,
          Regressions = diff.Regressions,
          Remediations = [],
          Added = [],
          Removed = [],
          SeveritySummary = diff.SeveritySummary
        };
      }

      if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
      {
        if (string.IsNullOrWhiteSpace(output))
        {
          Console.Error.WriteLine("--output is required when using --format json");
          context.ExitCode = 2;
          await host.StopAsync(ct);
          return;
        }
        ComplianceDiffGenerator.WriteDiffJson(diff, output);
        Console.WriteLine("Diff written to: " + output);
      }
      else if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
      {
        if (string.IsNullOrWhiteSpace(output))
        {
          Console.Error.WriteLine("--output is required when using --format csv");
          context.ExitCode = 2;
          await host.StopAsync(ct);
          return;
        }
        ComplianceDiffGenerator.WriteDiffCsv(diff, output);
        Console.WriteLine("Diff written to: " + output);
      }
      else
      {
        ComplianceDiffGenerator.WriteDiffConsole(diff, Console.Out);
      }

      logger.LogInformation("compliance-diff completed: {Regressions} regressions, {Remediations} remediations",
        diff.Regressions.Count, diff.Remediations.Count);
      await host.StopAsync(ct);
    });

    rootCmd.AddCommand(cmd);
  }
}

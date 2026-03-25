using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class PhaseCImportCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterAcasImport(rootCmd, buildHost);
    RegisterNessusImport(rootCmd, buildHost);
    RegisterCklImport(rootCmd, buildHost);
  }

  private static void RegisterAcasImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("acas-import", "Import ACAS/Nessus XML and correlate findings to bundle controls");
    var fileOpt = new Option<string>("--file", "Path to .nessus XML file") { IsRequired = true };
    var bundleOpt = new Option<string?>("--bundle", () => null, "Optional bundle root for rule correlation");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(bundleOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("acas-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var result = await phaseC.AcasImportAsync(file, bundle, ctx.GetCancellationToken()).ConfigureAwait(false);
        var exitCode = result.UnmatchedCount > 0 ? PhaseCExpansionCommands.ExitActionRequired : PhaseCExpansionCommands.ExitSuccess;
        var message = result.UnmatchedCount > 0
          ? "Unmatched findings detected; review recommended."
          : "ACAS import and correlation completed.";
        if (json)
          PhaseCExpansionCommands.WriteJsonEnvelope("acas-import", true, exitCode, result, message);
        else
        {
          Console.WriteLine($"ACAS import complete: {file}");
          Console.WriteLine($"  Total findings: {result.TotalFindings}");
          Console.WriteLine($"  Correlated: {result.CorrelatedCount}");
          Console.WriteLine($"  Unmatched: {result.UnmatchedCount}");
          Console.WriteLine($"  Mismatches: {result.Mismatches}");
          if (exitCode == PhaseCExpansionCommands.ExitActionRequired)
            Console.WriteLine("  Action: unmatched findings require review.");
        }

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "acas-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterNessusImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("nessus-import", "Import Nessus .nessus XML findings");
    var fileOpt = new Option<string>("--file", "Path to .nessus XML file") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("nessus-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var findings = await phaseC.NessusImportAsync(file, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          PhaseCExpansionCommands.WriteJsonEnvelope("nessus-import", true, PhaseCExpansionCommands.ExitSuccess, findings, "Nessus import completed.");
        else
        {
          Console.WriteLine($"Nessus import complete: {file}");
          Console.WriteLine($"  Findings: {findings.Count}");
          Console.WriteLine($"  Critical/High: {findings.Count(f => f.Severity >= 3)}");
        }

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "nessus-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCklImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-import", "Import STIG Viewer CKL checklist");
    var fileOpt = new Option<string>("--file", "Path to .ckl file") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var checklist = await phaseC.CklImportAsync(file, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          PhaseCExpansionCommands.WriteJsonEnvelope("ckl-import", true, PhaseCExpansionCommands.ExitSuccess, checklist, "CKL import completed.");
        else
        {
          Console.WriteLine($"CKL import complete: {file}");
          Console.WriteLine($"  Asset: {checklist.AssetName}");
          Console.WriteLine($"  STIG: {checklist.StigTitle}");
          Console.WriteLine($"  Findings: {checklist.Findings.Count}");
        }

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "ckl-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

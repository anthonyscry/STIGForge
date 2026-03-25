using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class PhaseCCklCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterCklExport(rootCmd, buildHost);
    RegisterCklMerge(rootCmd, buildHost);
  }

  private static void RegisterCklExport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-export", "Export bundle controls into CKL format");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outputOpt = new Option<string>("--output", "Output CKL path") { IsRequired = true };
    var hostOpt = new Option<string>("--host-name", () => Environment.MachineName, "Host name for CKL asset metadata");
    var stigOpt = new Option<string>("--stig-title", () => "STIG Checklist", "STIG title metadata");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(hostOpt);
    cmd.AddOption(stigOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var hostName = ctx.ParseResult.GetValueForOption(hostOpt) ?? Environment.MachineName;
      var stigTitle = ctx.ParseResult.GetValueForOption(stigOpt) ?? "STIG Checklist";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-export");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var exported = await phaseC.CklExportAsync(bundle, output, hostName, stigTitle, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          PhaseCExpansionCommands.WriteJsonEnvelope("ckl-export", true, PhaseCExpansionCommands.ExitSuccess, new { bundle, output = exported, hostName, stigTitle }, "CKL export completed.");
        else
          Console.WriteLine($"CKL export complete: {exported}");

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "ckl-export", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCklMerge(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-merge", "Merge imported CKL with bundle verification results and detect conflicts");
    var cklFileOpt = new Option<string>("--ckl-file", "Path to imported .ckl file") { IsRequired = true };
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var strategyOpt = new Option<string>("--strategy", () => CklConflictResolutionStrategy.MostRecent.ToString(), "Conflict strategy: CklWins|StigForgeWins|MostRecent|Manual");
    var outputOpt = new Option<string?>("--output", () => null, "Optional output path for merged .ckl");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(cklFileOpt);
    cmd.AddOption(bundleOpt);
    cmd.AddOption(strategyOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var cklFile = ctx.ParseResult.GetValueForOption(cklFileOpt) ?? string.Empty;
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var strategyText = ctx.ParseResult.GetValueForOption(strategyOpt) ?? CklConflictResolutionStrategy.MostRecent.ToString();
      var output = ctx.ParseResult.GetValueForOption(outputOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-merge");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();
      var cklExporter = host.Services.GetRequiredService<CklExporter>();

      try
      {
        var strategy = PhaseCExpansionCommands.ParseCklMergeStrategy(strategyText);
        var result = await phaseC.CklMergeAsync(cklFile, bundle, strategy, ctx.GetCancellationToken()).ConfigureAwait(false);

        string? exportPath = null;
        if (!string.IsNullOrWhiteSpace(output))
        {
          exportPath = output;
          var outputDirectory = Path.GetDirectoryName(output);
          if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
          cklExporter.Export(result.MergedChecklist, output);
        }

        var exitCode = strategy == CklConflictResolutionStrategy.Manual && result.Conflicts.Count > 0
          ? PhaseCExpansionCommands.ExitActionRequired
          : PhaseCExpansionCommands.ExitSuccess;

        if (json)
        {
          PhaseCExpansionCommands.WriteJsonEnvelope("ckl-merge", true, exitCode,
            new
            {
              strategy = result.Strategy,
              mergedFindings = result.MergedFindings.Count,
              conflicts = result.Conflicts.Count,
              manualResolutionRequired = exitCode == PhaseCExpansionCommands.ExitActionRequired,
              output = exportPath,
              conflictDetails = result.Conflicts
            },
            exitCode == PhaseCExpansionCommands.ExitActionRequired
              ? "Merge completed with unresolved manual conflicts."
              : "CKL merge completed.");
        }
        else
        {
          Console.WriteLine($"CKL merge complete: {cklFile}");
          Console.WriteLine($"  Strategy: {result.Strategy}");
          Console.WriteLine($"  Merged findings: {result.MergedFindings.Count}");
          Console.WriteLine($"  Conflicts: {result.Conflicts.Count}");
          if (!string.IsNullOrWhiteSpace(exportPath))
            Console.WriteLine($"  Output: {exportPath}");
          if (exitCode == PhaseCExpansionCommands.ExitActionRequired)
            Console.WriteLine("  Action: resolve conflicts manually or rerun with a non-manual strategy.");
        }

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "ckl-merge", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

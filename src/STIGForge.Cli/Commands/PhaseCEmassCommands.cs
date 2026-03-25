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

internal static class PhaseCEmassCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterEmassPackage(rootCmd, buildHost);
  }

  private static void RegisterEmassPackage(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("emass-package", "Generate eMASS package from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var systemNameOpt = new Option<string>("--system-name", "System name") { IsRequired = true };
    var acronymOpt = new Option<string>("--system-acronym", "System acronym") { IsRequired = true };
    var outputOpt = new Option<string>("--output", "Output directory") { IsRequired = true };
    var previousOpt = new Option<string?>("--previous-package", () => null, "Previous package directory for change log diff");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(systemNameOpt);
    cmd.AddOption(acronymOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(previousOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var systemName = ctx.ParseResult.GetValueForOption(systemNameOpt) ?? string.Empty;
      var acronym = ctx.ParseResult.GetValueForOption(acronymOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var previous = ctx.ParseResult.GetValueForOption(previousOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("emass-package");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var package = await phaseC.EmassPackageAsync(bundle, systemName, acronym, output, previous, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
        {
          PhaseCExpansionCommands.WriteJsonEnvelope("emass-package", true, PhaseCExpansionCommands.ExitSuccess,
            new
            {
              package.PackageId,
              package.SystemName,
              package.SystemAcronym,
              ControlCount = package.ControlCorrelationMatrix.Controls.Count,
              PoamEntryCount = package.Poam.Entries.Count,
              output
            },
            "eMASS package generation completed.");
        }
        else
        {
          Console.WriteLine($"eMASS package generated: {package.PackageId}");
          Console.WriteLine($"  System: {package.SystemName} ({package.SystemAcronym})");
          Console.WriteLine($"  Controls: {package.ControlCorrelationMatrix.Controls.Count}");
          Console.WriteLine($"  POA&M entries: {package.Poam.Entries.Count}");
        }

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "emass-package", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

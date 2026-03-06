using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Cli.Commands;

internal static class ImportCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterImportPack(rootCmd, buildHost);
    RegisterOverlayImportPowerStig(rootCmd, buildHost);
    RegisterPowerStigMapExport(rootCmd, buildHost);
  }

  private static void RegisterImportPack(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("import-pack", "Import DISA content packs (STIG XCCDF, SCAP bundles, or GPO packages)");
    var zipArg = new Argument<string>("zip", "Path to zip file");
    var nameOpt = new Option<string>("--name", () => "Imported_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm"), "Pack name");
    cmd.AddArgument(zipArg);
    cmd.AddOption(nameOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var zip = ctx.ParseResult.GetValueForArgument(zipArg);
      var name = ctx.ParseResult.GetValueForOption(nameOpt) ?? string.Empty;
      var ct = ctx.GetCancellationToken();
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("import-pack started: zip={Zip}, name={Name}", zip, name);
      var importer = host.Services.GetRequiredService<ContentPackImporter>();
      var pack = await importer.ImportZipAsync(zip, name, "cli_import", ct);
      logger.LogInformation("import-pack completed: packId={PackId}, name={PackName}", pack.PackId, pack.Name);
      Console.WriteLine("Imported: " + pack.Name + " (" + pack.PackId + ")");
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterOverlayImportPowerStig(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("overlay-import-powerstig", "Import PowerSTIG overrides from CSV");
    var csvOpt = new Option<string>("--csv", "CSV path with RuleId,SettingName,Value") { IsRequired = true };
    var nameOpt = new Option<string>("--name", () => "PowerSTIG Overrides", "Overlay name");
    var idOpt = new Option<string>("--overlay-id", () => string.Empty, "Update an existing overlay id (optional)");
    cmd.AddOption(csvOpt);
    cmd.AddOption(nameOpt);
    cmd.AddOption(idOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var csvPath = ctx.ParseResult.GetValueForOption(csvOpt) ?? string.Empty;
      var overlayName = ctx.ParseResult.GetValueForOption(nameOpt) ?? string.Empty;
      var overlayId = ctx.ParseResult.GetValueForOption(idOpt) ?? string.Empty;
      var ct = ctx.GetCancellationToken();
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("overlay-import-powerstig started: csv={Csv}, name={Name}", csvPath, overlayName);
      var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
      var overlays = Helpers.ReadPowerStigOverrides(csvPath);

      Overlay overlay;
      if (!string.IsNullOrWhiteSpace(overlayId))
        overlay = await overlaysRepo.GetAsync(overlayId, ct) ?? new Overlay { OverlayId = overlayId };
      else
        overlay = new Overlay { OverlayId = Guid.NewGuid().ToString("n") };

      overlay.Name = overlayName;
      overlay.UpdatedAt = DateTimeOffset.Now;
      overlay.PowerStigOverrides = overlays;
      await overlaysRepo.SaveAsync(overlay, ct);
      logger.LogInformation("overlay-import-powerstig completed: overlayId={OverlayId}", overlay.OverlayId);
      Console.WriteLine("Overlay saved: " + overlay.OverlayId + " (" + overlay.Name + ")");
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterPowerStigMapExport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("powerstig-map-export", "Export PowerSTIG mapping template CSV from a pack");
    var packOpt = new Option<string>("--pack-id", "Pack id to export from") { IsRequired = true };
    var outOpt = new Option<string>("--output", "Output CSV path") { IsRequired = true };
    cmd.AddOption(packOpt);
    cmd.AddOption(outOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var packId = ctx.ParseResult.GetValueForOption(packOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outOpt) ?? string.Empty;
      var ct = ctx.GetCancellationToken();
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("powerstig-map-export started: packId={PackId}, output={Output}", packId, output);
      var controlsRepo = host.Services.GetRequiredService<IControlRepository>();
      var list = await controlsRepo.ListControlsAsync(packId, ct);
      Helpers.WritePowerStigMapCsv(output, list);
      logger.LogInformation("powerstig-map-export completed: {Count} controls exported", list.Count);
      Console.WriteLine("Wrote mapping template: " + output);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

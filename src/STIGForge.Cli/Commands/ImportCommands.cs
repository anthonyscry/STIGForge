using System.CommandLine;
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

    cmd.SetHandler(async (zip, name) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("import-pack started: zip={Zip}, name={Name}", zip, name);

      var importer = host.Services.GetRequiredService<ContentPackImporter>();

      // Build a single-operation planned import so staging transitions are explicit and observable.
      var planned = new PlannedContentImport
      {
        ZipPath = zip,
        FileName = System.IO.Path.GetFileName(zip),
        ArtifactKind = STIGForge.Content.Import.ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "cli_import",
        State = ImportOperationState.Planned
      };

      Console.WriteLine($"[{planned.State}] {planned.FileName}");

      try
      {
        var packs = await importer.ExecutePlannedImportAsync(planned, CancellationToken.None);
        // planned.State is now Committed
        foreach (var pack in packs)
        {
          logger.LogInformation("import-pack completed: state={State}, packId={PackId}, name={PackName}",
            planned.State, pack.PackId, pack.Name);
          Console.WriteLine($"[{planned.State}] {pack.Name} ({pack.PackId})");
        }
      }
      catch (Exception ex)
      {
        // planned.State is now Failed; FailureReason is populated
        logger.LogError("import-pack failed: state={State}, zip={Zip}, reason={Reason}",
          planned.State, zip, planned.FailureReason ?? ex.Message);
        Console.Error.WriteLine($"[{planned.State}] {planned.FileName}: {planned.FailureReason ?? ex.Message}");
        throw;
      }

      await host.StopAsync();
    }, zipArg, nameOpt);

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

    cmd.SetHandler(async (csvPath, overlayName, overlayId) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("overlay-import-powerstig started: csv={Csv}, name={Name}", csvPath, overlayName);
      var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
      var overlays = Helpers.ReadPowerStigOverrides(csvPath);

      Overlay overlay;
      if (!string.IsNullOrWhiteSpace(overlayId))
        overlay = await overlaysRepo.GetAsync(overlayId, CancellationToken.None) ?? new Overlay { OverlayId = overlayId };
      else
        overlay = new Overlay { OverlayId = Guid.NewGuid().ToString("n") };

      overlay.Name = overlayName;
      overlay.UpdatedAt = DateTimeOffset.Now;
      overlay.PowerStigOverrides = overlays;
      await overlaysRepo.SaveAsync(overlay, CancellationToken.None);
      logger.LogInformation("overlay-import-powerstig completed: overlayId={OverlayId}", overlay.OverlayId);
      Console.WriteLine("Overlay saved: " + overlay.OverlayId + " (" + overlay.Name + ")");
      await host.StopAsync();
    }, csvOpt, nameOpt, idOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterPowerStigMapExport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("powerstig-map-export", "Export PowerSTIG mapping template CSV from a pack");
    var packOpt = new Option<string>("--pack-id", "Pack id to export from") { IsRequired = true };
    var outOpt = new Option<string>("--output", "Output CSV path") { IsRequired = true };
    cmd.AddOption(packOpt);
    cmd.AddOption(outOpt);

    cmd.SetHandler(async (packId, output) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ImportCommands");
      logger.LogInformation("powerstig-map-export started: packId={PackId}, output={Output}", packId, output);
      var controlsRepo = host.Services.GetRequiredService<IControlRepository>();
      var list = await controlsRepo.ListControlsAsync(packId, CancellationToken.None);
      Helpers.WritePowerStigMapCsv(output, list);
      logger.LogInformation("powerstig-map-export completed: {Count} controls exported", list.Count);
      Console.WriteLine("Wrote mapping template: " + output);
      await host.StopAsync();
    }, packOpt, outOpt);

    rootCmd.AddCommand(cmd);
  }
}

using System.CommandLine;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.Storage;

static IHost BuildHost()
{
  return Host.CreateDefaultBuilder()
    .UseSerilog((ctx, lc) =>
    {
      var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "STIGForge", "logs");
      Directory.CreateDirectory(root);
      lc.MinimumLevel.Information()
        .WriteTo.File(Path.Combine(root, "stigforge-cli.log"), rollingInterval: RollingInterval.Day);
    })
    .ConfigureServices(services =>
    {
      services.AddSingleton<IClock, SystemClock>();
      services.AddSingleton<IClassificationScopeService, ClassificationScopeService>();
      services.AddSingleton<IPathBuilder, PathBuilder>();
      services.AddSingleton<IHashingService, Sha256HashingService>();

      services.AddSingleton(sp =>
      {
        var paths = sp.GetRequiredService<IPathBuilder>();
        Directory.CreateDirectory(paths.GetAppDataRoot());
        Directory.CreateDirectory(paths.GetLogsRoot());
        var dbPath = Path.Combine(paths.GetAppDataRoot(), "data", "stigforge.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var cs = "Data Source=" + dbPath;
        DbBootstrap.EnsureCreated(cs);
        return cs;
      });

      services.AddSingleton<IContentPackRepository>(sp => new SqliteContentPackRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IControlRepository>(sp => new SqliteJsonControlRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IProfileRepository>(sp => new SqliteJsonProfileRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IOverlayRepository>(sp => new SqliteJsonOverlayRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<ContentPackImporter>();
    })
    .Build();
}

var rootCmd = new RootCommand("STIGForge CLI (offline-first)");

var importCmd = new Command("import-pack", "Import a DISA content pack zip");
var zipArg = new Argument<string>("zip", "Path to zip file");
var nameOpt = new Option<string>("--name", () => "Imported_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm"), "Pack name");
importCmd.AddArgument(zipArg);
importCmd.AddOption(nameOpt);

importCmd.SetHandler(async (zip, name) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  var importer = host.Services.GetRequiredService<ContentPackImporter>();
  var pack = await importer.ImportZipAsync(zip, name, "cli_import", CancellationToken.None);
  Console.WriteLine("Imported: " + pack.Name + " (" + pack.PackId + ")");

  await host.StopAsync();
}, zipArg, nameOpt);

rootCmd.AddCommand(importCmd);

return await rootCmd.InvokeAsync(args);

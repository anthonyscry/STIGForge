using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Apply.Snapshot;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.Storage;
using STIGForge.Infrastructure.System;

namespace STIGForge.App;

public partial class App : Application
{
  private IHost? _host;
  public IServiceProvider Services => _host!.Services;

  protected override void OnStartup(StartupEventArgs e)
  {
    _host = Host.CreateDefaultBuilder()
      .UseSerilog((ctx, lc) =>
      {
        lc.MinimumLevel.Information()
          .WriteTo.File(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "STIGForge", "logs", "stigforge.log"),
            rollingInterval: RollingInterval.Day);
      })
      .ConfigureServices(services =>
      {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IClassificationScopeService, ClassificationScopeService>();
        services.AddSingleton<STIGForge.Core.Services.ReleaseAgeGate>();

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
        services.AddSingleton<BundleBuilder>();
        services.AddSingleton<SnapshotService>();
        services.AddSingleton<RollbackScriptGenerator>();
        services.AddSingleton<STIGForge.Apply.ApplyRunner>();
        services.AddSingleton<BundleOrchestrator>();
        services.AddSingleton<STIGForge.Export.EmassExporter>();
        services.AddSingleton<STIGForge.Evidence.EvidenceCollector>();
        services.AddSingleton<OverlayEditorViewModel>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
      })
      .Build();

    _host.Start();

    var main = _host.Services.GetRequiredService<MainWindow>();
    main.Show();

    base.OnStartup(e);
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    if (_host != null)
    {
      await _host.StopAsync();
      _host.Dispose();
    }

    base.OnExit(e);
  }
}

using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
using STIGForge.Verify;

namespace STIGForge.App;

public partial class App : Application
{
  private IHost? _host;
  public IServiceProvider Services => _host!.Services;

  protected override void OnStartup(StartupEventArgs e)
  {
    RegisterUnhandledExceptionHandlers();

    try
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
        .UseDefaultServiceProvider((_, options) =>
        {
          options.ValidateScopes = true;
          options.ValidateOnBuild = true;
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
          services.AddSingleton<STIGForge.Apply.Dsc.LcmService>();
          services.AddSingleton<STIGForge.Apply.Reboot.RebootCoordinator>();
          services.AddSingleton<STIGForge.Apply.ApplyRunner>();
          services.AddSingleton<EvaluateStigRunner>();
          services.AddSingleton<IScapRunner, ScapRunner>();
          services.AddSingleton<ScapRunner>();
          services.AddSingleton<DscScanRunner>();
          services.AddSingleton<IVerificationWorkflowService, VerificationWorkflowService>();
          services.AddSingleton<VerificationArtifactAggregationService>();
          services.AddSingleton<IBundleMissionSummaryService, BundleMissionSummaryService>();
          services.AddSingleton<BundleOrchestrator>();
          services.AddSingleton<STIGForge.Export.EmassExporter>();
          services.AddSingleton<STIGForge.Evidence.EvidenceCollector>();
          services.AddSingleton<IAuditTrailService>(sp =>
            new AuditTrailService(sp.GetRequiredService<string>(), sp.GetRequiredService<IClock>()));
          services.AddSingleton<ICredentialStore>(sp =>
            new DpapiCredentialStore(sp.GetRequiredService<IPathBuilder>()));
          services.AddSingleton<ScheduledTaskService>();
          services.AddSingleton<FleetService>(sp =>
            new FleetService(sp.GetRequiredService<ICredentialStore>()));
          services.AddSingleton<OverlayEditorViewModel>();
          services.AddSingleton<ManualAnswerService>();
          services.AddSingleton<ControlAnnotationService>();

          services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<ContentPackImporter>(),
            sp.GetRequiredService<IContentPackRepository>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IControlRepository>(),
            sp.GetRequiredService<IOverlayRepository>(),
            sp.GetRequiredService<BundleBuilder>(),
            sp.GetRequiredService<STIGForge.Apply.ApplyRunner>(),
            sp.GetRequiredService<IVerificationWorkflowService>(),
            sp.GetRequiredService<STIGForge.Export.EmassExporter>(),
            sp.GetRequiredService<IPathBuilder>(),
            sp.GetRequiredService<STIGForge.Evidence.EvidenceCollector>(),
            sp.GetRequiredService<IBundleMissionSummaryService>(),
            sp.GetRequiredService<VerificationArtifactAggregationService>(),
            sp.GetRequiredService<ManualAnswerService>(),
            sp.GetRequiredService<ControlAnnotationService>(),
            sp.GetRequiredService<IAuditTrailService>(),
            sp.GetRequiredService<ScheduledTaskService>(),
            sp.GetRequiredService<FleetService>(),
            sp.GetService<ILogger<MainViewModel>>()));
          services.AddSingleton<MainWindow>();
        })
        .Build();

      _host.Start();

      var main = _host.Services.GetRequiredService<MainWindow>();
      main.Show();

      base.OnStartup(e);
    }
    catch (Exception ex)
    {
      Log.Fatal(ex, "Unhandled exception during application startup.");
      MessageBox.Show(
        "STIGForge failed to start.\n\n" + ex.Message,
        "STIGForge Startup Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
      Log.CloseAndFlush();
      Shutdown(-1);
    }
  }

  private void RegisterUnhandledExceptionHandlers()
  {
    DispatcherUnhandledException += OnDispatcherUnhandledException;
    AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
  }

  private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
  {
    Log.Fatal(e.Exception, "Unhandled dispatcher exception.");
    MessageBox.Show(
      "STIGForge encountered an unexpected error and must close.\n\n" + e.Exception.Message,
      "STIGForge Error",
      MessageBoxButton.OK,
      MessageBoxImage.Error);
    e.Handled = true;
    Shutdown(-1);
  }

  private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
  {
    if (e.ExceptionObject is Exception ex)
      Log.Fatal(ex, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);
    else
      Log.Fatal("Unhandled AppDomain exception object {ExceptionObject}. IsTerminating={IsTerminating}", e.ExceptionObject, e.IsTerminating);

    Log.CloseAndFlush();
  }

  private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
  {
    Log.Error(e.Exception, "Unobserved task exception.");
    e.SetObserved();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    try
    {
      DispatcherUnhandledException -= OnDispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
      System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

      base.OnExit(e);

      if (_host != null)
      {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
      }
    }
    catch (Exception ex)
    {
      try
      {
        Log.Error(ex, "Error during application shutdown.");
      }
      catch
      {
      }
    }
  }
}

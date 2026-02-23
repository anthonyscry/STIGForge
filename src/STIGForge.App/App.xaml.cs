using System.IO;
using System.Text;
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
using STIGForge.Infrastructure.Logging;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Infrastructure.Storage;
using STIGForge.Infrastructure.System;
using STIGForge.Verify;

namespace STIGForge.App;

public partial class App : Application
{
  private IHost? _host;
  private static readonly object StartupTraceLock = new();
  public IServiceProvider Services => _host!.Services;

  protected override void OnStartup(StartupEventArgs e)
  {
    TraceStartup("OnStartup begin");
    RegisterUnhandledExceptionHandlers();
    TraceStartup("Unhandled exception handlers registered");

    try
    {
      TraceStartup("Host build begin");
      _host = Host.CreateDefaultBuilder()
        .UseSerilog((ctx, lc) =>
        {
          LoggingConfiguration.ConfigureFromEnvironment();

          var logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "STIGForge", "logs");
          Directory.CreateDirectory(logRoot);

          lc.MinimumLevel.ControlledBy(LoggingConfiguration.LevelSwitch)
            .Enrich.With(new CorrelationIdEnricher())
            .Enrich.FromLogContext()
            .WriteTo.File(
              Path.Combine(logRoot, "stigforge.log"),
              rollingInterval: RollingInterval.Day,
              outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}");
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

          services.AddSingleton<IPathBuilder>(_ => new PathBuilder());
          services.AddSingleton<IHashingService, Sha256HashingService>();

          services.AddSingleton(sp =>
          {
            TraceStartup("Connection string factory begin");
            var paths = sp.GetRequiredService<IPathBuilder>();
            TraceStartup("Connection string factory got IPathBuilder");
            Directory.CreateDirectory(paths.GetAppDataRoot());
            Directory.CreateDirectory(paths.GetLogsRoot());
            TraceStartup("Connection string factory ensured app directories");
            var dbPath = Path.Combine(paths.GetAppDataRoot(), "data", "stigforge.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            var cs = "Data Source=" + dbPath;
            TraceStartup("Connection string factory calling DbBootstrap.EnsureCreated: " + dbPath);
            DbBootstrap.EnsureCreated(cs);
            TraceStartup("Connection string factory EnsureCreated complete");
            return cs;
          });

          services.AddSingleton<IContentPackRepository>(sp => new SqliteContentPackRepository(sp.GetRequiredService<string>()));
          services.AddSingleton<IControlRepository>(sp => new SqliteJsonControlRepository(sp.GetRequiredService<string>()));
          services.AddSingleton<IProfileRepository>(sp => new SqliteJsonProfileRepository(sp.GetRequiredService<string>()));
          services.AddSingleton<IOverlayRepository>(sp => new SqliteJsonOverlayRepository(sp.GetRequiredService<string>()));
          services.AddSingleton<IMissionRunRepository>(sp => new MissionRunRepository(sp.GetRequiredService<string>()));

          services.AddSingleton<ContentPackImporter>();
          services.AddSingleton<STIGForge.Core.Services.OverlayConflictDetector>();
          services.AddSingleton<BundleBuilder>();
          services.AddSingleton<SnapshotService>();
          services.AddSingleton<RollbackScriptGenerator>();
          services.AddSingleton<STIGForge.Apply.Dsc.LcmService>();
          services.AddSingleton<STIGForge.Apply.Reboot.RebootCoordinator>();
          services.AddSingleton<STIGForge.Apply.ApplyRunner>();
          services.AddSingleton<EvaluateStigRunner>();
          services.AddSingleton<ScapRunner>();
          services.AddSingleton<IVerificationWorkflowService, VerificationWorkflowService>();
          services.AddSingleton<VerificationArtifactAggregationService>();
          services.AddSingleton<MissionTracingService>();
          services.AddSingleton<IBundleMissionSummaryService, BundleMissionSummaryService>();
          services.AddSingleton<ImportSelectionOrchestrator>();
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
          services.AddSingleton<OverlayEditorViewModel>(sp =>
            new OverlayEditorViewModel(
              sp.GetRequiredService<IOverlayRepository>(),
              sp.GetRequiredService<IControlRepository>()));

          services.AddSingleton<MainViewModel>(sp =>
          {
            TraceStartup("MainViewModel factory begin");
            var importer = sp.GetRequiredService<ContentPackImporter>();
            TraceStartup("MainViewModel factory got ContentPackImporter");
            var packs = sp.GetRequiredService<IContentPackRepository>();
            TraceStartup("MainViewModel factory got IContentPackRepository");
            var profiles = sp.GetRequiredService<IProfileRepository>();
            TraceStartup("MainViewModel factory got IProfileRepository");
            var controls = sp.GetRequiredService<IControlRepository>();
            TraceStartup("MainViewModel factory got IControlRepository");
            var overlays = sp.GetRequiredService<IOverlayRepository>();
            TraceStartup("MainViewModel factory got IOverlayRepository");
            var builder = sp.GetRequiredService<BundleBuilder>();
            TraceStartup("MainViewModel factory got BundleBuilder");
            var applyRunner = sp.GetRequiredService<STIGForge.Apply.ApplyRunner>();
            TraceStartup("MainViewModel factory got ApplyRunner");
            var verificationWorkflow = sp.GetRequiredService<IVerificationWorkflowService>();
            TraceStartup("MainViewModel factory got IVerificationWorkflowService");
            var emassExporter = sp.GetRequiredService<STIGForge.Export.EmassExporter>();
            TraceStartup("MainViewModel factory got EmassExporter");
            var paths = sp.GetRequiredService<IPathBuilder>();
            TraceStartup("MainViewModel factory got IPathBuilder");
            var evidence = sp.GetRequiredService<STIGForge.Evidence.EvidenceCollector>();
            TraceStartup("MainViewModel factory got EvidenceCollector");
            var bundleMissionSummary = sp.GetRequiredService<IBundleMissionSummaryService>();
            TraceStartup("MainViewModel factory got IBundleMissionSummaryService");
            var artifactAggregation = sp.GetRequiredService<VerificationArtifactAggregationService>();
            TraceStartup("MainViewModel factory got VerificationArtifactAggregationService");
            var importSelectionOrchestrator = sp.GetRequiredService<ImportSelectionOrchestrator>();
            TraceStartup("MainViewModel factory got ImportSelectionOrchestrator");
            var audit = sp.GetRequiredService<IAuditTrailService>();
            TraceStartup("MainViewModel factory got IAuditTrailService");
            var scheduledTaskService = sp.GetRequiredService<ScheduledTaskService>();
            TraceStartup("MainViewModel factory got ScheduledTaskService");
            var fleetService = sp.GetRequiredService<FleetService>();
            TraceStartup("MainViewModel factory got FleetService");

            return new MainViewModel(
              importer,
              packs,
              profiles,
              controls,
              overlays,
              builder,
              applyRunner,
              verificationWorkflow,
              emassExporter,
              paths,
              evidence,
              bundleMissionSummary,
              artifactAggregation,
              importSelectionOrchestrator,
              audit,
              scheduledTaskService,
              fleetService);
          });
          services.AddSingleton<MainWindow>();
        })
        .Build();
      TraceStartup("Host build complete");

      var main = new MainWindow();
      TraceStartup("Main window constructed");

      MainWindow = main;
      main.Show();
      TraceStartup("Main window shown");

      TraceStartup("MainViewModel resolve begin");
      var vm = _host.Services.GetRequiredService<MainViewModel>();
      TraceStartup("MainViewModel resolve complete");
      main.BindViewModel(vm);
      TraceStartup("MainViewModel bound to main window");
      vm.StartInitialLoad();
      TraceStartup("MainViewModel initial load started");

      _ = StartHostAsync(_host);
      TraceStartup("Host start scheduled");

      base.OnStartup(e);
      TraceStartup("OnStartup complete");
    }
    catch (Exception ex)
    {
      TraceStartup("OnStartup failed", ex);
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

  private async Task StartHostAsync(IHost host)
  {
    try
    {
      TraceStartup("Host start begin");
      await host.StartAsync().ConfigureAwait(false);
      TraceStartup("Host start complete");
    }
    catch (Exception ex)
    {
      TraceStartup("Host start failed", ex);
      Log.Error(ex, "Host failed to start after main window initialization.");

      Dispatcher.Invoke(() =>
      {
        MessageBox.Show(
          "STIGForge started with a background startup error.\n\n" + ex.Message,
          "STIGForge Startup Warning",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      });
    }
  }

  private static void TraceStartup(string message, Exception? ex = null)
  {
    try
    {
      var root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STIGForge");
      Directory.CreateDirectory(root);

      var line = DateTimeOffset.Now.ToString("o") + " | " + message;
      if (ex != null)
        line += " | " + ex.GetType().FullName + ": " + ex.Message;

      var path = Path.Combine(root, "startup-trace.log");
      lock (StartupTraceLock)
      {
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
      }
    }
    catch
    {
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
      TraceStartup("OnExit begin");
      DispatcherUnhandledException -= OnDispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
      System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

      base.OnExit(e);

      if (_host != null)
      {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
        TraceStartup("Host stopped and disposed");
      }
    }
    catch (Exception ex)
    {
      TraceStartup("OnExit failed", ex);
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

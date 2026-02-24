using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using STIGForge.Apply.Snapshot;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Infrastructure.Logging;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.Storage;
using STIGForge.Infrastructure.System;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Verify;

namespace STIGForge.Cli;

public static class CliHostFactory
{
  public static IHost BuildHost()
  {
    return BuildHost(static () => new PathBuilder());
  }

  public static IHost BuildHost(Func<IPathBuilder> pathBuilderFactory)
  {
    ArgumentNullException.ThrowIfNull(pathBuilderFactory);

    return Host.CreateDefaultBuilder()
      .UseSerilog((_, lc) =>
      {
        LoggingConfiguration.ConfigureFromEnvironment();

        var root = pathBuilderFactory().GetLogsRoot();
        Directory.CreateDirectory(root);

        lc.MinimumLevel.ControlledBy(LoggingConfiguration.LevelSwitch)
          .Enrich.With(new CorrelationIdEnricher())
          .Enrich.FromLogContext()
          .WriteTo.File(
            Path.Combine(root, "stigforge-cli.log"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}");
      })
      .UseDefaultServiceProvider((_, options) =>
      {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
      })
      .ConfigureServices(services => ConfigureServices(services, pathBuilderFactory))
      .Build();
  }

  public static void ConfigureServices(IServiceCollection services)
  {
    ConfigureServices(services, static () => new PathBuilder());
  }

  public static void ConfigureServices(IServiceCollection services, Func<IPathBuilder> pathBuilderFactory)
  {
    ArgumentNullException.ThrowIfNull(pathBuilderFactory);

    services.AddSingleton<IClock, SystemClock>();
    services.AddSingleton<IProcessRunner, ProcessRunner>();
    services.AddSingleton<IClassificationScopeService, ClassificationScopeService>();
    services.AddSingleton<ReleaseAgeGate>();
    services.AddSingleton<IPathBuilder>(_ => pathBuilderFactory());
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
    services.AddSingleton<IMissionRunRepository>(sp => new MissionRunRepository(sp.GetRequiredService<string>()));
    services.AddSingleton<ContentPackImporter>();
    services.AddSingleton<STIGForge.Core.Services.OverlayConflictDetector>();
    services.AddSingleton<OverlayMergeService>();
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
    services.AddSingleton<PerformanceInstrumenter>();
    services.AddSingleton<BaselineDiffService>();
    services.AddSingleton<OverlayRebaseService>();
    services.AddSingleton<ManualAnswerService>();
    services.AddSingleton<IBundleMissionSummaryService, BundleMissionSummaryService>();
    services.AddSingleton<EvidenceCollector>();
    services.AddSingleton<BundleOrchestrator>();
    services.AddSingleton<STIGForge.Export.EmassExporter>();
    services.AddSingleton<IAuditTrailService>(sp =>
      new AuditTrailService(sp.GetRequiredService<string>(), sp.GetRequiredService<IClock>()));
#pragma warning disable CA1416
    services.AddSingleton<ICredentialStore>(sp =>
      new DpapiCredentialStore(sp.GetRequiredService<IPathBuilder>()));
#pragma warning restore CA1416
  }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using STIGForge.Apply.Remediation;
using STIGForge.Apply.Security;
using STIGForge.Apply.Snapshot;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Infrastructure.Hashing;
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
        var root = pathBuilderFactory().GetLogsRoot();
        Directory.CreateDirectory(root);
        lc.MinimumLevel.Information()
          .WriteTo.File(Path.Combine(root, "stigforge-cli.log"), rollingInterval: RollingInterval.Day);
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
      return new DbConnectionString(cs);
    });

    services.AddSingleton<IContentPackRepository>(sp => new SqliteContentPackRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<IControlRepository>(sp => new SqliteJsonControlRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<IProfileRepository>(sp => new SqliteJsonProfileRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<IOverlayRepository>(sp => new SqliteJsonOverlayRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<IFleetInventoryRepository>(sp => new FleetInventoryRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<ContentPackImporter>();
    services.AddSingleton<OverlayConflictDetector>();
    services.AddSingleton<OverlayMergeService>();
    services.AddSingleton<BundleBuilder>();
    services.AddSingleton<IBundleBuilder>(sp => sp.GetRequiredService<BundleBuilder>());
    services.AddSingleton<SnapshotService>();
    services.AddSingleton<RollbackScriptGenerator>();
    services.AddSingleton<STIGForge.Apply.Dsc.LcmService>(sp =>
      new STIGForge.Apply.Dsc.LcmService(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<STIGForge.Apply.Dsc.LcmService>>(),
        sp.GetRequiredService<IProcessRunner>()));
    services.AddSingleton<STIGForge.Apply.Reboot.RebootCoordinator>();
    services.AddSingleton<STIGForge.Apply.Lgpo.LgpoRunner>();
    services.AddSingleton<STIGForge.Apply.ApplyRunner>();
    services.AddSingleton<STIGForge.Apply.IApplyRunner>(sp => sp.GetRequiredService<STIGForge.Apply.ApplyRunner>());
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
    services.AddSingleton<IEvidenceCompiler>(sp =>
        new EvidenceCompiler(sp.GetRequiredService<ILoggerFactory>().CreateLogger<EvidenceCompiler>()));
    services.AddSingleton<STIGForge.Apply.PowerStig.PowerStigDataGenerator>(sp =>
      new STIGForge.Apply.PowerStig.PowerStigDataGenerator(
        sp.GetRequiredService<ReleaseAgeGate>(),
        sp.GetRequiredService<ClassificationScopeService>()));
    services.AddSingleton<BundleOrchestrator>();
    services.AddSingleton<STIGForge.Export.EmassExporter>();
    services.AddSingleton<IAuditTrailService>(sp =>
      new AuditTrailService(sp.GetRequiredService<DbConnectionString>(), sp.GetRequiredService<IClock>()));
#pragma warning disable CA1416
    services.AddSingleton<ICredentialStore>(sp =>
      new DpapiCredentialStore(sp.GetRequiredService<IPathBuilder>()));
#pragma warning restore CA1416

    services.AddSingleton<ControlFilterService>();

    services.AddSingleton<IComplianceTrendRepository>(sp =>
      new SqliteComplianceTrendRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<ComplianceTrendService>();

    services.AddSingleton<IExceptionRepository>(sp =>
      new SqliteExceptionRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<ExceptionWorkflowService>();

    services.AddSingleton<IReleaseCheckRepository>(sp =>
      new SqliteReleaseCheckRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<StigReleaseMonitorService>();

    services.AddSingleton(sp =>
      new RemediationRunner(
        RemediationHandlerRegistry.CreateHandlers(sp.GetRequiredService<IProcessRunner>())));

    services.AddSingleton<WdacPolicyService>();
    services.AddSingleton<BitLockerService>();
    services.AddSingleton<FirewallRuleService>();
    services.AddSingleton<SecurityFeatureRunner>(sp =>
      new SecurityFeatureRunner(new ISecurityFeatureService[]
      {
        sp.GetRequiredService<WdacPolicyService>(),
        sp.GetRequiredService<BitLockerService>(),
        sp.GetRequiredService<FirewallRuleService>()
      }));
    services.AddSingleton<IDriftRepository>(sp =>
      new SqliteDriftRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<DriftDetectionService>(sp =>
      new DriftDetectionService(
        sp.GetRequiredService<IDriftRepository>(),
        RemediationHandlerRegistry.CreateHandlers(sp.GetRequiredService<IProcessRunner>()),
        sp.GetRequiredService<IClock>()));

    services.AddSingleton<IRollbackRepository>(sp =>
      new SqliteRollbackRepository(sp.GetRequiredService<DbConnectionString>()));
    services.AddSingleton<RollbackService>();

    services.AddSingleton<GpoConflictDetector>();
    services.AddSingleton<NessusImporter>();
    services.AddSingleton<AcasCorrelationService>();
    services.AddSingleton<CklImporter>();
    services.AddSingleton<CklExporter>();
    services.AddSingleton<CklMergeService>();
    services.AddSingleton<EmassPackageGenerator>();
    services.AddSingleton(new ComplianceAgentConfig { BundleRoot = Directory.GetCurrentDirectory() });
    services.AddSingleton<ComplianceAgentFactory>();
    services.AddSingleton<PhaseCCommandService>();
  }
}

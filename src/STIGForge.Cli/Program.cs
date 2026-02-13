using System.CommandLine;
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
using STIGForge.Evidence;
using STIGForge.Cli.Commands;
using STIGForge.Verify;

var rootCmd = new RootCommand("STIGForge CLI (offline-first)");

ImportCommands.Register(rootCmd, BuildHost);
BuildCommands.Register(rootCmd, BuildHost);
VerifyCommands.Register(rootCmd, BuildHost);
DiffRebaseCommands.Register(rootCmd, BuildHost);
BundleCommands.Register(rootCmd, BuildHost);
AuditCommands.Register(rootCmd, BuildHost);
ExportCommands.Register(rootCmd, BuildHost);
ScheduleCommands.Register(rootCmd, BuildHost);
FleetCommands.Register(rootCmd, BuildHost);

return await InvokeWithErrorHandlingAsync(rootCmd, args);

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
      services.AddSingleton<BaselineDiffService>();
      services.AddSingleton<OverlayRebaseService>();
      services.AddSingleton<ManualAnswerService>();
      services.AddSingleton<IBundleMissionSummaryService, BundleMissionSummaryService>();
      services.AddSingleton<EvidenceCollector>();
      services.AddSingleton<BundleOrchestrator>();
      services.AddSingleton<STIGForge.Export.EmassExporter>();
      services.AddSingleton<IAuditTrailService>(sp =>
        new AuditTrailService(sp.GetRequiredService<string>(), sp.GetRequiredService<IClock>()));
      services.AddSingleton<ICredentialStore>(sp =>
        new DpapiCredentialStore(sp.GetRequiredService<IPathBuilder>()));
    })
    .Build();
}

static async Task<int> InvokeWithErrorHandlingAsync(RootCommand command, string[] argv)
{
  try
  {
    return await command.InvokeAsync(argv);
  }
  catch (ArgumentException ex)
  {
    Console.Error.WriteLine($"[CLI-ARG-001] {ex.Message}");
    return 2;
  }
  catch (FileNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (DirectoryNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (Exception ex)
  {
    Console.Error.WriteLine($"[CLI-UNEXPECTED-500] {ex.Message}");
    return 1;
  }
}

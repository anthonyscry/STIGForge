using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal class RollbackCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterRollbackCreate(rootCmd, buildHost);
    RegisterRollbackApply(rootCmd, buildHost);
    RegisterRollbackList(rootCmd, buildHost);
  }

  private static void RegisterRollbackCreate(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("rollback-create", "Create pre-hardening rollback snapshot");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var descOpt = new Option<string>("--description", "Snapshot description") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(descOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var description = ctx.ParseResult.GetValueForOption(descOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<RollbackCommands>>();
      var rollbackService = host.Services.GetRequiredService<RollbackService>();

      try
      {
        var snapshot = await rollbackService.CapturePreHardeningStateAsync(bundle, description, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"Rollback Snapshot Created");
          Console.WriteLine($"  Snapshot ID: {snapshot.SnapshotId}");
          Console.WriteLine($"  Bundle: {snapshot.BundleRoot}");
          Console.WriteLine($"  Description: {snapshot.Description}");
          Console.WriteLine($"  Created At: {snapshot.CreatedAt}");
          Console.WriteLine($"  Registry Keys: {snapshot.RegistryKeys.Count}");
          Console.WriteLine($"  Service States: {snapshot.ServiceStates.Count}");
          Console.WriteLine($"  GPO Settings: {snapshot.GpoSettings.Count}");
          Console.WriteLine($"  Script Path: {snapshot.RollbackScriptPath}");
        }

        ctx.ExitCode = 0;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Rollback create failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterRollbackApply(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("rollback-apply", "Apply rollback snapshot");
    var snapshotIdOpt = new Option<string>("--snapshot-id", "Snapshot ID to apply") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(snapshotIdOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var snapshotId = ctx.ParseResult.GetValueForOption(snapshotIdOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<RollbackCommands>>();
      var rollbackService = host.Services.GetRequiredService<RollbackService>();

      try
      {
        var result = await rollbackService.ExecuteRollbackAsync(snapshotId, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"Rollback Applied: {result.SnapshotId}");
          Console.WriteLine($"  Success: {result.Success}");
          Console.WriteLine($"  Exit Code: {result.ExitCode}");
          Console.WriteLine($"  Started: {result.StartedAt}");
          Console.WriteLine($"  Finished: {result.FinishedAt}");

          if (!string.IsNullOrWhiteSpace(result.Output))
          {
            Console.WriteLine("\n  Output:");
            Console.WriteLine(result.Output);
          }

          if (!string.IsNullOrWhiteSpace(result.Error))
          {
            Console.WriteLine("\n  Errors:");
            Console.WriteLine(result.Error);
          }
        }

        ctx.ExitCode = result.Success ? 0 : 1;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Rollback apply failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterRollbackList(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("rollback-list", "List rollback snapshots");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var limitOpt = new Option<int>("--limit", () => 10, "Max snapshots to show");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(limitOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var limit = ctx.ParseResult.GetValueForOption(limitOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILogger<RollbackCommands>>();
      var rollbackService = host.Services.GetRequiredService<RollbackService>();

      try
      {
        var snapshots = await rollbackService.ListSnapshotsAsync(bundle, limit, ctx.GetCancellationToken()).ConfigureAwait(false);

        if (json)
        {
          Console.WriteLine(JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
          Console.WriteLine($"Rollback Snapshots: {bundle}\n");

          foreach (var snapshot in snapshots)
          {
            Console.WriteLine($"  [{snapshot.CreatedAt:yyyy-MM-dd HH:mm:ss}] {snapshot.SnapshotId}");
            Console.WriteLine($"    Description: {snapshot.Description}");
            Console.WriteLine($"    Registry: {snapshot.RegistryKeys.Count}, Services: {snapshot.ServiceStates.Count}, GPO: {snapshot.GpoSettings.Count}");
            Console.WriteLine();
          }
        }

        ctx.ExitCode = 0;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Rollback list failed");
        Console.Error.WriteLine($"Error: {ex.Message}");
        ctx.ExitCode = 2;
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

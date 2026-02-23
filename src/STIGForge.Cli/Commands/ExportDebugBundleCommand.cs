using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Telemetry;

namespace STIGForge.Cli.Commands;

/// <summary>
/// CLI command for exporting debug bundles containing diagnostic artifacts.
/// </summary>
internal static class ExportDebugBundleCommand
{
  /// <summary>
  /// Registers the export-debug-bundle command with the root command.
  /// </summary>
  /// <param name="rootCmd">The root command to register with.</param>
  /// <param name="buildHost">A function to build the DI host.</param>
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-debug-bundle", "Export diagnostic artifacts to a portable ZIP file for offline support");

    var bundleRootOpt = new Option<string?>(
      "--bundle-root",
      "Path to bundle directory for including bundle-specific artifacts (Apply/Logs, Verify/*.json, etc.)");

    var daysOpt = new Option<int>(
      "--days",
      () => 7,
      "Number of days of application logs to include (default: 7)");

    var reasonOpt = new Option<string?>(
      "--reason",
      "Export reason for manifest documentation");

    cmd.AddOption(bundleRootOpt);
    cmd.AddOption(daysOpt);
    cmd.AddOption(reasonOpt);

    cmd.SetHandler(async (bundleRoot, days, reason) =>
    {
      using var host = buildHost();
      await host.StartAsync();

      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExportDebugBundleCommand");
      var paths = host.Services.GetRequiredService<IPathBuilder>();
      var exporter = new DebugBundleExporter(paths);

      logger.LogInformation(
        "export-debug-bundle started: bundle-root={BundleRoot}, days={Days}, reason={Reason}",
        bundleRoot ?? "(none)",
        days,
        reason ?? "(none)");

      try
      {
        var request = new DebugBundleRequest
        {
          BundleRoot = bundleRoot,
          IncludeDaysOfLogs = days,
          ExportReason = reason
        };

        var result = exporter.ExportBundle(request);

        Console.WriteLine("Debug bundle export:");
        Console.WriteLine($"  Output: {result.OutputPath}");
        Console.WriteLine($"  Files:  {result.FileCount}");
        Console.WriteLine($"  Created: {result.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");

        logger.LogInformation(
          "export-debug-bundle completed: {FileCount} files exported to {OutputPath}",
          result.FileCount,
          result.OutputPath);

        Environment.ExitCode = 0;
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine($"[CLI-DEBUG-001] Failed to create debug bundle: {ex.Message}");
        logger.LogError(ex, "export-debug-bundle failed: {Message}", ex.Message);
        Environment.ExitCode = 1;
      }

      await host.StopAsync();
    }, bundleRootOpt, daysOpt, reasonOpt);

    rootCmd.AddCommand(cmd);
  }
}

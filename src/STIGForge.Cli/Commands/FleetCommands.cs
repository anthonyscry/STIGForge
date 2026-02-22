using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Export;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class FleetCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterFleetApply(rootCmd, buildHost);
    RegisterFleetVerify(rootCmd, buildHost);
    RegisterFleetStatus(rootCmd, buildHost);
    RegisterFleetCollect(rootCmd, buildHost);
    RegisterFleetSummary(rootCmd, buildHost);
    RegisterFleetCredentialSave(rootCmd, buildHost);
    RegisterFleetCredentialList(rootCmd, buildHost);
    RegisterFleetCredentialRemove(rootCmd, buildHost);
  }

  private static void RegisterFleetApply(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-apply", "Apply STIG hardening across multiple machines via WinRM/PSRemoting");
    AddCommonFleetOptions(cmd, out var targetsOpt, out var cliPathOpt, out var bundlePathOpt, out var concurrencyOpt, out var timeoutOpt, out var jsonOpt);
    var modeOpt = new Option<string>("--mode", () => string.Empty, "Apply mode: AuditOnly|Safe|Full");
    cmd.AddOption(modeOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FleetCommands");

      var targets = ParseTargets(ctx.ParseResult.GetValueForOption(targetsOpt) ?? string.Empty);
      var request = new FleetRequest
      {
        Targets = targets,
        Operation = "apply",
        RemoteCliPath = NullIfEmpty(ctx, cliPathOpt),
        RemoteBundleRoot = NullIfEmpty(ctx, bundlePathOpt),
        ApplyMode = NullIfEmpty(ctx, modeOpt),
        MaxConcurrency = ctx.ParseResult.GetValueForOption(concurrencyOpt),
        TimeoutSeconds = ctx.ParseResult.GetValueForOption(timeoutOpt)
      };

      logger.LogInformation("fleet-apply started: {Count} targets, mode={Mode}", targets.Count, request.ApplyMode);
      var credStore = host.Services.GetService<ICredentialStore>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var svc = new FleetService(credStore, audit);
      var result = await svc.ExecuteAsync(request, CancellationToken.None);
      WriteFleetResult(result, ctx.ParseResult.GetValueForOption(jsonOpt), logger);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetVerify(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-verify", "Run verification across multiple machines via WinRM/PSRemoting");
    AddCommonFleetOptions(cmd, out var targetsOpt, out var cliPathOpt, out var bundlePathOpt, out var concurrencyOpt, out var timeoutOpt, out var jsonOpt);
    var scapCmdOpt = new Option<string>("--scap-cmd", () => string.Empty, "SCAP/SCC executable path on remote");
    var scapArgsOpt = new Option<string>("--scap-args", () => string.Empty, "SCAP arguments");
    var evalRootOpt = new Option<string>("--evaluate-stig-root", () => string.Empty, "Evaluate-STIG root on remote");
    cmd.AddOption(scapCmdOpt); cmd.AddOption(scapArgsOpt); cmd.AddOption(evalRootOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FleetCommands");

      var targets = ParseTargets(ctx.ParseResult.GetValueForOption(targetsOpt) ?? string.Empty);
      var request = new FleetRequest
      {
        Targets = targets,
        Operation = "verify",
        RemoteCliPath = NullIfEmpty(ctx, cliPathOpt),
        RemoteBundleRoot = NullIfEmpty(ctx, bundlePathOpt),
        ScapCmd = NullIfEmpty(ctx, scapCmdOpt),
        ScapArgs = NullIfEmpty(ctx, scapArgsOpt),
        EvaluateStigRoot = NullIfEmpty(ctx, evalRootOpt),
        MaxConcurrency = ctx.ParseResult.GetValueForOption(concurrencyOpt),
        TimeoutSeconds = ctx.ParseResult.GetValueForOption(timeoutOpt)
      };

      logger.LogInformation("fleet-verify started: {Count} targets", targets.Count);
      var credStore = host.Services.GetService<ICredentialStore>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var svc = new FleetService(credStore, audit);
      var result = await svc.ExecuteAsync(request, CancellationToken.None);
      WriteFleetResult(result, ctx.ParseResult.GetValueForOption(jsonOpt), logger);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetStatus(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-status", "Check WinRM connectivity for fleet targets");
    var targetsOpt = new Option<string>("--targets", "Comma-separated list of hostnames or host:ip pairs") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", "Output as JSON");
    cmd.AddOption(targetsOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (targets, json) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FleetCommands");
      var targetList = ParseTargets(targets);

      logger.LogInformation("fleet-status: checking {Count} targets", targetList.Count);
      var credStore = host.Services.GetService<ICredentialStore>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var svc = new FleetService(credStore, audit);
      var result = await svc.CheckStatusAsync(targetList, CancellationToken.None);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
      }
      else
      {
        Console.WriteLine($"Fleet status: {result.ReachableCount}/{result.TotalMachines} reachable");
        Console.WriteLine();
        Console.WriteLine($"{"Machine",-30} {"IP",-18} {"Status",-12} {"Message"}");
        Console.WriteLine(new string('-', 90));
        foreach (var s in result.MachineStatuses)
          Console.WriteLine($"{s.MachineName,-30} {(s.IpAddress ?? ""),-18} {(s.IsReachable ? "OK" : "FAIL"),-12} {s.Message}");
      }

      logger.LogInformation("fleet-status completed: {Reachable}/{Total} reachable", result.ReachableCount, result.TotalMachines);
      await host.StopAsync();
    }, targetsOpt, jsonOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetCollect(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-collect", "Collect artifacts from remote fleet hosts via WinRM and generate per-host CKL");
    var targetsOpt = new Option<string>("--targets", "Comma-separated list of hostnames or host:ip pairs") { IsRequired = true };
    var remoteBundleOpt = new Option<string>("--remote-bundle-path", () => @"C:\STIGForge\bundle", "Bundle path on remote machines");
    var outputOpt = new Option<string>("--output", "Local results root directory") { IsRequired = true };
    var concurrencyOpt = new Option<int>("--concurrency", () => 5, "Max concurrent machines");
    var timeoutOpt = new Option<int>("--timeout", () => 600, "Timeout per machine in seconds");
    var jsonOpt = new Option<bool>("--json", "Output as JSON");
    cmd.AddOption(targetsOpt); cmd.AddOption(remoteBundleOpt); cmd.AddOption(outputOpt);
    cmd.AddOption(concurrencyOpt); cmd.AddOption(timeoutOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FleetCommands");

      var targets = ParseTargets(ctx.ParseResult.GetValueForOption(targetsOpt) ?? string.Empty);
      var outputDir = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var remotePath = ctx.ParseResult.GetValueForOption(remoteBundleOpt) ?? @"C:\STIGForge\bundle";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      var request = new FleetCollectionRequest
      {
        Targets = targets,
        RemoteBundleRoot = remotePath,
        LocalResultsRoot = outputDir,
        MaxConcurrency = ctx.ParseResult.GetValueForOption(concurrencyOpt),
        TimeoutSeconds = ctx.ParseResult.GetValueForOption(timeoutOpt)
      };

      logger.LogInformation("fleet-collect started: {Count} targets, output={Output}", targets.Count, outputDir);
      var credStore = host.Services.GetService<ICredentialStore>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var svc = new FleetService(credStore, audit);
      var result = await svc.CollectArtifactsAsync(request, CancellationToken.None);

      // Generate per-host CKL from collected artifacts
      var fleetResultsDir = Path.Combine(outputDir, "fleet_results");
      FleetSummaryService.GeneratePerHostCkl(fleetResultsDir);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
      }
      else
      {
        Console.WriteLine($"Fleet collect: {result.SuccessCount}/{result.TotalHosts} succeeded");
        Console.WriteLine();
        Console.WriteLine($"{"Host",-30} {"Status",-10} {"Files",-10} {"Error"}");
        Console.WriteLine(new string('-', 80));
        foreach (var hr in result.HostResults)
          Console.WriteLine($"{hr.MachineName,-30} {(hr.Success ? "OK" : "FAIL"),-10} {hr.FilesCollected,-10} {Helpers.Truncate(hr.Error, 30)}");
      }

      logger.LogInformation("fleet-collect completed: {Success}/{Total} succeeded",
        result.SuccessCount, result.TotalHosts);
      if (result.FailureCount > 0) Environment.ExitCode = 1;
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetSummary(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-summary", "Generate unified fleet summary from collected per-host results");
    var resultsDirOpt = new Option<string>("--results-dir", "Path to fleet_results directory") { IsRequired = true };
    var outputOpt = new Option<string>("--output", () => string.Empty, "Output directory (defaults to results-dir/../fleet_summary)");
    var systemNameOpt = new Option<string>("--system-name", () => "Fleet", "System name for fleet POA&M");
    var jsonOpt = new Option<bool>("--json", "JSON-only output");
    cmd.AddOption(resultsDirOpt); cmd.AddOption(outputOpt); cmd.AddOption(systemNameOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FleetCommands");

      var resultsDir = ctx.ParseResult.GetValueForOption(resultsDirOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var systemName = ctx.ParseResult.GetValueForOption(systemNameOpt) ?? "Fleet";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      var outputDir = string.IsNullOrWhiteSpace(output)
        ? Path.Combine(Path.GetDirectoryName(resultsDir) ?? resultsDir, "fleet_summary")
        : output;

      logger.LogInformation("fleet-summary started: results={ResultsDir}", resultsDir);

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(resultsDir);
      svc.WriteSummaryFiles(summary, outputDir);

      // Generate fleet POA&M
      var poam = svc.GenerateFleetPoam(resultsDir, systemName);
      var poamPath = Path.Combine(outputDir, "fleet_poam.json");
      File.WriteAllText(poamPath, JsonSerializer.Serialize(poam, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
      }
      else
      {
        Console.WriteLine("Fleet Summary:");
        Console.WriteLine($"  Fleet-wide compliance: {summary.FleetWideCompliance:F1}%");
        Console.WriteLine($"  Hosts: {summary.PerHostStats.Count}");
        Console.WriteLine($"  Failing controls: {summary.FailingControls.Count}");
        Console.WriteLine();
        Console.WriteLine($"{"Host",-30} {"Total",-8} {"Pass",-8} {"Fail",-8} {"Compliance"}");
        Console.WriteLine(new string('-', 70));
        foreach (var h in summary.PerHostStats)
          Console.WriteLine($"{h.HostName,-30} {h.TotalControls,-8} {h.PassCount,-8} {h.FailCount,-8} {h.CompliancePercentage:F1}%");
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine("  " + outputDir);
      }

      logger.LogInformation("fleet-summary completed: {Hosts} hosts, fleet compliance={Compliance:F1}%",
        summary.PerHostStats.Count, summary.FleetWideCompliance);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetCredentialSave(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-credential-save", "Save encrypted credentials for a fleet target (DPAPI)");
    var hostOpt = new Option<string>("--host", "Target hostname") { IsRequired = true };
    var userOpt = new Option<string>("--user", "Username") { IsRequired = true };
    var passOpt = new Option<string>("--password", "Password") { IsRequired = true };
    cmd.AddOption(hostOpt); cmd.AddOption(userOpt); cmd.AddOption(passOpt);

    cmd.SetHandler(async (host, user, password) =>
    {
      using var h = buildHost();
      await h.StartAsync().ConfigureAwait(false);
      var store = h.Services.GetRequiredService<ICredentialStore>();
      store.Save(host, user, password);
      Console.WriteLine($"Credential saved for '{host}'.");
      await h.StopAsync().ConfigureAwait(false);
    }, hostOpt, userOpt, passOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetCredentialList(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-credential-list", "List all stored fleet credentials");

    cmd.SetHandler(async () =>
    {
      using var h = buildHost();
      await h.StartAsync().ConfigureAwait(false);
      var store = h.Services.GetRequiredService<ICredentialStore>();
      var hosts = store.ListHosts();

      if (hosts.Count == 0)
      {
        Console.WriteLine("No stored credentials.");
      }
      else
      {
        Console.WriteLine($"Stored credentials ({hosts.Count}):");
        foreach (var host in hosts)
          Console.WriteLine($"  {host}");
      }
      await h.StopAsync().ConfigureAwait(false);
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterFleetCredentialRemove(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("fleet-credential-remove", "Remove stored credential for a fleet target");
    var hostOpt = new Option<string>("--host", "Target hostname") { IsRequired = true };
    cmd.AddOption(hostOpt);

    cmd.SetHandler(async (host) =>
    {
      using var h = buildHost();
      await h.StartAsync().ConfigureAwait(false);
      var store = h.Services.GetRequiredService<ICredentialStore>();
      var removed = store.Remove(host);
      Console.WriteLine(removed ? $"Credential removed for '{host}'." : $"No credential found for '{host}'.");
      await h.StopAsync().ConfigureAwait(false);
    }, hostOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void AddCommonFleetOptions(Command cmd,
    out Option<string> targetsOpt, out Option<string> cliPathOpt,
    out Option<string> bundlePathOpt, out Option<int> concurrencyOpt,
    out Option<int> timeoutOpt, out Option<bool> jsonOpt)
  {
    targetsOpt = new Option<string>("--targets", "Comma-separated list of hostnames or host:ip pairs") { IsRequired = true };
    cliPathOpt = new Option<string>("--remote-cli-path", () => string.Empty, "STIGForge CLI path on remote machines");
    bundlePathOpt = new Option<string>("--remote-bundle-path", () => string.Empty, "Bundle path on remote machines");
    concurrencyOpt = new Option<int>("--concurrency", () => 5, "Max concurrent machines");
    timeoutOpt = new Option<int>("--timeout", () => 600, "Timeout per machine in seconds");
    jsonOpt = new Option<bool>("--json", "Output as JSON");
    cmd.AddOption(targetsOpt); cmd.AddOption(cliPathOpt); cmd.AddOption(bundlePathOpt);
    cmd.AddOption(concurrencyOpt); cmd.AddOption(timeoutOpt); cmd.AddOption(jsonOpt);
  }

  private static List<FleetTarget> ParseTargets(string targets)
  {
    var list = new List<FleetTarget>();
    foreach (var raw in targets.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
      var item = raw.Trim();
      if (item.Length == 0) continue;

      var colonIdx = item.IndexOf(':');
      if (colonIdx < 0)
      {
        list.Add(new FleetTarget { HostName = item });
        continue;
      }

      if (colonIdx == 0)
        continue;

      list.Add(new FleetTarget
      {
        HostName = item.Substring(0, colonIdx).Trim(),
        IpAddress = item.Substring(colonIdx + 1).Trim()
      });
    }
    return list;
  }

  private static void WriteFleetResult(FleetResult result, bool json, ILogger logger)
  {
    if (json)
    {
      Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
      Console.WriteLine($"Fleet {result.Operation}: {result.SuccessCount}/{result.TotalMachines} succeeded");
      Console.WriteLine($"Duration: {(result.FinishedAt - result.StartedAt).TotalSeconds:F1}s");
      Console.WriteLine();
      Console.WriteLine($"{"Machine",-30} {"Status",-10} {"Exit",-6} {"Duration",-12} {"Error"}");
      Console.WriteLine(new string('-', 100));
      foreach (var m in result.MachineResults)
      {
        var duration = (m.FinishedAt - m.StartedAt).TotalSeconds;
        Console.WriteLine($"{m.MachineName,-30} {(m.Success ? "OK" : "FAIL"),-10} {m.ExitCode,-6} {duration:F1}s{"",-8} {Helpers.Truncate(m.Error, 30)}");
      }
    }

    logger.LogInformation("fleet-{Operation} completed: {Success}/{Total} succeeded in {Duration:F1}s",
      result.Operation, result.SuccessCount, result.TotalMachines, (result.FinishedAt - result.StartedAt).TotalSeconds);

    if (result.FailureCount > 0) Environment.ExitCode = 1;
  }

  private static string? NullIfEmpty(InvocationContext ctx, Option<string> opt)
  {
    var val = ctx.ParseResult.GetValueForOption(opt);
    return string.IsNullOrWhiteSpace(val) ? null : val;
  }
}

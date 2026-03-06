using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class PhaseCExpansionCommands
{
  private const int ExitSuccess = 0;
  private const int ExitFailure = 2;
  private const int ExitActionRequired = 4;

  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterAcasImport(rootCmd, buildHost);
    RegisterNessusImport(rootCmd, buildHost);
    RegisterCklImport(rootCmd, buildHost);
    RegisterCklExport(rootCmd, buildHost);
    RegisterCklMerge(rootCmd, buildHost);
    RegisterEmassPackage(rootCmd, buildHost);
    RegisterAgentInstall(rootCmd, buildHost);
    RegisterAgentUninstall(rootCmd, buildHost);
    RegisterAgentStatus(rootCmd, buildHost);
    RegisterAgentConfig(rootCmd, buildHost);
  }

  private static void RegisterAcasImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("acas-import", "Import ACAS/Nessus XML and correlate findings to bundle controls");
    var fileOpt = new Option<string>("--file", "Path to .nessus XML file") { IsRequired = true };
    var bundleOpt = new Option<string?>("--bundle", () => null, "Optional bundle root for rule correlation");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(bundleOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("acas-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var result = await phaseC.AcasImportAsync(file, bundle, ctx.GetCancellationToken()).ConfigureAwait(false);
        var exitCode = result.UnmatchedCount > 0 ? ExitActionRequired : ExitSuccess;
        var message = result.UnmatchedCount > 0
          ? "Unmatched findings detected; review recommended."
          : "ACAS import and correlation completed.";
        if (json)
          WriteJsonEnvelope("acas-import", true, exitCode, result, message);
        else
        {
          Console.WriteLine($"ACAS import complete: {file}");
          Console.WriteLine($"  Total findings: {result.TotalFindings}");
          Console.WriteLine($"  Correlated: {result.CorrelatedCount}");
          Console.WriteLine($"  Unmatched: {result.UnmatchedCount}");
          Console.WriteLine($"  Mismatches: {result.Mismatches}");
          if (exitCode == ExitActionRequired)
            Console.WriteLine("  Action: unmatched findings require review.");
        }

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "acas-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterNessusImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("nessus-import", "Import Nessus .nessus XML findings");
    var fileOpt = new Option<string>("--file", "Path to .nessus XML file") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("nessus-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var findings = await phaseC.NessusImportAsync(file, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          WriteJsonEnvelope("nessus-import", true, ExitSuccess, findings, "Nessus import completed.");
        else
        {
          Console.WriteLine($"Nessus import complete: {file}");
          Console.WriteLine($"  Findings: {findings.Count}");
          Console.WriteLine($"  Critical/High: {findings.Count(f => f.Severity >= 3)}");
        }

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "nessus-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCklImport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-import", "Import STIG Viewer CKL checklist");
    var fileOpt = new Option<string>("--file", "Path to .ckl file") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(fileOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var file = ctx.ParseResult.GetValueForOption(fileOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-import");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var checklist = await phaseC.CklImportAsync(file, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          WriteJsonEnvelope("ckl-import", true, ExitSuccess, checklist, "CKL import completed.");
        else
        {
          Console.WriteLine($"CKL import complete: {file}");
          Console.WriteLine($"  Asset: {checklist.AssetName}");
          Console.WriteLine($"  STIG: {checklist.StigTitle}");
          Console.WriteLine($"  Findings: {checklist.Findings.Count}");
        }

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "ckl-import", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCklExport(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-export", "Export bundle controls into CKL format");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outputOpt = new Option<string>("--output", "Output CKL path") { IsRequired = true };
    var hostOpt = new Option<string>("--host-name", () => Environment.MachineName, "Host name for CKL asset metadata");
    var stigOpt = new Option<string>("--stig-title", () => "STIG Checklist", "STIG title metadata");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(hostOpt);
    cmd.AddOption(stigOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var hostName = ctx.ParseResult.GetValueForOption(hostOpt) ?? Environment.MachineName;
      var stigTitle = ctx.ParseResult.GetValueForOption(stigOpt) ?? "STIG Checklist";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-export");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var exported = await phaseC.CklExportAsync(bundle, output, hostName, stigTitle, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          WriteJsonEnvelope("ckl-export", true, ExitSuccess, new { bundle, output = exported, hostName, stigTitle }, "CKL export completed.");
        else
          Console.WriteLine($"CKL export complete: {exported}");

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "ckl-export", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCklMerge(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("ckl-merge", "Merge imported CKL with bundle verification results and detect conflicts");
    var cklFileOpt = new Option<string>("--ckl-file", "Path to imported .ckl file") { IsRequired = true };
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var strategyOpt = new Option<string>("--strategy", () => CklConflictResolutionStrategy.MostRecent.ToString(), "Conflict strategy: CklWins|StigForgeWins|MostRecent|Manual");
    var outputOpt = new Option<string?>("--output", () => null, "Optional output path for merged .ckl");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(cklFileOpt);
    cmd.AddOption(bundleOpt);
    cmd.AddOption(strategyOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var cklFile = ctx.ParseResult.GetValueForOption(cklFileOpt) ?? string.Empty;
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var strategyText = ctx.ParseResult.GetValueForOption(strategyOpt) ?? CklConflictResolutionStrategy.MostRecent.ToString();
      var output = ctx.ParseResult.GetValueForOption(outputOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ckl-merge");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();
      var cklExporter = host.Services.GetRequiredService<CklExporter>();

      try
      {
        var strategy = ParseCklMergeStrategy(strategyText);
        var result = await phaseC.CklMergeAsync(cklFile, bundle, strategy, ctx.GetCancellationToken()).ConfigureAwait(false);

        string? exportPath = null;
        if (!string.IsNullOrWhiteSpace(output))
        {
          exportPath = output;
          var outputDirectory = Path.GetDirectoryName(output);
          if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
          cklExporter.Export(result.MergedChecklist, output);
        }

        var exitCode = strategy == CklConflictResolutionStrategy.Manual && result.Conflicts.Count > 0
          ? ExitActionRequired
          : ExitSuccess;

        if (json)
        {
          WriteJsonEnvelope("ckl-merge", true, exitCode,
            new
            {
              strategy = result.Strategy,
              mergedFindings = result.MergedFindings.Count,
              conflicts = result.Conflicts.Count,
              manualResolutionRequired = exitCode == ExitActionRequired,
              output = exportPath,
              conflictDetails = result.Conflicts
            },
            exitCode == ExitActionRequired
              ? "Merge completed with unresolved manual conflicts."
              : "CKL merge completed.");
        }
        else
        {
          Console.WriteLine($"CKL merge complete: {cklFile}");
          Console.WriteLine($"  Strategy: {result.Strategy}");
          Console.WriteLine($"  Merged findings: {result.MergedFindings.Count}");
          Console.WriteLine($"  Conflicts: {result.Conflicts.Count}");
          if (!string.IsNullOrWhiteSpace(exportPath))
            Console.WriteLine($"  Output: {exportPath}");
          if (exitCode == ExitActionRequired)
            Console.WriteLine("  Action: resolve conflicts manually or rerun with a non-manual strategy.");
        }

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "ckl-merge", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterEmassPackage(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("emass-package", "Generate eMASS package from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var systemNameOpt = new Option<string>("--system-name", "System name") { IsRequired = true };
    var acronymOpt = new Option<string>("--system-acronym", "System acronym") { IsRequired = true };
    var outputOpt = new Option<string>("--output", "Output directory") { IsRequired = true };
    var previousOpt = new Option<string?>("--previous-package", () => null, "Previous package directory for change log diff");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(systemNameOpt);
    cmd.AddOption(acronymOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(previousOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var systemName = ctx.ParseResult.GetValueForOption(systemNameOpt) ?? string.Empty;
      var acronym = ctx.ParseResult.GetValueForOption(acronymOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var previous = ctx.ParseResult.GetValueForOption(previousOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("emass-package");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();

      try
      {
        var package = await phaseC.EmassPackageAsync(bundle, systemName, acronym, output, previous, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
        {
          WriteJsonEnvelope("emass-package", true, ExitSuccess,
            new
            {
              package.PackageId,
              package.SystemName,
              package.SystemAcronym,
              ControlCount = package.ControlCorrelationMatrix.Controls.Count,
              PoamEntryCount = package.Poam.Entries.Count,
              output
            },
            "eMASS package generation completed.");
        }
        else
        {
          Console.WriteLine($"eMASS package generated: {package.PackageId}");
          Console.WriteLine($"  System: {package.SystemName} ({package.SystemAcronym})");
          Console.WriteLine($"  Controls: {package.ControlCorrelationMatrix.Controls.Count}");
          Console.WriteLine($"  POA&M entries: {package.Poam.Entries.Count}");
        }

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "emass-package", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterAgentInstall(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("agent-install", "Install continuous compliance Windows service");
    var serviceNameOpt = new Option<string>("--service-name", () => "STIGForgeComplianceAgent", "Windows service name");
    var displayNameOpt = new Option<string>("--display-name", () => "STIGForge Continuous Compliance Agent", "Windows service display name");
    var executableOpt = new Option<string?>("--executable", () => null, "Executable path (default: current process)");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(serviceNameOpt);
    cmd.AddOption(displayNameOpt);
    cmd.AddOption(executableOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var serviceName = ctx.ParseResult.GetValueForOption(serviceNameOpt) ?? "STIGForgeComplianceAgent";
      var displayName = ctx.ParseResult.GetValueForOption(displayNameOpt) ?? "STIGForge Continuous Compliance Agent";
      var executable = ctx.ParseResult.GetValueForOption(executableOpt) ?? Environment.ProcessPath ?? "STIGForge.Cli.exe";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("agent-install");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();
      try
      {
        await phaseC.AgentInstallAsync(serviceName, displayName, executable, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          WriteJsonEnvelope("agent-install", true, ExitSuccess, new { serviceName, displayName, executable }, "Agent install completed.");
        else
          Console.WriteLine($"Installed service '{serviceName}' using '{executable}'.");

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "agent-install", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterAgentUninstall(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("agent-uninstall", "Uninstall continuous compliance Windows service");
    var serviceNameOpt = new Option<string>("--service-name", () => "STIGForgeComplianceAgent", "Windows service name");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(serviceNameOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var serviceName = ctx.ParseResult.GetValueForOption(serviceNameOpt) ?? "STIGForgeComplianceAgent";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);
      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("agent-uninstall");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();
      try
      {
        await phaseC.AgentUninstallAsync(serviceName, ctx.GetCancellationToken()).ConfigureAwait(false);
        if (json)
          WriteJsonEnvelope("agent-uninstall", true, ExitSuccess, new { serviceName }, "Agent uninstall completed.");
        else
          Console.WriteLine($"Uninstalled service '{serviceName}'.");

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "agent-uninstall", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterAgentStatus(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("agent-status", "Query continuous compliance Windows service status");
    var serviceNameOpt = new Option<string>("--service-name", () => "STIGForgeComplianceAgent", "Windows service name");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");
    cmd.AddOption(serviceNameOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var serviceName = ctx.ParseResult.GetValueForOption(serviceNameOpt) ?? "STIGForgeComplianceAgent";
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);
      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("agent-status");
      var phaseC = host.Services.GetRequiredService<PhaseCCommandService>();
      try
      {
        var status = await phaseC.AgentStatusAsync(serviceName, ctx.GetCancellationToken()).ConfigureAwait(false);
        var hasStatus = !string.IsNullOrWhiteSpace(status);
        var exitCode = hasStatus ? ExitSuccess : ExitActionRequired;
        if (json)
          WriteJsonEnvelope("agent-status", hasStatus, exitCode, new { serviceName, status }, hasStatus ? "Agent status retrieved." : "Service status unavailable.");
        else
          Console.WriteLine(status);

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "agent-status", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterAgentConfig(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("agent-config", "Manage continuous compliance agent JSON configuration");
    var configPathOpt = new Option<string>("--config", () => "agent-config.json", "Path to agent config file");
    var initOpt = new Option<bool>("--init", () => false, "Initialize config file with defaults");
    var showOpt = new Option<bool>("--show", () => false, "Show current configuration");
    var bundleRootOpt = new Option<string?>("--bundle-root", () => null, "Set bundle root path");
    var intervalOpt = new Option<int?>("--interval", () => null, "Set check interval in minutes");
    var autoRemediateOpt = new Option<bool?>("--auto-remediate", () => null, "Set auto-remediation behavior");
    var auditForwardingOpt = new Option<bool?>("--enable-audit-forwarding", () => null, "Set audit forwarding behavior");
    var maxForwardOpt = new Option<int?>("--max-drift-events-to-forward", () => null, "Set max drift events forwarded each run");
    var jsonOpt = new Option<bool>("--json", () => false, "JSON output");

    cmd.AddOption(configPathOpt);
    cmd.AddOption(initOpt);
    cmd.AddOption(showOpt);
    cmd.AddOption(bundleRootOpt);
    cmd.AddOption(intervalOpt);
    cmd.AddOption(autoRemediateOpt);
    cmd.AddOption(auditForwardingOpt);
    cmd.AddOption(maxForwardOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var configPath = ctx.ParseResult.GetValueForOption(configPathOpt) ?? "agent-config.json";
      var initialize = ctx.ParseResult.GetValueForOption(initOpt);
      var show = ctx.ParseResult.GetValueForOption(showOpt);
      var bundleRoot = ctx.ParseResult.GetValueForOption(bundleRootOpt);
      var interval = ctx.ParseResult.GetValueForOption(intervalOpt);
      var autoRemediate = ctx.ParseResult.GetValueForOption(autoRemediateOpt);
      var enableAuditForwarding = ctx.ParseResult.GetValueForOption(auditForwardingOpt);
      var maxForward = ctx.ParseResult.GetValueForOption(maxForwardOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("agent-config");

      try
      {
        if (initialize)
        {
          var defaultConfig = new ComplianceAgentConfig
          {
            BundleRoot = bundleRoot ?? Environment.CurrentDirectory
          };

          await ComplianceAgentConfig.SaveToFileAsync(defaultConfig, configPath).ConfigureAwait(false);

          if (json)
            WriteJsonEnvelope("agent-config", true, ExitSuccess, new { configPath, config = defaultConfig }, "Agent config initialized.");
          else
            Console.WriteLine($"Initialized agent config: {configPath}");
        }

        var hasUpdates = !string.IsNullOrWhiteSpace(bundleRoot)
          || interval.HasValue
          || autoRemediate.HasValue
          || enableAuditForwarding.HasValue
          || maxForward.HasValue;

        if (hasUpdates)
        {
          var config = File.Exists(configPath)
            ? await ComplianceAgentConfig.LoadFromFileAsync(configPath).ConfigureAwait(false)
            : new ComplianceAgentConfig { BundleRoot = bundleRoot ?? Environment.CurrentDirectory };

          if (!string.IsNullOrWhiteSpace(bundleRoot))
            config.BundleRoot = bundleRoot;
          if (interval.HasValue)
            config.CheckIntervalMinutes = interval.Value;
          if (autoRemediate.HasValue)
            config.AutoRemediate = autoRemediate.Value;
          if (enableAuditForwarding.HasValue)
            config.EnableAuditForwarding = enableAuditForwarding.Value;
          if (maxForward.HasValue)
            config.MaxDriftEventsToForward = maxForward.Value;

          await ComplianceAgentConfig.SaveToFileAsync(config, configPath).ConfigureAwait(false);

          if (json)
            WriteJsonEnvelope("agent-config", true, ExitSuccess, new { configPath, config }, "Agent config updated.");
          else
            Console.WriteLine($"Updated agent config: {configPath}");
        }

        if (show || (!initialize && !hasUpdates))
        {
          var config = await ComplianceAgentConfig.LoadFromFileAsync(configPath).ConfigureAwait(false);

          if (json)
            WriteJsonEnvelope("agent-config", true, ExitSuccess, new { configPath, config }, "Agent config loaded.");
          else
          {
            Console.WriteLine($"Config: {configPath}");
            Console.WriteLine($"  BundleRoot: {config.BundleRoot}");
            Console.WriteLine($"  CheckIntervalMinutes: {config.CheckIntervalMinutes}");
            Console.WriteLine($"  AutoRemediate: {config.AutoRemediate}");
            Console.WriteLine($"  EnableAuditForwarding: {config.EnableAuditForwarding}");
            Console.WriteLine($"  MaxDriftEventsToForward: {config.MaxDriftEventsToForward}");
          }
        }

        ctx.ExitCode = ExitSuccess;
      }
      catch (Exception ex)
      {
        HandleCommandFailure(ctx, logger, "agent-config", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void HandleCommandFailure(InvocationContext ctx, ILogger logger, string command, Exception ex, bool json)
  {
    logger.LogError(ex, "{Command} failed", command);
    if (json)
    {
      WriteJsonEnvelope(command, false, ExitFailure, new { error = ex.Message }, "Command failed.");
    }
    else
    {
      Console.Error.WriteLine($"Error: {ex.Message}");
    }

    ctx.ExitCode = ExitFailure;
  }

  private static void WriteJsonEnvelope(string command, bool success, int exitCode, object data, string message)
  {
    var envelope = new CommandEnvelope
    {
      Command = command,
      Success = success,
      ExitCode = exitCode,
      Message = message,
      TimestampUtc = DateTimeOffset.UtcNow,
      Data = data
    };

    Console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions.Indented));
  }

  private static CklConflictResolutionStrategy ParseCklMergeStrategy(string value)
  {
    if (Enum.TryParse<CklConflictResolutionStrategy>(value, ignoreCase: true, out var parsed))
      return parsed;

    throw new ArgumentException("Invalid --strategy value. Allowed: CklWins, StigForgeWins, MostRecent, Manual.");
  }

  private sealed class CommandEnvelope
  {
    public string Command { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public object Data { get; set; } = new();
  }
}

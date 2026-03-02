using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class PhaseCExpansionCommands
{
  private const int ExitSuccess = 0;
  private const int ExitFailure = 2;
  private const int ExitActionRequired = 4;
  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterAcasImport(rootCmd, buildHost);
    RegisterNessusImport(rootCmd, buildHost);
    RegisterCklImport(rootCmd, buildHost);
    RegisterCklExport(rootCmd, buildHost);
    RegisterEmassPackage(rootCmd, buildHost);
    RegisterAgentInstall(rootCmd, buildHost);
    RegisterAgentUninstall(rootCmd, buildHost);
    RegisterAgentStatus(rootCmd, buildHost);
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

    Console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
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

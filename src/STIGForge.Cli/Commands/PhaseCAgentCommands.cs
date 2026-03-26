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

internal static class PhaseCAgentCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterAgentInstall(rootCmd, buildHost);
    RegisterAgentUninstall(rootCmd, buildHost);
    RegisterAgentStatus(rootCmd, buildHost);
    RegisterAgentConfig(rootCmd, buildHost);
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
          PhaseCExpansionCommands.WriteJsonEnvelope("agent-install", true, PhaseCExpansionCommands.ExitSuccess, new { serviceName, displayName, executable }, "Agent install completed.");
        else
          Console.WriteLine($"Installed service '{serviceName}' using '{executable}'.");

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "agent-install", ex, json);
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
          PhaseCExpansionCommands.WriteJsonEnvelope("agent-uninstall", true, PhaseCExpansionCommands.ExitSuccess, new { serviceName }, "Agent uninstall completed.");
        else
          Console.WriteLine($"Uninstalled service '{serviceName}'.");

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "agent-uninstall", ex, json);
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
        var exitCode = hasStatus ? PhaseCExpansionCommands.ExitSuccess : PhaseCExpansionCommands.ExitActionRequired;
        if (json)
          PhaseCExpansionCommands.WriteJsonEnvelope("agent-status", hasStatus, exitCode, new { serviceName, status }, hasStatus ? "Agent status retrieved." : "Service status unavailable.");
        else
          Console.WriteLine(status);

        ctx.ExitCode = exitCode;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "agent-status", ex, json);
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
            PhaseCExpansionCommands.WriteJsonEnvelope("agent-config", true, PhaseCExpansionCommands.ExitSuccess, new { configPath, config = defaultConfig }, "Agent config initialized.");
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
            PhaseCExpansionCommands.WriteJsonEnvelope("agent-config", true, PhaseCExpansionCommands.ExitSuccess, new { configPath, config }, "Agent config updated.");
          else
            Console.WriteLine($"Updated agent config: {configPath}");
        }

        if (show || (!initialize && !hasUpdates))
        {
          var config = await ComplianceAgentConfig.LoadFromFileAsync(configPath).ConfigureAwait(false);

          if (json)
            PhaseCExpansionCommands.WriteJsonEnvelope("agent-config", true, PhaseCExpansionCommands.ExitSuccess, new { configPath, config }, "Agent config loaded.");
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

        ctx.ExitCode = PhaseCExpansionCommands.ExitSuccess;
      }
      catch (Exception ex)
      {
        PhaseCExpansionCommands.HandleCommandFailure(ctx, logger, "agent-config", ex, json);
      }
    });

    rootCmd.AddCommand(cmd);
  }
}

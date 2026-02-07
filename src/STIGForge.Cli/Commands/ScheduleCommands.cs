using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class ScheduleCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterScheduleVerify(rootCmd, buildHost);
    RegisterScheduleRemove(rootCmd, buildHost);
    RegisterScheduleList(rootCmd, buildHost);
  }

  private static void RegisterScheduleVerify(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("schedule-verify", "Register a scheduled re-verification task in Windows Task Scheduler");
    var nameOpt = new Option<string>("--name", "Task name") { IsRequired = true };
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var freqOpt = new Option<string>("--frequency", () => "DAILY", "Schedule frequency: DAILY|WEEKLY|MONTHLY|ONCE");
    var timeOpt = new Option<string>("--time", () => "06:00", "Start time (HH:mm)");
    var daysOpt = new Option<string>("--days", () => string.Empty, "Days of week for WEEKLY (e.g., MON,WED,FRI)");
    var intervalOpt = new Option<int>("--interval", () => 1, "Interval in days for DAILY");
    var typeOpt = new Option<string>("--verify-type", () => string.Empty, "Verify type: scap|evaluate-stig|orchestrate (default: orchestrate)");
    var scapCmdOpt = new Option<string>("--scap-cmd", () => string.Empty, "SCAP/SCC executable path");
    var scapArgsOpt = new Option<string>("--scap-args", () => string.Empty, "SCAP arguments");
    var evalRootOpt = new Option<string>("--evaluate-stig-root", () => string.Empty, "Evaluate-STIG root folder");
    var evalArgsOpt = new Option<string>("--evaluate-stig-args", () => string.Empty, "Evaluate-STIG arguments");
    var outOpt = new Option<string>("--output-root", () => string.Empty, "Output root for scan results");
    var cliOpt = new Option<string>("--cli-path", () => string.Empty, "CLI executable path override");

    foreach (var o in new Option[] { nameOpt, bundleOpt, freqOpt, timeOpt, daysOpt, intervalOpt, typeOpt, scapCmdOpt, scapArgsOpt, evalRootOpt, evalArgsOpt, outOpt, cliOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var name = ctx.ParseResult.GetValueForOption(nameOpt) ?? string.Empty;
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var freq = ctx.ParseResult.GetValueForOption(freqOpt) ?? "DAILY";
      var time = ctx.ParseResult.GetValueForOption(timeOpt) ?? "06:00";
      var days = ctx.ParseResult.GetValueForOption(daysOpt) ?? string.Empty;
      var interval = ctx.ParseResult.GetValueForOption(intervalOpt);
      var verifyType = ctx.ParseResult.GetValueForOption(typeOpt) ?? string.Empty;
      var scapCmd = ctx.ParseResult.GetValueForOption(scapCmdOpt) ?? string.Empty;
      var scapArgs = ctx.ParseResult.GetValueForOption(scapArgsOpt) ?? string.Empty;
      var evalRoot = ctx.ParseResult.GetValueForOption(evalRootOpt) ?? string.Empty;
      var evalArgs = ctx.ParseResult.GetValueForOption(evalArgsOpt) ?? string.Empty;
      var outputRoot = ctx.ParseResult.GetValueForOption(outOpt) ?? string.Empty;
      var cliPath = ctx.ParseResult.GetValueForOption(cliOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScheduleCommands");
      logger.LogInformation("schedule-verify started: name={Name}, bundle={Bundle}, frequency={Frequency}", name, bundle, freq);

      var svc = new ScheduledTaskService();
      var result = svc.Register(new ScheduledTaskRequest
      {
        TaskName = name,
        BundleRoot = bundle,
        Frequency = freq,
        StartTime = time,
        DaysOfWeek = string.IsNullOrWhiteSpace(days) ? null : days,
        IntervalDays = interval,
        VerifyType = string.IsNullOrWhiteSpace(verifyType) ? null : verifyType,
        ScapCmd = string.IsNullOrWhiteSpace(scapCmd) ? null : scapCmd,
        ScapArgs = string.IsNullOrWhiteSpace(scapArgs) ? null : scapArgs,
        EvaluateStigRoot = string.IsNullOrWhiteSpace(evalRoot) ? null : evalRoot,
        EvaluateStigArgs = string.IsNullOrWhiteSpace(evalArgs) ? null : evalArgs,
        OutputRoot = string.IsNullOrWhiteSpace(outputRoot) ? null : outputRoot,
        CliPath = string.IsNullOrWhiteSpace(cliPath) ? null : cliPath
      });

      if (result.Success)
      {
        Console.WriteLine("Scheduled task registered:");
        Console.WriteLine("  Task: " + result.TaskName);
        Console.WriteLine("  Script: " + result.ScriptPath);
        logger.LogInformation("schedule-verify completed: task={TaskName}", result.TaskName);
      }
      else
      {
        Console.Error.WriteLine("Failed to register task: " + result.Message);
        logger.LogError("schedule-verify failed: {Message}", result.Message);
        Environment.ExitCode = 1;
      }
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterScheduleRemove(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("schedule-remove", "Remove a scheduled re-verification task");
    var nameOpt = new Option<string>("--name", "Task name to remove") { IsRequired = true };
    cmd.AddOption(nameOpt);

    cmd.SetHandler(async (name) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScheduleCommands");
      logger.LogInformation("schedule-remove: name={Name}", name);

      var svc = new ScheduledTaskService();
      var result = svc.Unregister(name);

      Console.WriteLine(result.Success ? "Task removed: " + result.TaskName : "Failed: " + result.Message);
      if (!result.Success) Environment.ExitCode = 1;
      await host.StopAsync();
    }, nameOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterScheduleList(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("schedule-list", "List scheduled STIGForge verification tasks");

    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScheduleCommands");
      logger.LogInformation("schedule-list");

      var svc = new ScheduledTaskService();
      var result = svc.List();
      Console.WriteLine(result.Message);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}

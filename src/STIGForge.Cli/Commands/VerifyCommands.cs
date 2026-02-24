using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Workflow;
using STIGForge.Verify;

namespace STIGForge.Cli.Commands;

internal static class VerifyCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterVerifyEvaluateStig(rootCmd, buildHost);
    RegisterVerifyScap(rootCmd, buildHost);
    RegisterCoverageOverlap(rootCmd);
    // NOTE: export-emass moved to ExportCommands.cs to avoid duplicate registration
  }

  private static void RegisterVerifyEvaluateStig(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("verify-evaluate-stig", "Run Evaluate-STIG.ps1 with provided arguments");
    var toolRootOpt = new Option<string>("--tool-root", () => Path.GetFullPath(".\\.stigforge\\tools\\Evaluate-STIG\\Evaluate-STIG"), "Root folder containing Evaluate-STIG.ps1");
    var argsOpt = new Option<string>("--args", () => string.Empty, "Arguments passed to Evaluate-STIG.ps1");
    var workDirOpt = new Option<string>("--workdir", () => string.Empty, "Working directory for the script");
    var logOpt = new Option<string>("--log", () => string.Empty, "Optional log file path");
    var outputRootOpt = new Option<string>("--output-root", () => string.Empty, "Folder to scan for generated CKL files");

    cmd.AddOption(toolRootOpt); cmd.AddOption(argsOpt); cmd.AddOption(workDirOpt);
    cmd.AddOption(logOpt); cmd.AddOption(outputRootOpt);

    cmd.SetHandler(async (toolRoot, args, workDir, logPath, outputRoot) =>
    {
      var workflowResult = await RunWorkflowAsync(
        buildHost,
        outputRoot,
        services => services.GetRequiredService<LocalSetupValidator>().ValidateRequiredTools(toolRoot),
        request =>
        {
          request.ConsolidatedToolLabel = "Evaluate-STIG";
          request.EvaluateStig = new EvaluateStigWorkflowOptions
          {
            Enabled = true,
            ToolRoot = toolRoot,
            Arguments = args ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir
          };
        });

      var evalRun = workflowResult.ToolRuns.FirstOrDefault(r => r.Tool.IndexOf("Evaluate", StringComparison.OrdinalIgnoreCase) >= 0);
      if (evalRun != null)
      {
        if (!string.IsNullOrWhiteSpace(evalRun.Output)) Console.WriteLine(evalRun.Output);
        if (!string.IsNullOrWhiteSpace(evalRun.Error)) Console.Error.WriteLine(evalRun.Error);

        if (!string.IsNullOrWhiteSpace(logPath))
          File.WriteAllText(logPath, evalRun.Output + Environment.NewLine + evalRun.Error);

        Environment.ExitCode = evalRun.ExitCode;
      }

      PrintConsolidatedOutput(outputRoot, workflowResult);
    }, toolRootOpt, argsOpt, workDirOpt, logOpt, outputRootOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterVerifyScap(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("verify-scap", "Run a SCAP tool and consolidate CKL results");
    var exeOpt = new Option<string>("--cmd", "Path to SCAP/SCC executable") { IsRequired = true };
    var argsOpt = new Option<string>("--args", () => string.Empty, "Arguments passed to SCAP tool");
    var workOpt = new Option<string>("--workdir", () => string.Empty, "Working directory");
    var outOpt = new Option<string>("--output-root", () => string.Empty, "Folder to scan for generated CKL files");
    var toolOpt = new Option<string>("--tool", () => "SCAP", "Tool label used in reports");
    var logOpt = new Option<string>("--log", () => string.Empty, "Optional log file path");

    cmd.AddOption(exeOpt); cmd.AddOption(argsOpt); cmd.AddOption(workOpt);
    cmd.AddOption(outOpt); cmd.AddOption(toolOpt); cmd.AddOption(logOpt);

    cmd.SetHandler(async (exe, args, workDir, outputRoot, toolName, logPath) =>
    {
      var workflowResult = await RunWorkflowAsync(
        buildHost,
        outputRoot,
        null,
        request =>
        {
          request.ConsolidatedToolLabel = string.IsNullOrWhiteSpace(toolName) ? "SCAP" : toolName;
          request.Scap = new ScapWorkflowOptions
          {
            Enabled = true,
            CommandPath = exe,
            Arguments = args ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            ToolLabel = string.IsNullOrWhiteSpace(toolName) ? "SCAP" : toolName
          };
        });

      var scapRun = workflowResult.ToolRuns.FirstOrDefault(r => r.Tool.IndexOf("SCAP", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(r.Tool, toolName, StringComparison.OrdinalIgnoreCase));
      if (scapRun != null)
      {
        if (!string.IsNullOrWhiteSpace(scapRun.Output)) Console.WriteLine(scapRun.Output);
        if (!string.IsNullOrWhiteSpace(scapRun.Error)) Console.Error.WriteLine(scapRun.Error);

        if (!string.IsNullOrWhiteSpace(logPath))
          File.WriteAllText(logPath, scapRun.Output + Environment.NewLine + scapRun.Error);

        Environment.ExitCode = scapRun.ExitCode;
      }

      PrintConsolidatedOutput(outputRoot, workflowResult);
    }, exeOpt, argsOpt, workOpt, outOpt, toolOpt, logOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterCoverageOverlap(RootCommand rootCmd)
  {
    var cmd = new Command("coverage-overlap", "Build coverage overlap summary from consolidated results");
    var inputsOpt = new Option<string>("--inputs", "Semicolon-delimited inputs (Label|Path or Path)") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Output folder for summary files");
    cmd.AddOption(inputsOpt); cmd.AddOption(outOpt);

    cmd.SetHandler((inputs, output) =>
    {
      var outputRoot = string.IsNullOrWhiteSpace(output) ? Environment.CurrentDirectory : output;
      Directory.CreateDirectory(outputRoot);

      var allResults = new List<ControlResult>();
      foreach (var raw in inputs.Split(';', StringSplitOptions.RemoveEmptyEntries))
      {
        var item = raw.Trim();
        if (item.Length == 0) continue;
        string label = string.Empty, path = item;
        var pipeIdx = item.IndexOf('|');
        if (pipeIdx >= 0)
        {
          if (pipeIdx == 0)
            continue;

          label = item.Substring(0, pipeIdx).Trim();
          path = item.Substring(pipeIdx + 1).Trim();
        }

        var resolved = Helpers.ResolveReportPath(path);
        var report = VerifyReportReader.LoadFromJson(resolved);
        if (!string.IsNullOrWhiteSpace(label)) report.Tool = label;
        foreach (var r in report.Results)
          if (string.IsNullOrWhiteSpace(r.Tool)) r.Tool = report.Tool;
        allResults.AddRange(report.Results);
      }

      var coverage = VerifyReportWriter.BuildCoverageSummary(allResults);
      VerifyReportWriter.WriteCoverageSummary(Path.Combine(outputRoot, "coverage_by_tool.csv"), Path.Combine(outputRoot, "coverage_by_tool.json"), coverage);

      var maps = VerifyReportWriter.BuildControlSourceMap(allResults);
      VerifyReportWriter.WriteControlSourceMap(Path.Combine(outputRoot, "control_sources.csv"), maps);

      var overlaps = VerifyReportWriter.BuildOverlapSummary(allResults);
      VerifyReportWriter.WriteOverlapSummary(Path.Combine(outputRoot, "coverage_overlap.csv"), Path.Combine(outputRoot, "coverage_overlap.json"), overlaps);

      Console.WriteLine("Wrote overlap summaries to: " + outputRoot);
    }, inputsOpt, outOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExportEmass(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-emass", "Export eMASS submission package from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Optional export root override");
    cmd.AddOption(bundleOpt); cmd.AddOption(outOpt);

    cmd.SetHandler(async (bundle, output) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("VerifyCommands");
      logger.LogInformation("export-emass started: bundle={Bundle}", bundle);
      var exporter = host.Services.GetRequiredService<STIGForge.Export.EmassExporter>();
      var result = await exporter.ExportAsync(new STIGForge.Export.ExportRequest
      {
        BundleRoot = bundle, OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output
      }, CancellationToken.None);

      Console.WriteLine("Exported eMASS package:");
      Console.WriteLine("  " + result.OutputRoot);
      Console.WriteLine("  " + result.ManifestPath);
      if (!string.IsNullOrWhiteSpace(result.ValidationReportPath))
        Console.WriteLine("  Validation report (text): " + result.ValidationReportPath);
      if (!string.IsNullOrWhiteSpace(result.ValidationReportJsonPath))
        Console.WriteLine("  Validation report (json): " + result.ValidationReportJsonPath);

      if (result.ValidationResult != null)
      {
        Console.WriteLine();
        Console.WriteLine($"Package validation: {(result.ValidationResult.IsValid ? "VALID" : "INVALID")}");
        Console.WriteLine($"  Errors: {result.ValidationResult.Errors.Count}");
        Console.WriteLine($"  Warnings: {result.ValidationResult.Warnings.Count}");
        Console.WriteLine($"  Indexed controls: {result.ValidationResult.Metrics.IndexedControlCount}");
        Console.WriteLine($"  Cross-artifact mismatches: {result.ValidationResult.Metrics.CrossArtifactMismatchCount}");
        foreach (var error in result.ValidationResult.Errors) Console.WriteLine($"  Error: {error}");
        foreach (var warning in result.ValidationResult.Warnings) Console.WriteLine($"  Warning: {warning}");
        if (result.ValidationResult.IsValid)
        {
          Console.WriteLine("  Package is ready for eMASS submission!");
          Environment.ExitCode = 0;
        }
        else
        {
          Console.Error.WriteLine("  Submission readiness BLOCKED. Resolve validation errors before mission completion.");
          Environment.ExitCode = 1;
        }
      }
      logger.LogInformation(
        "export-emass completed: outputRoot={OutputRoot}, valid={IsValid}, errors={Errors}, warnings={Warnings}",
        result.OutputRoot,
        result.ValidationResult?.IsValid,
        result.ValidationResult?.Errors.Count,
        result.ValidationResult?.Warnings.Count);
      await host.StopAsync();
    }, bundleOpt, outOpt);

    rootCmd.AddCommand(cmd);
  }

  private static async Task<VerificationWorkflowResult> RunWorkflowAsync(
    Func<IHost> buildHost,
    string? outputRoot,
    Action<IServiceProvider>? preflight,
    Action<VerificationWorkflowRequest> configure)
  {
    using var host = buildHost();
    await host.StartAsync();
    preflight?.Invoke(host.Services);

    var useRequestedRoot = !string.IsNullOrWhiteSpace(outputRoot) && Directory.Exists(outputRoot);
    var tempRoot = string.Empty;
    var workflowRoot = outputRoot ?? string.Empty;

    if (!useRequestedRoot)
    {
      tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-verify-cli-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempRoot);
      workflowRoot = tempRoot;
    }

    var request = new VerificationWorkflowRequest
    {
      OutputRoot = workflowRoot
    };
    configure(request);

    var service = host.Services.GetRequiredService<IVerificationWorkflowService>();
    var result = await service.RunAsync(request, CancellationToken.None);

    if (!string.IsNullOrWhiteSpace(tempRoot))
    {
      try { Directory.Delete(tempRoot, true); }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Temp cleanup failed: " + ex.Message);
      }
    }

    await host.StopAsync();
    return result;
  }

  private static void PrintConsolidatedOutput(string? outputRoot, VerificationWorkflowResult workflowResult)
  {
    if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
      return;

    Console.WriteLine("Wrote consolidated results:");
    Console.WriteLine("  " + workflowResult.ConsolidatedJsonPath);
    Console.WriteLine("  " + workflowResult.ConsolidatedCsvPath);
  }
}

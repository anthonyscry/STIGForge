using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STIGForge.Verify;

namespace STIGForge.Cli.Commands;

internal static class VerifyCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterVerifyEvaluateStig(rootCmd);
    RegisterVerifyScap(rootCmd);
    RegisterCoverageOverlap(rootCmd);
    RegisterExportEmass(rootCmd, buildHost);
  }

  private static void RegisterVerifyEvaluateStig(RootCommand rootCmd)
  {
    var cmd = new Command("verify-evaluate-stig", "Run Evaluate-STIG.ps1 with provided arguments");
    var toolRootOpt = new Option<string>("--tool-root", () => Path.GetFullPath(".\\.stigforge\\tools\\Evaluate-STIG\\Evaluate-STIG"), "Root folder containing Evaluate-STIG.ps1");
    var argsOpt = new Option<string>("--args", () => string.Empty, "Arguments passed to Evaluate-STIG.ps1");
    var workDirOpt = new Option<string>("--workdir", () => string.Empty, "Working directory for the script");
    var logOpt = new Option<string>("--log", () => string.Empty, "Optional log file path");
    var outputRootOpt = new Option<string>("--output-root", () => string.Empty, "Folder to scan for generated CKL files");

    cmd.AddOption(toolRootOpt); cmd.AddOption(argsOpt); cmd.AddOption(workDirOpt);
    cmd.AddOption(logOpt); cmd.AddOption(outputRootOpt);

    cmd.SetHandler((toolRoot, args, workDir, logPath, outputRoot) =>
    {
      var runner = new EvaluateStigRunner();
      var result = runner.Run(toolRoot, args, string.IsNullOrWhiteSpace(workDir) ? null : workDir);

      if (!string.IsNullOrWhiteSpace(result.Output)) Console.WriteLine(result.Output);
      if (!string.IsNullOrWhiteSpace(result.Error)) Console.Error.WriteLine(result.Error);

      if (!string.IsNullOrWhiteSpace(logPath))
        File.WriteAllText(logPath, result.Output + Environment.NewLine + result.Error);

      if (!string.IsNullOrWhiteSpace(outputRoot) && Directory.Exists(outputRoot))
        WriteConsolidatedResults(outputRoot, "Evaluate-STIG", result.StartedAt, result.FinishedAt);

      Environment.ExitCode = result.ExitCode;
    }, toolRootOpt, argsOpt, workDirOpt, logOpt, outputRootOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterVerifyScap(RootCommand rootCmd)
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

    cmd.SetHandler((exe, args, workDir, outputRoot, toolName, logPath) =>
    {
      var runner = new ScapRunner();
      var result = runner.Run(exe, args, string.IsNullOrWhiteSpace(workDir) ? null : workDir);

      if (!string.IsNullOrWhiteSpace(result.Output)) Console.WriteLine(result.Output);
      if (!string.IsNullOrWhiteSpace(result.Error)) Console.Error.WriteLine(result.Error);

      if (!string.IsNullOrWhiteSpace(logPath))
        File.WriteAllText(logPath, result.Output + Environment.NewLine + result.Error);

      if (!string.IsNullOrWhiteSpace(outputRoot) && Directory.Exists(outputRoot))
        WriteConsolidatedResults(outputRoot, toolName, result.StartedAt, result.FinishedAt);

      Environment.ExitCode = result.ExitCode;
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
        if (pipeIdx > 0) { label = item.Substring(0, pipeIdx).Trim(); path = item.Substring(pipeIdx + 1).Trim(); }

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
      var exporter = host.Services.GetRequiredService<STIGForge.Export.EmassExporter>();
      var result = await exporter.ExportAsync(new STIGForge.Export.ExportRequest
      {
        BundleRoot = bundle, OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output
      }, CancellationToken.None);

      Console.WriteLine("Exported eMASS package:");
      Console.WriteLine("  " + result.OutputRoot);
      Console.WriteLine("  " + result.ManifestPath);

      if (result.ValidationResult != null)
      {
        Console.WriteLine();
        Console.WriteLine($"Package validation: {(result.ValidationResult.IsValid ? "VALID" : "INVALID")}");
        foreach (var error in result.ValidationResult.Errors) Console.WriteLine($"  Error: {error}");
        foreach (var warning in result.ValidationResult.Warnings) Console.WriteLine($"  Warning: {warning}");
        if (result.ValidationResult.IsValid) Console.WriteLine("  Package is ready for eMASS submission!");
      }
      await host.StopAsync();
    }, bundleOpt, outOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void WriteConsolidatedResults(string outputRoot, string toolName, DateTimeOffset startedAt, DateTimeOffset finishedAt)
  {
    var report = VerifyReportWriter.BuildFromCkls(outputRoot, toolName);
    report.StartedAt = startedAt;
    report.FinishedAt = finishedAt;

    var jsonPath = Path.Combine(outputRoot, "consolidated-results.json");
    var csvPath = Path.Combine(outputRoot, "consolidated-results.csv");
    VerifyReportWriter.WriteJson(jsonPath, report);
    VerifyReportWriter.WriteCsv(csvPath, report.Results);

    var summary = VerifyReportWriter.BuildCoverageSummary(report.Results);
    VerifyReportWriter.WriteCoverageSummary(Path.Combine(outputRoot, "coverage_summary.csv"), Path.Combine(outputRoot, "coverage_summary.json"), summary);

    Console.WriteLine("Wrote consolidated results:");
    Console.WriteLine("  " + jsonPath);
    Console.WriteLine("  " + csvPath);
  }
}

using System.CommandLine;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class ManualCommands
{
  public static void Register(RootCommand rootCmd, Func<Microsoft.Extensions.Hosting.IHost> buildHost)
  {
    RegisterExportAnswers(rootCmd);
    RegisterImportAnswers(rootCmd);
  }

  private static void RegisterExportAnswers(RootCommand rootCmd)
  {
    var cmd = new Command("export-answers", "Export manual answers from a bundle to a portable JSON file");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outputOpt = new Option<string>("--output", "Output JSON file path") { IsRequired = true };
    var stigIdOpt = new Option<string>("--stig-id", () => string.Empty, "STIG identifier for export metadata");
    cmd.AddOption(bundleOpt);
    cmd.AddOption(outputOpt);
    cmd.AddOption(stigIdOpt);

    cmd.SetHandler((bundle, output, stigId) =>
    {
      var svc = new ManualAnswerService();
      var stigIdValue = string.IsNullOrWhiteSpace(stigId) ? null : stigId;
      var export = svc.ExportAnswers(bundle, stigIdValue);
      svc.WriteExportFile(output, export);

      var count = export.Answers?.Answers?.Count ?? 0;
      Console.WriteLine($"Exported {count} answers to {output}");
      if (!string.IsNullOrWhiteSpace(export.StigId))
        Console.WriteLine($"  STIG ID:     {export.StigId}");
      Console.WriteLine($"  Exported at: {export.ExportedAt}");
      Console.WriteLine($"  Exported by: {export.ExportedBy}");
    }, bundleOpt, outputOpt, stigIdOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterImportAnswers(RootCommand rootCmd)
  {
    var cmd = new Command("import-answers", "Import manual answers from a portable JSON file into a bundle");
    var bundleOpt = new Option<string>("--bundle", "Target bundle root path") { IsRequired = true };
    var fileOpt = new Option<string>("--file", "Path to exported answers JSON file") { IsRequired = true };
    cmd.AddOption(bundleOpt);
    cmd.AddOption(fileOpt);

    cmd.SetHandler((bundle, file) =>
    {
      var svc = new ManualAnswerService();
      var import = svc.ReadExportFile(file);
      var result = svc.ImportAnswers(bundle, import);

      Console.WriteLine($"Imported {result.Imported} answers, skipped {result.Skipped} (already resolved)");
      Console.WriteLine($"  Total in file: {result.Total}");

      if (result.SkippedControls.Count > 0)
      {
        Console.WriteLine("  Skipped controls (already resolved):");
        foreach (var control in result.SkippedControls)
          Console.WriteLine($"    - {control}");
      }
    }, bundleOpt, fileOpt);

    rootCmd.AddCommand(cmd);
  }
}

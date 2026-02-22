using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Export;

namespace STIGForge.Cli.Commands;

internal static class ExportCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterExportPoam(rootCmd, buildHost);
    RegisterExportCkl(rootCmd, buildHost);
    RegisterExportEmass(rootCmd, buildHost);
    RegisterImportAttestations(rootCmd, buildHost);
  }

  private static void RegisterExportPoam(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-poam", "Export standalone POA&M (Plan of Action & Milestones) from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Output directory override");
    var systemOpt = new Option<string>("--system-name", () => string.Empty, "System name override");
    cmd.AddOption(bundleOpt); cmd.AddOption(outOpt); cmd.AddOption(systemOpt);

    cmd.SetHandler(async (bundle, output, systemName) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExportCommands");
      logger.LogInformation("export-poam started: bundle={Bundle}", bundle);

      var result = StandalonePoamExporter.ExportPoam(new PoamExportRequest
      {
        BundleRoot = bundle,
        OutputDirectory = string.IsNullOrWhiteSpace(output) ? null : output,
        SystemName = string.IsNullOrWhiteSpace(systemName) ? null : systemName
      });

      Console.WriteLine("POA&M export:");
      Console.WriteLine("  Output: " + result.OutputDirectory);
      Console.WriteLine("  Open findings: " + result.ItemCount);
      Console.WriteLine($"  CAT I: {result.CriticalCount}  CAT II: {result.HighCount}  CAT III: {result.MediumCount}");

      logger.LogInformation("export-poam completed: {ItemCount} findings exported to {Output}", result.ItemCount, result.OutputDirectory);
      await host.StopAsync();
    }, bundleOpt, outOpt, systemOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExportCkl(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-ckl", "Export STIG Viewer-compatible CKL (Checklist) from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Output directory override");
    var fileNameOpt = new Option<string>("--file-name", () => string.Empty, "Output file name (default: stigforge_checklist.ckl)");
    var hostOpt = new Option<string>("--host-name", () => string.Empty, "Host name for CKL ASSET section");
    var ipOpt = new Option<string>("--host-ip", () => string.Empty, "Host IP for CKL ASSET section");
    var macOpt = new Option<string>("--host-mac", () => string.Empty, "Host MAC for CKL ASSET section");
    var stigIdOpt = new Option<string>("--stig-id", () => string.Empty, "STIG ID for CKL header");
    var formatOpt = new Option<string>("--format", () => "ckl", "Checklist format: ckl or cklb");
    var includeCsvOpt = new Option<bool>("--include-csv", () => false, "Also emit CSV checklist rows");
    formatOpt.AddValidator(result =>
    {
      var value = result.GetValueOrDefault<string>() ?? string.Empty;
      if (!string.Equals(value, "ckl", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(value, "cklb", StringComparison.OrdinalIgnoreCase))
      {
        result.ErrorMessage = "Invalid --format value '" + value + "'. Allowed values: ckl, cklb.";
      }
    });

    cmd.AddOption(bundleOpt); cmd.AddOption(outOpt); cmd.AddOption(fileNameOpt);
    cmd.AddOption(hostOpt); cmd.AddOption(ipOpt); cmd.AddOption(macOpt); cmd.AddOption(stigIdOpt); cmd.AddOption(formatOpt); cmd.AddOption(includeCsvOpt);

    cmd.SetHandler(async (InvocationContext context) =>
    {
      var bundle = context.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var output = context.ParseResult.GetValueForOption(outOpt) ?? string.Empty;
      var fileName = context.ParseResult.GetValueForOption(fileNameOpt) ?? string.Empty;
      var hostName = context.ParseResult.GetValueForOption(hostOpt) ?? string.Empty;
      var hostIp = context.ParseResult.GetValueForOption(ipOpt) ?? string.Empty;
      var hostMac = context.ParseResult.GetValueForOption(macOpt) ?? string.Empty;
      var stigId = context.ParseResult.GetValueForOption(stigIdOpt) ?? string.Empty;
      var format = context.ParseResult.GetValueForOption(formatOpt) ?? "ckl";
      var includeCsv = context.ParseResult.GetValueForOption(includeCsvOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExportCommands");
      logger.LogInformation("export-ckl started: bundle={Bundle}", bundle);

      var checklistFormat = ParseChecklistFormat(format);

      var result = CklExporter.ExportCkl(new CklExportRequest
      {
        BundleRoot = bundle,
        OutputDirectory = string.IsNullOrWhiteSpace(output) ? null : output,
        FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName,
        HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName,
        HostIp = string.IsNullOrWhiteSpace(hostIp) ? null : hostIp,
        HostMac = string.IsNullOrWhiteSpace(hostMac) ? null : hostMac,
        StigId = string.IsNullOrWhiteSpace(stigId) ? null : stigId,
        FileFormat = checklistFormat,
        IncludeCsv = includeCsv
      });

      Console.WriteLine("CKL export:");
      Console.WriteLine("  File(s): " + string.Join(" | ", result.OutputPaths));
      Console.WriteLine("  Controls: " + result.ControlCount);

      logger.LogInformation("export-ckl completed: {ControlCount} controls exported to {Output}", result.ControlCount, result.OutputPath);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExportEmass(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export-emass", "Export deterministic eMASS submission package from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var outOpt = new Option<string>("--output", () => string.Empty, "Output root directory override");
    cmd.AddOption(bundleOpt); cmd.AddOption(outOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExportCommands");
      logger.LogInformation("export-emass started: bundle={Bundle}", bundle);

      var paths = host.Services.GetRequiredService<IPathBuilder>();
      var hash = host.Services.GetRequiredService<IHashingService>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var exporter = new EmassExporter(paths, hash, audit);

      var result = await exporter.ExportAsync(new ExportRequest
      {
        BundleRoot = bundle,
        OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output
      }, CancellationToken.None);

      Console.WriteLine("eMASS export:");
      Console.WriteLine("  Output: " + result.OutputRoot);
      Console.WriteLine("  Manifest: " + result.ManifestPath);
      Console.WriteLine("  Valid: " + (result.ValidationResult?.IsValid ?? false));
      Console.WriteLine("  Ready for submission: " + result.IsReadyForSubmission);
      if (result.BlockingFailures.Count > 0)
      {
        Console.WriteLine("  Errors:");
        foreach (var err in result.BlockingFailures)
          Console.WriteLine("    - " + err);
      }
      if (result.Warnings.Count > 0)
      {
        Console.WriteLine("  Warnings:");
        foreach (var warn in result.Warnings)
          Console.WriteLine("    - " + warn);
      }

      logger.LogInformation("export-emass completed: output={Output}, valid={Valid}, ready={Ready}",
        result.OutputRoot, result.ValidationResult?.IsValid, result.IsReadyForSubmission);
      if (!result.IsReadyForSubmission) Environment.ExitCode = 1;
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterImportAttestations(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("import-attestations", "Import completed attestation CSV into eMASS package");
    var packageOpt = new Option<string>("--package", "eMASS package root path") { IsRequired = true };
    var fileOpt = new Option<string>("--file", "Path to filled attestation CSV file") { IsRequired = true };
    cmd.AddOption(packageOpt); cmd.AddOption(fileOpt);

    cmd.SetHandler(async (package, file) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExportCommands");
      logger.LogInformation("import-attestations started: package={Package}, file={File}", package, file);

      var audit = host.Services.GetService<IAuditTrailService>();
      var result = AttestationImporter.ImportAttestations(package, file, audit);

      Console.WriteLine("Attestation import:");
      Console.WriteLine($"  Updated: {result.Updated}");
      Console.WriteLine($"  Skipped: {result.Skipped}");
      Console.WriteLine($"  Not found: {result.NotFound}");
      if (result.NotFoundControls.Count > 0)
      {
        Console.WriteLine("  Not found controls:");
        foreach (var ctrl in result.NotFoundControls)
          Console.WriteLine("    - " + ctrl);
      }

      logger.LogInformation("import-attestations completed: updated={Updated}, skipped={Skipped}, notFound={NotFound}",
        result.Updated, result.Skipped, result.NotFound);
      await host.StopAsync();
    }, packageOpt, fileOpt);

    rootCmd.AddCommand(cmd);
  }

  private static CklFileFormat ParseChecklistFormat(string format)
  {
    if (string.Equals(format, "cklb", StringComparison.OrdinalIgnoreCase))
      return CklFileFormat.Cklb;

    if (string.Equals(format, "ckl", StringComparison.OrdinalIgnoreCase))
      return CklFileFormat.Ckl;

    throw new ArgumentException("Invalid --format value '" + format + "'. Allowed values: ckl, cklb.", nameof(format));
  }
}

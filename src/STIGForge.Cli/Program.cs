using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Apply.Snapshot;
using STIGForge.Infrastructure.Hashing;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.Storage;
using STIGForge.Infrastructure.System;
using STIGForge.Verify;

static IHost BuildHost()
{
  return Host.CreateDefaultBuilder()
    .UseSerilog((ctx, lc) =>
    {
      var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "STIGForge", "logs");
      Directory.CreateDirectory(root);
      lc.MinimumLevel.Information()
        .WriteTo.File(Path.Combine(root, "stigforge-cli.log"), rollingInterval: RollingInterval.Day);
    })
    .ConfigureServices(services =>
    {
      services.AddSingleton<IClock, SystemClock>();
      services.AddSingleton<IProcessRunner, ProcessRunner>();
      services.AddSingleton<IClassificationScopeService, ClassificationScopeService>();
      services.AddSingleton<STIGForge.Core.Services.ReleaseAgeGate>();
      services.AddSingleton<IPathBuilder, PathBuilder>();
      services.AddSingleton<IHashingService, Sha256HashingService>();

      services.AddSingleton(sp =>
      {
        var paths = sp.GetRequiredService<IPathBuilder>();
        Directory.CreateDirectory(paths.GetAppDataRoot());
        Directory.CreateDirectory(paths.GetLogsRoot());
        var dbPath = Path.Combine(paths.GetAppDataRoot(), "data", "stigforge.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var cs = "Data Source=" + dbPath;
        DbBootstrap.EnsureCreated(cs);
        return cs;
      });

      services.AddSingleton<IContentPackRepository>(sp => new SqliteContentPackRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IControlRepository>(sp => new SqliteJsonControlRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IProfileRepository>(sp => new SqliteJsonProfileRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<IOverlayRepository>(sp => new SqliteJsonOverlayRepository(sp.GetRequiredService<string>()));
      services.AddSingleton<ContentPackImporter>();
      services.AddSingleton<BundleBuilder>();
      services.AddSingleton<SnapshotService>();
      services.AddSingleton<RollbackScriptGenerator>();
      services.AddSingleton<STIGForge.Apply.ApplyRunner>();
      services.AddSingleton<BundleOrchestrator>();
      services.AddSingleton<STIGForge.Export.EmassExporter>();
    })
    .Build();
}

var rootCmd = new RootCommand("STIGForge CLI (offline-first)");

var importCmd = new Command("import-pack", "Import DISA content packs (STIG XCCDF, SCAP bundles, or GPO packages)");
var zipArg = new Argument<string>("zip", "Path to zip file");
var nameOpt = new Option<string>("--name", () => "Imported_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm"), "Pack name");
importCmd.AddArgument(zipArg);
importCmd.AddOption(nameOpt);

importCmd.SetHandler(async (zip, name) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  var importer = host.Services.GetRequiredService<ContentPackImporter>();
  var pack = await importer.ImportZipAsync(zip, name, "cli_import", CancellationToken.None);
  Console.WriteLine("Imported: " + pack.Name + " (" + pack.PackId + ")");

  await host.StopAsync();
}, zipArg, nameOpt);

rootCmd.AddCommand(importCmd);

var buildCmd = new Command("build-bundle", "Build offline bundle (Apply/Verify/Manual/Evidence/Reports)");
var packIdOpt = new Option<string>("--pack-id", "Pack id to build from") { IsRequired = true };
var profileIdOpt = new Option<string>("--profile-id", "Profile id to load from repo");
var profileJsonOpt = new Option<string>("--profile-json", "Path to profile JSON file");
var bundleIdOpt = new Option<string>("--bundle-id", "Bundle id (optional)");
var outputOpt = new Option<string>("--output", "Output path override (optional)");
var saveProfileOpt = new Option<bool>("--save-profile", "Save profile to repo when using --profile-json");
var forceAutoApplyOpt = new Option<bool>("--force-auto-apply", "Override release-age gate (use with caution)");

buildCmd.AddOption(packIdOpt);
buildCmd.AddOption(profileIdOpt);
buildCmd.AddOption(profileJsonOpt);
buildCmd.AddOption(bundleIdOpt);
buildCmd.AddOption(outputOpt);
buildCmd.AddOption(saveProfileOpt);
buildCmd.AddOption(forceAutoApplyOpt);

buildCmd.SetHandler(async (packId, profileId, profileJson, bundleId, output, saveProfile, forceAutoApply) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  if (string.IsNullOrWhiteSpace(profileId) && string.IsNullOrWhiteSpace(profileJson))
    throw new ArgumentException("Provide --profile-id or --profile-json.");

  if (!string.IsNullOrWhiteSpace(profileId) && !string.IsNullOrWhiteSpace(profileJson))
    throw new ArgumentException("Provide only one of --profile-id or --profile-json.");

  var packs = host.Services.GetRequiredService<IContentPackRepository>();
  var profiles = host.Services.GetRequiredService<IProfileRepository>();
  var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
  var controls = host.Services.GetRequiredService<IControlRepository>();
  var builder = host.Services.GetRequiredService<BundleBuilder>();

  var pack = await packs.GetAsync(packId, CancellationToken.None);
  if (pack is null) throw new ArgumentException("Pack not found: " + packId);

  Profile profile;
  if (!string.IsNullOrWhiteSpace(profileId))
  {
    profile = await profiles.GetAsync(profileId, CancellationToken.None) ??
      throw new ArgumentException("Profile not found: " + profileId);
  }
  else
  {
    var json = File.ReadAllText(profileJson!);
    profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
      ?? throw new ArgumentException("Invalid profile JSON.");

    if (string.IsNullOrWhiteSpace(profile.ProfileId))
      profile.ProfileId = Guid.NewGuid().ToString("n");

    if (saveProfile)
      await profiles.SaveAsync(profile, CancellationToken.None);
  }

  var list = await controls.ListControlsAsync(pack.PackId, CancellationToken.None);
  var overlays = new List<STIGForge.Core.Models.Overlay>();
  if (profile.OverlayIds != null && profile.OverlayIds.Count > 0)
  {
    foreach (var oid in profile.OverlayIds)
    {
      if (string.IsNullOrWhiteSpace(oid)) continue;
      var ov = await overlaysRepo.GetAsync(oid, CancellationToken.None);
      if (ov != null) overlays.Add(ov);
    }
  }
  var result = await builder.BuildAsync(new BundleBuildRequest
  {
    BundleId = bundleId ?? string.Empty,
    OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output,
    Pack = pack,
    Profile = profile,
    Controls = list,
    Overlays = overlays,
    ToolVersion = "0.1.0-dev",
    ForceAutoApply = forceAutoApply
  }, CancellationToken.None);

  Console.WriteLine("Bundle created: " + result.BundleRoot);
  Console.WriteLine("Manifest: " + result.ManifestPath);
  var gatePath = Path.Combine(result.BundleRoot, "Reports", "automation_gate.json");
  if (File.Exists(gatePath))
    Console.WriteLine("Automation gate: " + gatePath);

  await host.StopAsync();
}, packIdOpt, profileIdOpt, profileJsonOpt, bundleIdOpt, outputOpt, saveProfileOpt, forceAutoApplyOpt);

rootCmd.AddCommand(buildCmd);

var overlayImportCmd = new Command("overlay-import-powerstig", "Import PowerSTIG overrides from CSV");
var overlayCsvOpt = new Option<string>("--csv", "CSV path with RuleId,SettingName,Value") { IsRequired = true };
var overlayNameOpt = new Option<string>("--name", () => "PowerSTIG Overrides", "Overlay name");
var overlayIdOpt = new Option<string>("--overlay-id", () => string.Empty, "Update an existing overlay id (optional)");

overlayImportCmd.AddOption(overlayCsvOpt);
overlayImportCmd.AddOption(overlayNameOpt);
overlayImportCmd.AddOption(overlayIdOpt);

overlayImportCmd.SetHandler(async (csvPath, overlayName, overlayId) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
  var overlays = ReadPowerStigOverrides(csvPath);

  STIGForge.Core.Models.Overlay overlay;
  if (!string.IsNullOrWhiteSpace(overlayId))
  {
    overlay = await overlaysRepo.GetAsync(overlayId, CancellationToken.None) ??
      new STIGForge.Core.Models.Overlay { OverlayId = overlayId };
  }
  else
  {
    overlay = new STIGForge.Core.Models.Overlay { OverlayId = Guid.NewGuid().ToString("n") };
  }

  overlay.Name = overlayName;
  overlay.UpdatedAt = DateTimeOffset.Now;
  overlay.PowerStigOverrides = overlays;

  await overlaysRepo.SaveAsync(overlay, CancellationToken.None);
  Console.WriteLine("Overlay saved: " + overlay.OverlayId + " (" + overlay.Name + ")");

  await host.StopAsync();
}, overlayCsvOpt, overlayNameOpt, overlayIdOpt);

rootCmd.AddCommand(overlayImportCmd);

var mapExportCmd = new Command("powerstig-map-export", "Export PowerSTIG mapping template CSV from a pack");
var mapPackOpt = new Option<string>("--pack-id", "Pack id to export from") { IsRequired = true };
var mapOutOpt = new Option<string>("--output", "Output CSV path") { IsRequired = true };

mapExportCmd.AddOption(mapPackOpt);
mapExportCmd.AddOption(mapOutOpt);

mapExportCmd.SetHandler(async (packId, output) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  var controlsRepo = host.Services.GetRequiredService<IControlRepository>();
  var list = await controlsRepo.ListControlsAsync(packId, CancellationToken.None);
  WritePowerStigMapCsv(output, list);
  Console.WriteLine("Wrote mapping template: " + output);

  await host.StopAsync();
}, mapPackOpt, mapOutOpt);

rootCmd.AddCommand(mapExportCmd);

var orchestrateCmd = new Command("orchestrate", "Run build -> apply -> verify using a bundle");
var orchBundleOpt = new Option<string>("--bundle", "Bundle root path (existing bundle)") { IsRequired = true };
var orchDscOpt = new Option<string>("--dsc-path", () => string.Empty, "DSC MOF path (optional)");
var orchDscVerboseOpt = new Option<bool>("--dsc-verbose", "Verbose DSC output");
var orchPowerStigModuleOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module path (optional)");
var orchPowerStigDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file (optional)");
var orchPowerStigOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG output (optional)");
var orchPowerStigVerboseOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");
var orchEvalRootOpt = new Option<string>("--evaluate-stig", () => string.Empty, "Evaluate-STIG root (optional)");
var orchEvalArgsOpt = new Option<string>("--evaluate-args", () => string.Empty, "Evaluate-STIG args (optional)");
var orchScapCmdOpt = new Option<string>("--scap-cmd", () => string.Empty, "SCAP/SCC command path (optional)");
var orchScapArgsOpt = new Option<string>("--scap-args", () => string.Empty, "SCAP args (optional)");
var orchScapLabelOpt = new Option<string>("--scap-label", () => string.Empty, "Label for SCAP tool (optional)");

orchestrateCmd.AddOption(orchBundleOpt);
orchestrateCmd.AddOption(orchDscOpt);
orchestrateCmd.AddOption(orchDscVerboseOpt);
orchestrateCmd.AddOption(orchPowerStigModuleOpt);
orchestrateCmd.AddOption(orchPowerStigDataOpt);
orchestrateCmd.AddOption(orchPowerStigOutOpt);
orchestrateCmd.AddOption(orchPowerStigVerboseOpt);
orchestrateCmd.AddOption(orchEvalRootOpt);
orchestrateCmd.AddOption(orchEvalArgsOpt);
orchestrateCmd.AddOption(orchScapCmdOpt);
orchestrateCmd.AddOption(orchScapArgsOpt);
orchestrateCmd.AddOption(orchScapLabelOpt);

orchestrateCmd.SetHandler(async (InvocationContext ctx) =>
{
  var bundle = ctx.ParseResult.GetValueForOption(orchBundleOpt) ?? string.Empty;
  var dscPath = ctx.ParseResult.GetValueForOption(orchDscOpt) ?? string.Empty;
  var dscVerbose = ctx.ParseResult.GetValueForOption(orchDscVerboseOpt);
  var powerStigModule = ctx.ParseResult.GetValueForOption(orchPowerStigModuleOpt) ?? string.Empty;
  var powerStigData = ctx.ParseResult.GetValueForOption(orchPowerStigDataOpt) ?? string.Empty;
  var powerStigOut = ctx.ParseResult.GetValueForOption(orchPowerStigOutOpt) ?? string.Empty;
  var powerStigVerbose = ctx.ParseResult.GetValueForOption(orchPowerStigVerboseOpt);
  var evalRoot = ctx.ParseResult.GetValueForOption(orchEvalRootOpt) ?? string.Empty;
  var evalArgs = ctx.ParseResult.GetValueForOption(orchEvalArgsOpt) ?? string.Empty;
  var scapCmd = ctx.ParseResult.GetValueForOption(orchScapCmdOpt) ?? string.Empty;
  var scapArgs = ctx.ParseResult.GetValueForOption(orchScapArgsOpt) ?? string.Empty;
  var scapLabel = ctx.ParseResult.GetValueForOption(orchScapLabelOpt) ?? string.Empty;

  using var host = BuildHost();
  await host.StartAsync();

  var orchestrator = host.Services.GetRequiredService<BundleOrchestrator>();
  await orchestrator.OrchestrateAsync(new OrchestrateRequest
  {
    BundleRoot = bundle,
    DscMofPath = string.IsNullOrWhiteSpace(dscPath) ? null : dscPath,
    DscVerbose = dscVerbose,
    PowerStigModulePath = string.IsNullOrWhiteSpace(powerStigModule) ? null : powerStigModule,
    PowerStigDataFile = string.IsNullOrWhiteSpace(powerStigData) ? null : powerStigData,
    PowerStigOutputPath = string.IsNullOrWhiteSpace(powerStigOut) ? null : powerStigOut,
    PowerStigVerbose = powerStigVerbose,
    EvaluateStigRoot = string.IsNullOrWhiteSpace(evalRoot) ? null : evalRoot,
    EvaluateStigArgs = string.IsNullOrWhiteSpace(evalArgs) ? null : evalArgs,
    ScapCommandPath = string.IsNullOrWhiteSpace(scapCmd) ? null : scapCmd,
    ScapArgs = string.IsNullOrWhiteSpace(scapArgs) ? null : scapArgs,
    ScapToolLabel = string.IsNullOrWhiteSpace(scapLabel) ? null : scapLabel
  }, CancellationToken.None);

  await host.StopAsync();
});

rootCmd.AddCommand(orchestrateCmd);

var evalCmd = new Command("verify-evaluate-stig", "Run Evaluate-STIG.ps1 with provided arguments");
var toolRootOpt = new Option<string>("--tool-root",
  () => Path.GetFullPath(".\\.stigforge\\tools\\Evaluate-STIG\\Evaluate-STIG"),
  "Root folder that contains Evaluate-STIG.ps1");
var argsOpt = new Option<string>("--args", () => string.Empty, "Arguments passed to Evaluate-STIG.ps1");
var workDirOpt = new Option<string>("--workdir", () => string.Empty, "Working directory for the script");
var logOpt = new Option<string>("--log", () => string.Empty, "Optional log file path");
var outputRootOpt = new Option<string>("--output-root", () => string.Empty, "Folder to scan for generated CKL files");

evalCmd.AddOption(toolRootOpt);
evalCmd.AddOption(argsOpt);
evalCmd.AddOption(workDirOpt);
evalCmd.AddOption(logOpt);
evalCmd.AddOption(outputRootOpt);

evalCmd.SetHandler((toolRoot, args, workDir, logPath, outputRoot) =>
{
  var runner = new EvaluateStigRunner();
  var result = runner.Run(toolRoot, args, string.IsNullOrWhiteSpace(workDir) ? null : workDir);

  if (!string.IsNullOrWhiteSpace(result.Output))
    Console.WriteLine(result.Output);

  if (!string.IsNullOrWhiteSpace(result.Error))
    Console.Error.WriteLine(result.Error);

  if (!string.IsNullOrWhiteSpace(logPath))
  {
    var combined = result.Output + Environment.NewLine + result.Error;
    File.WriteAllText(logPath, combined);
  }

  if (!string.IsNullOrWhiteSpace(outputRoot) && Directory.Exists(outputRoot))
  {
    var report = VerifyReportWriter.BuildFromCkls(outputRoot, "Evaluate-STIG");
    report.StartedAt = result.StartedAt;
    report.FinishedAt = result.FinishedAt;

    var jsonPath = Path.Combine(outputRoot, "consolidated-results.json");
    var csvPath = Path.Combine(outputRoot, "consolidated-results.csv");
    VerifyReportWriter.WriteJson(jsonPath, report);
    VerifyReportWriter.WriteCsv(csvPath, report.Results);

    var summary = VerifyReportWriter.BuildCoverageSummary(report.Results);
    var summaryJson = Path.Combine(outputRoot, "coverage_summary.json");
    var summaryCsv = Path.Combine(outputRoot, "coverage_summary.csv");
    VerifyReportWriter.WriteCoverageSummary(summaryCsv, summaryJson, summary);

    Console.WriteLine("Wrote consolidated results:");
    Console.WriteLine("  " + jsonPath);
    Console.WriteLine("  " + csvPath);
    Console.WriteLine("Wrote coverage summary:");
    Console.WriteLine("  " + summaryJson);
    Console.WriteLine("  " + summaryCsv);
  }

  Environment.ExitCode = result.ExitCode;
}, toolRootOpt, argsOpt, workDirOpt, logOpt, outputRootOpt);

rootCmd.AddCommand(evalCmd);

var applyCmd = new Command("apply-run", "Run apply phase using a bundle and optional script");
var bundleOpt = new Option<string>("--bundle", "Path to bundle root") { IsRequired = true };
var modeOpt = new Option<string>("--mode", () => string.Empty, "Override mode: AuditOnly|Safe|Full");
var scriptOpt = new Option<string>("--script", () => string.Empty, "Optional PowerShell script to execute");
var scriptArgsOpt = new Option<string>("--script-args", () => string.Empty, "Optional script args (passed as-is)");
var dscPathOpt = new Option<string>("--dsc-path", () => string.Empty, "Optional DSC MOF directory for Start-DscConfiguration");
var dscVerboseOpt = new Option<bool>("--dsc-verbose", "Enable verbose output for DSC apply");
var skipSnapshotOpt = new Option<bool>("--skip-snapshot", "Skip snapshot placeholder generation");
var powerStigModuleOpt = new Option<string>("--powerstig-module", () => string.Empty, "Path to PowerSTIG module folder (contains *.psd1)");
var powerStigDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "Optional PowerSTIG data file (XML/PSD1)");
var powerStigOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG MOF output folder (default: bundle Apply\\Dsc)");
var powerStigVerboseOpt = new Option<bool>("--powerstig-verbose", "Enable verbose PowerSTIG compile output");

applyCmd.AddOption(bundleOpt);
applyCmd.AddOption(modeOpt);
applyCmd.AddOption(scriptOpt);
applyCmd.AddOption(scriptArgsOpt);
applyCmd.AddOption(dscPathOpt);
applyCmd.AddOption(dscVerboseOpt);
applyCmd.AddOption(skipSnapshotOpt);
applyCmd.AddOption(powerStigModuleOpt);
applyCmd.AddOption(powerStigDataOpt);
applyCmd.AddOption(powerStigOutOpt);
applyCmd.AddOption(powerStigVerboseOpt);

applyCmd.SetHandler(async (InvocationContext ctx) =>
{
  var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
  var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? string.Empty;
  var script = ctx.ParseResult.GetValueForOption(scriptOpt) ?? string.Empty;
  var scriptArgs = ctx.ParseResult.GetValueForOption(scriptArgsOpt) ?? string.Empty;
  var dscPath = ctx.ParseResult.GetValueForOption(dscPathOpt) ?? string.Empty;
  var dscVerbose = ctx.ParseResult.GetValueForOption(dscVerboseOpt);
  var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnapshotOpt);
  var powerStigModule = ctx.ParseResult.GetValueForOption(powerStigModuleOpt) ?? string.Empty;
  var powerStigData = ctx.ParseResult.GetValueForOption(powerStigDataOpt) ?? string.Empty;
  var powerStigOut = ctx.ParseResult.GetValueForOption(powerStigOutOpt) ?? string.Empty;
  var powerStigVerbose = ctx.ParseResult.GetValueForOption(powerStigVerboseOpt);

  HardeningMode? parsedMode = null;
  if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse<HardeningMode>(mode, true, out var m))
    parsedMode = m;

  using var host = BuildHost();
  await host.StartAsync();

  var runner = host.Services.GetRequiredService<STIGForge.Apply.ApplyRunner>();
  var result = await runner.RunAsync(new STIGForge.Apply.ApplyRequest
  {
    BundleRoot = bundle,
    ModeOverride = parsedMode,
    ScriptPath = string.IsNullOrWhiteSpace(script) ? null : script,
    ScriptArgs = string.IsNullOrWhiteSpace(scriptArgs) ? null : scriptArgs,
    DscMofPath = string.IsNullOrWhiteSpace(dscPath) ? null : dscPath,
    DscVerbose = dscVerbose,
    SkipSnapshot = skipSnapshot,
    PowerStigModulePath = string.IsNullOrWhiteSpace(powerStigModule) ? null : powerStigModule,
    PowerStigDataFile = string.IsNullOrWhiteSpace(powerStigData) ? null : powerStigData,
    PowerStigOutputPath = string.IsNullOrWhiteSpace(powerStigOut) ? null : powerStigOut,
    PowerStigVerbose = powerStigVerbose
  }, CancellationToken.None);

  Console.WriteLine("Apply completed. Log: " + result.LogPath);
  await host.StopAsync();
});

rootCmd.AddCommand(applyCmd);

var overlapCmd = new Command("coverage-overlap", "Build coverage overlap summary from consolidated results");
var inputsOpt = new Option<string>("--inputs", "Semicolon-delimited inputs. Each item can be 'Label|Path' or just Path.") { IsRequired = true };
var outOpt = new Option<string>("--output", () => string.Empty, "Output folder for summary files (defaults to current dir)");

overlapCmd.AddOption(inputsOpt);
overlapCmd.AddOption(outOpt);

overlapCmd.SetHandler((inputs, output) =>
{
  var outputRoot = string.IsNullOrWhiteSpace(output) ? Environment.CurrentDirectory : output;
  Directory.CreateDirectory(outputRoot);

  var allResults = new List<ControlResult>();
  foreach (var raw in inputs.Split(';', StringSplitOptions.RemoveEmptyEntries))
  {
    var item = raw.Trim();
    if (item.Length == 0) continue;

    string label = string.Empty;
    string path = item;
    var pipeIdx = item.IndexOf('|');
    if (pipeIdx > 0)
    {
      label = item.Substring(0, pipeIdx).Trim();
      path = item.Substring(pipeIdx + 1).Trim();
    }

    var resolved = ResolveReportPath(path);
    var report = VerifyReportReader.LoadFromJson(resolved);

    if (!string.IsNullOrWhiteSpace(label))
      report.Tool = label;

    foreach (var r in report.Results)
    {
      if (string.IsNullOrWhiteSpace(r.Tool))
        r.Tool = report.Tool;
    }

    allResults.AddRange(report.Results);
  }

  var coverage = VerifyReportWriter.BuildCoverageSummary(allResults);
  VerifyReportWriter.WriteCoverageSummary(
    Path.Combine(outputRoot, "coverage_by_tool.csv"),
    Path.Combine(outputRoot, "coverage_by_tool.json"),
    coverage);

  var maps = VerifyReportWriter.BuildControlSourceMap(allResults);
  VerifyReportWriter.WriteControlSourceMap(
    Path.Combine(outputRoot, "control_sources.csv"),
    maps);

  var overlaps = VerifyReportWriter.BuildOverlapSummary(allResults);
  VerifyReportWriter.WriteOverlapSummary(
    Path.Combine(outputRoot, "coverage_overlap.csv"),
    Path.Combine(outputRoot, "coverage_overlap.json"),
    overlaps);

  Console.WriteLine("Wrote overlap summaries to: " + outputRoot);
}, inputsOpt, outOpt);

rootCmd.AddCommand(overlapCmd);

var scapCmd = new Command("verify-scap", "Run a SCAP tool and consolidate CKL results");
var scapExeOpt = new Option<string>("--cmd", "Path to SCAP/SCC executable") { IsRequired = true };
var scapArgsOpt = new Option<string>("--args", () => string.Empty, "Arguments passed to SCAP tool");
var scapWorkOpt = new Option<string>("--workdir", () => string.Empty, "Working directory for SCAP tool");
var scapOutputOpt = new Option<string>("--output-root", () => string.Empty, "Folder to scan for generated CKL files");
var scapToolOpt = new Option<string>("--tool", () => "SCAP", "Tool label used in reports");
var scapLogOpt = new Option<string>("--log", () => string.Empty, "Optional log file path");

scapCmd.AddOption(scapExeOpt);
scapCmd.AddOption(scapArgsOpt);
scapCmd.AddOption(scapWorkOpt);
scapCmd.AddOption(scapOutputOpt);
scapCmd.AddOption(scapToolOpt);
scapCmd.AddOption(scapLogOpt);

scapCmd.SetHandler((cmd, args, workDir, outputRoot, toolName, logPath) =>
{
  var runner = new ScapRunner();
  var result = runner.Run(cmd, args, string.IsNullOrWhiteSpace(workDir) ? null : workDir);

  if (!string.IsNullOrWhiteSpace(result.Output))
    Console.WriteLine(result.Output);

  if (!string.IsNullOrWhiteSpace(result.Error))
    Console.Error.WriteLine(result.Error);

  if (!string.IsNullOrWhiteSpace(logPath))
  {
    var combined = result.Output + Environment.NewLine + result.Error;
    File.WriteAllText(logPath, combined);
  }

  if (!string.IsNullOrWhiteSpace(outputRoot) && Directory.Exists(outputRoot))
  {
    var report = VerifyReportWriter.BuildFromCkls(outputRoot, toolName);
    report.StartedAt = result.StartedAt;
    report.FinishedAt = result.FinishedAt;

    var jsonPath = Path.Combine(outputRoot, "consolidated-results.json");
    var csvPath = Path.Combine(outputRoot, "consolidated-results.csv");
    VerifyReportWriter.WriteJson(jsonPath, report);
    VerifyReportWriter.WriteCsv(csvPath, report.Results);

    var summary = VerifyReportWriter.BuildCoverageSummary(report.Results);
    var summaryJson = Path.Combine(outputRoot, "coverage_summary.json");
    var summaryCsv = Path.Combine(outputRoot, "coverage_summary.csv");
    VerifyReportWriter.WriteCoverageSummary(summaryCsv, summaryJson, summary);

    Console.WriteLine("Wrote consolidated results:");
    Console.WriteLine("  " + jsonPath);
    Console.WriteLine("  " + csvPath);
  }

  Environment.ExitCode = result.ExitCode;
}, scapExeOpt, scapArgsOpt, scapWorkOpt, scapOutputOpt, scapToolOpt, scapLogOpt);

rootCmd.AddCommand(scapCmd);

var exportCmd = new Command("export-emass", "Export eMASS submission package from a bundle");
var exportBundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
var exportOutOpt = new Option<string>("--output", () => string.Empty, "Optional export root override");

exportCmd.AddOption(exportBundleOpt);
exportCmd.AddOption(exportOutOpt);

exportCmd.SetHandler(async (bundle, output) =>
{
  using var host = BuildHost();
  await host.StartAsync();

  var exporter = host.Services.GetRequiredService<STIGForge.Export.EmassExporter>();
  var result = await exporter.ExportAsync(new STIGForge.Export.ExportRequest
  {
    BundleRoot = bundle,
    OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output
  }, CancellationToken.None);

  Console.WriteLine("Exported eMASS package:");
  Console.WriteLine("  " + result.OutputRoot);
  Console.WriteLine("  " + result.ManifestPath);

  await host.StopAsync();
}, exportBundleOpt, exportOutOpt);

rootCmd.AddCommand(exportCmd);

return await InvokeWithErrorHandlingAsync(rootCmd, args);

static async Task<int> InvokeWithErrorHandlingAsync(RootCommand command, string[] argv)
{
  try
  {
    return await command.InvokeAsync(argv);
  }
  catch (ArgumentException ex)
  {
    Console.Error.WriteLine($"[CLI-ARG-001] {ex.Message}");
    return 2;
  }
  catch (FileNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (DirectoryNotFoundException ex)
  {
    Console.Error.WriteLine($"[CLI-IO-404] {ex.Message}");
    return 3;
  }
  catch (Exception ex)
  {
    Console.Error.WriteLine($"[CLI-UNEXPECTED-500] {ex.Message}");
    return 1;
  }
}

static string ResolveReportPath(string path)
{
  if (File.Exists(path)) return path;
  if (Directory.Exists(path))
  {
    var candidate = Path.Combine(path, "consolidated-results.json");
    if (File.Exists(candidate)) return candidate;
  }

  throw new FileNotFoundException("Report not found: " + path);
}

static IReadOnlyList<STIGForge.Core.Models.PowerStigOverride> ReadPowerStigOverrides(string csvPath)
{
  if (!File.Exists(csvPath)) throw new FileNotFoundException("CSV not found", csvPath);
  var lines = File.ReadAllLines(csvPath);
  var list = new List<STIGForge.Core.Models.PowerStigOverride>();

  foreach (var line in lines)
  {
    if (string.IsNullOrWhiteSpace(line)) continue;
    if (line.StartsWith("RuleId", StringComparison.OrdinalIgnoreCase)) continue;

    var parts = ParseCsvLine(line);
    if (parts.Length < 3) continue;

    var ruleId = parts[0].Trim();
    if (string.IsNullOrWhiteSpace(ruleId)) continue;

    list.Add(new STIGForge.Core.Models.PowerStigOverride
    {
      RuleId = ruleId,
      SettingName = parts[1].Trim(),
      Value = parts[2].Trim()
    });
  }

  return list;
}

static string[] ParseCsvLine(string line)
{
  var list = new List<string>();
  var sb = new System.Text.StringBuilder();
  bool inQuotes = false;
  for (int i = 0; i < line.Length; i++)
  {
    var ch = line[i];
    if (ch == '"')
    {
      if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
      {
        sb.Append('"');
        i++;
      }
      else
      {
        inQuotes = !inQuotes;
      }
    }
    else if (ch == ',' && !inQuotes)
    {
      list.Add(sb.ToString());
      sb.Clear();
    }
    else
    {
      sb.Append(ch);
    }
  }
  list.Add(sb.ToString());
  return list.ToArray();
}

static void WritePowerStigMapCsv(string path, IReadOnlyList<ControlRecord> controls)
{
  var sb = new System.Text.StringBuilder(controls.Count * 40 + 128);
  sb.AppendLine("RuleId,Title,SettingName,Value,HintSetting,HintValue");

  var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach (var c in controls)
  {
    var ruleId = c.ExternalIds.RuleId;
    if (string.IsNullOrWhiteSpace(ruleId)) continue;
    if (!seen.Add(ruleId)) continue;

    var hintSetting = ExtractHintSetting(c);
    var hintValue = ExtractHintValue(c);
    sb.AppendLine(string.Join(",",
      Csv(ruleId),
      Csv(c.Title),
      "",
      "",
      Csv(hintSetting),
      Csv(hintValue)));
  }

  File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
}

static string ExtractHintSetting(ControlRecord control)
{
  var text = (control.FixText ?? string.Empty) + "\n" + (control.CheckText ?? string.Empty);
  return ExtractAfterLabel(text, new[] { "Value Name", "Value name", "ValueName" });
}

static string ExtractHintValue(ControlRecord control)
{
  var text = (control.FixText ?? string.Empty) + "\n" + (control.CheckText ?? string.Empty);
  return ExtractAfterLabel(text, new[] { "Value Data", "Value data", "Value:" });
}

static string ExtractAfterLabel(string text, string[] labels)
{
  foreach (var label in labels)
  {
    var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
    if (idx < 0) continue;

    var start = idx + label.Length;
    var line = text.Substring(start);
    var nl = line.IndexOfAny(new[] { '\r', '\n' });
    if (nl >= 0) line = line.Substring(0, nl);
    var cleaned = line.Replace(":", string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
  }

  return string.Empty;
}

static string Csv(string? value)
{
  var v = value ?? string.Empty;
  if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
    v = "\"" + v.Replace("\"", "\"\"") + "\"";
  return v;
}

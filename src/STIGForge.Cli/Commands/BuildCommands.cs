using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class BuildCommands
{
  private const string BreakGlassAckOptionName = "--break-glass-ack";
  private const string BreakGlassReasonOptionName = "--break-glass-reason";

  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterBuildBundle(rootCmd, buildHost);
    RegisterOrchestrate(rootCmd, buildHost);
    RegisterApplyRun(rootCmd, buildHost);
    RegisterMissionAutopilot(rootCmd, buildHost);
  }

  private static void RegisterBuildBundle(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("build-bundle", "Build offline bundle (Apply/Verify/Manual/Evidence/Reports)");
    var packIdOpt = new Option<string>("--pack-id", "Pack id to build from") { IsRequired = true };
    var profileIdOpt = new Option<string>("--profile-id", "Profile id to load from repo");
    var profileJsonOpt = new Option<string>("--profile-json", "Path to profile JSON file");
    var bundleIdOpt = new Option<string>("--bundle-id", "Bundle id (optional)");
    var outputOpt = new Option<string>("--output", "Output path override (optional)");
    var saveProfileOpt = new Option<bool>("--save-profile", "Save profile to repo when using --profile-json");
    var forceAutoApplyOpt = new Option<bool>("--force-auto-apply", "Override release-age gate (use with caution)");
    var breakGlassAckOpt = new Option<bool>(BreakGlassAckOptionName, "Acknowledge high-risk break-glass use for --force-auto-apply");
    var breakGlassReasonOpt = new Option<string>(BreakGlassReasonOptionName, "Reason for break-glass use (required with --force-auto-apply)");

    cmd.AddOption(packIdOpt); cmd.AddOption(profileIdOpt); cmd.AddOption(profileJsonOpt);
    cmd.AddOption(bundleIdOpt); cmd.AddOption(outputOpt); cmd.AddOption(saveProfileOpt); cmd.AddOption(forceAutoApplyOpt);
    cmd.AddOption(breakGlassAckOpt); cmd.AddOption(breakGlassReasonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var packId = ctx.ParseResult.GetValueForOption(packIdOpt) ?? string.Empty;
      var profileId = ctx.ParseResult.GetValueForOption(profileIdOpt) ?? string.Empty;
      var profileJson = ctx.ParseResult.GetValueForOption(profileJsonOpt) ?? string.Empty;
      var bundleId = ctx.ParseResult.GetValueForOption(bundleIdOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var saveProfile = ctx.ParseResult.GetValueForOption(saveProfileOpt);
      var forceAutoApply = ctx.ParseResult.GetValueForOption(forceAutoApplyOpt);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);

      var breakGlassValidationError = ValidateBreakGlassArguments(
        forceAutoApply,
        breakGlassAck,
        breakGlassReason,
        "--force-auto-apply");
      if (breakGlassValidationError != null)
        throw new ArgumentException(breakGlassValidationError);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildCommands");
      logger.LogInformation("build-bundle started: packId={PackId}, forceAutoApply={ForceAutoApply}", packId, forceAutoApply);

      if (string.IsNullOrWhiteSpace(profileId) && string.IsNullOrWhiteSpace(profileJson))
        throw new ArgumentException("Provide --profile-id or --profile-json.");
      if (!string.IsNullOrWhiteSpace(profileId) && !string.IsNullOrWhiteSpace(profileJson))
        throw new ArgumentException("Provide only one of --profile-id or --profile-json.");

      var packs = host.Services.GetRequiredService<IContentPackRepository>();
      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
      var controls = host.Services.GetRequiredService<IControlRepository>();
      var builder = host.Services.GetRequiredService<BundleBuilder>();
      var audit = host.Services.GetService<IAuditTrailService>();

      var pack = await packs.GetAsync(packId, CancellationToken.None) ?? throw new ArgumentException("Pack not found: " + packId);

      Profile profile;
      if (!string.IsNullOrWhiteSpace(profileId))
      {
        profile = await profiles.GetAsync(profileId, CancellationToken.None) ?? throw new ArgumentException("Profile not found: " + profileId);
      }
      else
      {
        var json = File.ReadAllText(profileJson!);
        profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("Invalid profile JSON.");
        if (string.IsNullOrWhiteSpace(profile.ProfileId)) profile.ProfileId = Guid.NewGuid().ToString("n");
        if (saveProfile) await profiles.SaveAsync(profile, CancellationToken.None);
      }

      var list = await controls.ListControlsAsync(pack.PackId, CancellationToken.None);
      var overlays = new List<Overlay>();
      if (profile.OverlayIds != null && profile.OverlayIds.Count > 0)
        foreach (var oid in profile.OverlayIds)
        {
          if (string.IsNullOrWhiteSpace(oid)) continue;
          var ov = await overlaysRepo.GetAsync(oid, CancellationToken.None);
          if (ov != null) overlays.Add(ov);
        }

      var result = await builder.BuildAsync(new BundleBuildRequest
      {
        BundleId = bundleId ?? string.Empty,
        OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output,
        Pack = pack, Profile = profile, Controls = list, Overlays = overlays,
        ToolVersion = "0.1.0-dev", ForceAutoApply = forceAutoApply
      }, CancellationToken.None);

      await RecordBreakGlassAuditAsync(
        audit,
        forceAutoApply,
        "build-bundle",
        result.BundleRoot,
        "force-auto-apply",
        breakGlassReason,
        CancellationToken.None);

      logger.LogInformation("build-bundle completed: bundleRoot={BundleRoot}", result.BundleRoot);
      Console.WriteLine("Bundle created: " + result.BundleRoot);
      Console.WriteLine("Manifest: " + result.ManifestPath);
      var gatePath = Path.Combine(result.BundleRoot, BundlePaths.ReportsDirectory, "automation_gate.json");
      if (File.Exists(gatePath)) Console.WriteLine("Automation gate: " + gatePath);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterOrchestrate(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("orchestrate", "Run build -> apply -> verify using a bundle");
    var bOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var dscOpt = new Option<string>("--dsc-path", () => string.Empty, "DSC MOF path (optional)");
    var dscVOpt = new Option<bool>("--dsc-verbose", "Verbose DSC output");
    var psModOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module path");
    var psDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file");
    var psOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG output");
    var psVOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");
    var evalOpt = new Option<string>("--evaluate-stig", () => string.Empty, "Evaluate-STIG root");
    var evalArgsOpt = new Option<string>("--evaluate-args", () => string.Empty, "Evaluate-STIG args");
    var scapOpt = new Option<string>("--scap-cmd", () => string.Empty, "SCAP/SCC command path");
    var scapArgsOpt = new Option<string>("--scap-args", () => string.Empty, "SCAP args");
    var scapLabelOpt = new Option<string>("--scap-label", () => string.Empty, "Label for SCAP tool");
    var dscScanOpt = new Option<bool>("--dsc-scan", "Enable read-only DSC compliance scan via Test-DscConfiguration");
    var dscScanVOpt = new Option<bool>("--dsc-scan-verbose", "Verbose DSC scan output");
    var dscScanLabelOpt = new Option<string>("--dsc-scan-label", () => string.Empty, "Label for DSC scan tool");
    var skipSnapOpt = new Option<bool>("--skip-snapshot", "Skip pre-apply snapshot during orchestration (high risk)");
    var breakGlassAckOpt = new Option<bool>(BreakGlassAckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");

    foreach (var o in new Option[] { bOpt, dscOpt, dscVOpt, psModOpt, psDataOpt, psOutOpt, psVOpt, evalOpt, evalArgsOpt, scapOpt, scapArgsOpt, scapLabelOpt, dscScanOpt, dscScanVOpt, dscScanLabelOpt, skipSnapOpt, breakGlassAckOpt, breakGlassReasonOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnapOpt);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);
      var breakGlassValidationError = ValidateBreakGlassArguments(
        skipSnapshot,
        breakGlassAck,
        breakGlassReason,
        "--skip-snapshot");
      if (breakGlassValidationError != null)
        throw new ArgumentException(breakGlassValidationError);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildCommands");
      logger.LogInformation("orchestrate started: bundle={Bundle}", bundle);

      var orchestrator = host.Services.GetRequiredService<BundleOrchestrator>();
      await orchestrator.OrchestrateAsync(new OrchestrateRequest
      {
        BundleRoot = bundle,
        SkipSnapshot = skipSnapshot,
        BreakGlassAcknowledged = breakGlassAck,
        BreakGlassReason = breakGlassReason,
        DscMofPath = NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        PowerStigModulePath = NullIfEmpty(ctx, psModOpt), PowerStigDataFile = NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt),
        EvaluateStigRoot = NullIfEmpty(ctx, evalOpt), EvaluateStigArgs = NullIfEmpty(ctx, evalArgsOpt),
        ScapCommandPath = NullIfEmpty(ctx, scapOpt), ScapArgs = NullIfEmpty(ctx, scapArgsOpt),
        ScapToolLabel = NullIfEmpty(ctx, scapLabelOpt),
        DscScanEnabled = ctx.ParseResult.GetValueForOption(dscScanOpt),
        DscScanVerbose = ctx.ParseResult.GetValueForOption(dscScanVOpt),
        DscScanToolLabel = NullIfEmpty(ctx, dscScanLabelOpt)
      }, CancellationToken.None);
      logger.LogInformation("orchestrate completed: bundle={Bundle}", bundle);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterApplyRun(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("apply-run", "Run apply phase using a bundle and optional script");
    var bOpt = new Option<string>("--bundle", "Path to bundle root") { IsRequired = true };
    var modeOpt = new Option<string>("--mode", () => string.Empty, "Override mode: AuditOnly|Safe|Full");
    var scriptOpt = new Option<string>("--script", () => string.Empty, "Optional PowerShell script");
    var scriptArgsOpt = new Option<string>("--script-args", () => string.Empty, "Script args");
    var dscOpt = new Option<string>("--dsc-path", () => string.Empty, "DSC MOF directory");
    var dscVOpt = new Option<bool>("--dsc-verbose", "Verbose DSC output");
    var skipSnap = new Option<bool>("--skip-snapshot", "Skip snapshot generation");
    var breakGlassAckOpt = new Option<bool>(BreakGlassAckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");
    var psModOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module folder");
    var psDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file");
    var psOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG MOF output folder");
    var psVOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");

    foreach (var o in new Option[] { bOpt, modeOpt, scriptOpt, scriptArgsOpt, dscOpt, dscVOpt, skipSnap, breakGlassAckOpt, breakGlassReasonOpt, psModOpt, psDataOpt, psOutOpt, psVOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? string.Empty;
      HardeningMode? parsedMode = null;
      if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse<HardeningMode>(mode, true, out var m)) parsedMode = m;

      var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnap);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);
      var breakGlassValidationError = ValidateBreakGlassArguments(
        skipSnapshot,
        breakGlassAck,
        breakGlassReason,
        "--skip-snapshot");
      if (breakGlassValidationError != null)
        throw new ArgumentException(breakGlassValidationError);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildCommands");
      logger.LogInformation("apply-run started: bundle={Bundle}, mode={Mode}", bundle, mode);

      var audit = host.Services.GetService<IAuditTrailService>();
      await RecordBreakGlassAuditAsync(
        audit,
        skipSnapshot,
        "apply-run",
        bundle,
        "skip-snapshot",
        breakGlassReason,
        CancellationToken.None);

      var runner = host.Services.GetRequiredService<STIGForge.Apply.ApplyRunner>();
      var result = await runner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = bundle, ModeOverride = parsedMode,
        ScriptPath = NullIfEmpty(ctx, scriptOpt), ScriptArgs = NullIfEmpty(ctx, scriptArgsOpt),
        DscMofPath = NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        SkipSnapshot = skipSnapshot,
        PowerStigModulePath = NullIfEmpty(ctx, psModOpt), PowerStigDataFile = NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt)
      }, CancellationToken.None);
      logger.LogInformation("apply-run completed: log={LogPath}", result.LogPath);
      Console.WriteLine("Apply completed. Log: " + result.LogPath);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterMissionAutopilot(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("mission-autopilot", "Run seamless import/build/apply/verify pipeline with optional NIWC enhanced SCAP ingestion");

    var niwcZipOpt = new Option<string>("--niwc-enhanced-zip", () => string.Empty, "Path to NIWC Atlantic enhanced SCAP ZIP (imports before build)");
    var sourceLabelOpt = new Option<string>("--source-label", () => "niwc_atlantic_enhanced_scap", "Source label used for NIWC import");
    var packIdOpt = new Option<string>("--pack-id", () => string.Empty, "Existing pack id (used when import is skipped)");
    var packNameOpt = new Option<string>("--pack-name", () => string.Empty, "Optional pack name override for NIWC import");
    var niwcSourceUrlOpt = new Option<string>("--niwc-source-url", () => "https://github.com/niwc-atlantic/scap-content-library", "NIWC Atlantic source URL (repo or direct ZIP)");
    var disaStigUrlOpt = new Option<string>("--disa-stig-url", () => string.Empty, "DISA STIG source URL (direct ZIP or downloads page)");
    var allowRemoteDownloadsOpt = new Option<bool>("--allow-remote-downloads", () => true, "Allow automatic remote source downloads when local pack path is not provided");

    var profileIdOpt = new Option<string>("--profile-id", () => string.Empty, "Existing profile id");
    var profileJsonOpt = new Option<string>("--profile-json", () => string.Empty, "Path to profile JSON (overrides generated profile)");
    var saveProfileOpt = new Option<bool>("--save-profile", () => true, "Save generated/JSON profile to repository");
    var profileNameOpt = new Option<string>("--profile-name", () => "Autopilot Classified Win11", "Generated profile name when profile id/json not provided");
    var modeOpt = new Option<string>("--mode", () => HardeningMode.Safe.ToString(), "Generated profile mode: AuditOnly|Safe|Full");
    var classificationOpt = new Option<string>("--classification", () => ClassificationMode.Classified.ToString(), "Generated profile classification: Classified|Unclassified|Mixed");
    var osTargetOpt = new Option<string>("--os-target", () => OsTarget.Win11.ToString(), "Generated profile OS target: Win11|Server2019");
    var roleTemplateOpt = new Option<string>("--role-template", () => RoleTemplate.Workstation.ToString(), "Generated profile role: Workstation|MemberServer|DomainController|LabVm");
    var autoNaOpt = new Option<bool>("--auto-na", () => true, "Enable auto-NA out-of-scope controls for generated profile");
    var naConfidenceOpt = new Option<string>("--na-confidence", () => Confidence.High.ToString(), "Auto-NA confidence threshold: High|Medium|Low");
    var naCommentOpt = new Option<string>("--na-comment", () => "Auto-NA (classification scope)", "Default NA reason/comment for generated profile");

    var bundleIdOpt = new Option<string>("--bundle-id", () => string.Empty, "Bundle id override");
    var outputOpt = new Option<string>("--output", () => string.Empty, "Bundle output path override");

    var autoDetectToolsOpt = new Option<bool>("--auto-detect-tools", () => true, "Auto-detect Evaluate-STIG, SCC, and PowerSTIG paths when omitted");
    var psModOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module path (.psd1/.psm1 or module directory)");
    var powerStigSourceUrlOpt = new Option<string>("--powerstig-source-url", () => "https://github.com/microsoft/PowerStig", "PowerStig source URL (repo or direct ZIP)");
    var psDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file (.psd1)");
    var psOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG output folder");
    var psVOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");
    var evalOpt = new Option<string>("--evaluate-stig", () => string.Empty, "Evaluate-STIG root folder");
    var evalArgsOpt = new Option<string>("--evaluate-args", () => string.Empty, "Evaluate-STIG arguments");
    var scapOpt = new Option<string>("--scap-cmd", () => string.Empty, "SCAP/SCC command path");
    var scapArgsOpt = new Option<string>("--scap-args", () => string.Empty, "SCAP/SCC arguments (e.g. -s load,scan -r <dir> -u -o results,ckl)");
    var scapLabelOpt = new Option<string>("--scap-label", () => "DISA SCAP", "SCAP tool label");

    var skipSnapOpt = new Option<bool>("--skip-snapshot", "Skip snapshot generation for image pipeline scenarios (high risk)");
    var breakGlassAckOpt = new Option<bool>(BreakGlassAckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");

    foreach (var option in new Option[]
    {
      niwcZipOpt, sourceLabelOpt, packIdOpt, packNameOpt,
      niwcSourceUrlOpt, disaStigUrlOpt, allowRemoteDownloadsOpt,
      profileIdOpt, profileJsonOpt, saveProfileOpt, profileNameOpt, modeOpt, classificationOpt, osTargetOpt, roleTemplateOpt, autoNaOpt, naConfidenceOpt, naCommentOpt,
      bundleIdOpt, outputOpt,
      autoDetectToolsOpt, psModOpt, powerStigSourceUrlOpt, psDataOpt, psOutOpt, psVOpt, evalOpt, evalArgsOpt, scapOpt, scapArgsOpt, scapLabelOpt,
      skipSnapOpt, breakGlassAckOpt, breakGlassReasonOpt
    })
      cmd.AddOption(option);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var niwcZip = ctx.ParseResult.GetValueForOption(niwcZipOpt) ?? string.Empty;
      var niwcSourceUrl = ctx.ParseResult.GetValueForOption(niwcSourceUrlOpt) ?? "https://github.com/niwc-atlantic/scap-content-library";
      var disaStigUrl = ctx.ParseResult.GetValueForOption(disaStigUrlOpt) ?? string.Empty;
      var allowRemoteDownloads = ctx.ParseResult.GetValueForOption(allowRemoteDownloadsOpt);
      var packIdInput = ctx.ParseResult.GetValueForOption(packIdOpt) ?? string.Empty;
      var packName = ctx.ParseResult.GetValueForOption(packNameOpt) ?? string.Empty;
      var sourceLabel = ctx.ParseResult.GetValueForOption(sourceLabelOpt) ?? "niwc_atlantic_enhanced_scap";
      var profileId = ctx.ParseResult.GetValueForOption(profileIdOpt) ?? string.Empty;
      var profileJson = ctx.ParseResult.GetValueForOption(profileJsonOpt) ?? string.Empty;
      var saveProfile = ctx.ParseResult.GetValueForOption(saveProfileOpt);
      var generatedProfileName = ctx.ParseResult.GetValueForOption(profileNameOpt) ?? "Autopilot Classified Win11";
      var generatedMode = ctx.ParseResult.GetValueForOption(modeOpt) ?? HardeningMode.Safe.ToString();
      var generatedClassification = ctx.ParseResult.GetValueForOption(classificationOpt) ?? ClassificationMode.Classified.ToString();
      var generatedOsTarget = ctx.ParseResult.GetValueForOption(osTargetOpt) ?? OsTarget.Win11.ToString();
      var generatedRoleTemplate = ctx.ParseResult.GetValueForOption(roleTemplateOpt) ?? RoleTemplate.Workstation.ToString();
      var generatedAutoNa = ctx.ParseResult.GetValueForOption(autoNaOpt);
      var generatedNaConfidence = ctx.ParseResult.GetValueForOption(naConfidenceOpt) ?? Confidence.High.ToString();
      var generatedNaComment = ctx.ParseResult.GetValueForOption(naCommentOpt) ?? "Auto-NA (classification scope)";
      var powerStigSourceUrl = ctx.ParseResult.GetValueForOption(powerStigSourceUrlOpt) ?? "https://github.com/microsoft/PowerStig";
      var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnapOpt);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);

      if (string.IsNullOrWhiteSpace(niwcZip) && string.IsNullOrWhiteSpace(packIdInput) && string.IsNullOrWhiteSpace(disaStigUrl) && !allowRemoteDownloads)
        throw new ArgumentException("Provide --pack-id, --niwc-enhanced-zip, or --disa-stig-url when --allow-remote-downloads is disabled.");

      if (!string.IsNullOrWhiteSpace(profileId) && !string.IsNullOrWhiteSpace(profileJson))
        throw new ArgumentException("Provide only one of --profile-id or --profile-json.");

      var breakGlassValidationError = ValidateBreakGlassArguments(
        skipSnapshot,
        breakGlassAck,
        breakGlassReason,
        "--skip-snapshot");
      if (breakGlassValidationError != null)
        throw new ArgumentException(breakGlassValidationError);

      using var host = buildHost();
      await host.StartAsync();

      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildCommands");
      var paths = host.Services.GetRequiredService<IPathBuilder>();
      var importer = host.Services.GetRequiredService<ContentPackImporter>();
      var packs = host.Services.GetRequiredService<IContentPackRepository>();
      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var overlaysRepo = host.Services.GetRequiredService<IOverlayRepository>();
      var controlsRepo = host.Services.GetRequiredService<IControlRepository>();
      var scope = host.Services.GetRequiredService<IClassificationScopeService>();
      var manualAnswers = host.Services.GetRequiredService<ManualAnswerService>();
      var builder = host.Services.GetRequiredService<BundleBuilder>();
      var orchestrator = host.Services.GetRequiredService<BundleOrchestrator>();
      var audit = host.Services.GetService<IAuditTrailService>();
      var airGapTransferRoot = GetAirGapTransferRoot(paths);
      var downloadedArtifacts = new List<string>();

      logger.LogInformation("mission-autopilot started: niwcZip={NiwcZip}, packId={PackId}, disaUrl={DisaUrl}", niwcZip, packIdInput, disaStigUrl);

      string effectivePackId = packIdInput;

      if (string.IsNullOrWhiteSpace(effectivePackId) && !string.IsNullOrWhiteSpace(niwcZip))
      {
        if (!File.Exists(niwcZip))
          throw new FileNotFoundException("NIWC enhanced SCAP ZIP not found", niwcZip);

        var effectivePackName = string.IsNullOrWhiteSpace(packName)
          ? "NIWC_Enhanced_" + Path.GetFileNameWithoutExtension(niwcZip)
          : packName;

        var imported = await importer.ImportZipAsync(niwcZip, effectivePackName, sourceLabel, CancellationToken.None);
        effectivePackId = imported.PackId;
        Console.WriteLine("Imported NIWC enhanced SCAP pack: " + imported.Name + " (" + imported.PackId + ")");
      }

      if (string.IsNullOrWhiteSpace(effectivePackId) && !string.IsNullOrWhiteSpace(disaStigUrl))
      {
        var downloadedDisaZip = await DownloadSourceZipAsync(disaStigUrl, "disa-stig", airGapTransferRoot, CancellationToken.None);
        downloadedArtifacts.Add(downloadedDisaZip);
        var disaPackName = string.IsNullOrWhiteSpace(packName)
          ? "DISA_STIG_" + Path.GetFileNameWithoutExtension(downloadedDisaZip)
          : packName;
        var imported = await importer.ImportZipAsync(downloadedDisaZip, disaPackName, "disa_stig_library", CancellationToken.None);
        effectivePackId = imported.PackId;
        Console.WriteLine("Imported DISA STIG pack: " + imported.Name + " (" + imported.PackId + ")");
        Console.WriteLine("Saved DISA source archive: " + downloadedDisaZip);
      }

      if (string.IsNullOrWhiteSpace(effectivePackId) && allowRemoteDownloads)
      {
        var downloadedNiwcZip = await DownloadSourceZipAsync(niwcSourceUrl, "niwc-enhanced", airGapTransferRoot, CancellationToken.None);
        downloadedArtifacts.Add(downloadedNiwcZip);
        var effectivePackName = string.IsNullOrWhiteSpace(packName)
          ? "NIWC_Enhanced_" + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmm")
          : packName;
        var imported = await importer.ImportZipAsync(downloadedNiwcZip, effectivePackName, sourceLabel, CancellationToken.None);
        effectivePackId = imported.PackId;
        Console.WriteLine("Downloaded and imported NIWC enhanced SCAP pack: " + imported.Name + " (" + imported.PackId + ")");
        Console.WriteLine("Saved NIWC source archive: " + downloadedNiwcZip);
      }

      if (string.IsNullOrWhiteSpace(effectivePackId))
        throw new ArgumentException("Unable to resolve a content pack. Provide --pack-id, --niwc-enhanced-zip, --disa-stig-url, or enable --allow-remote-downloads.");

      var pack = await packs.GetAsync(effectivePackId, CancellationToken.None)
        ?? throw new ArgumentException("Pack not found: " + effectivePackId);

      var generatedProfile = BuildGeneratedProfile(
        generatedProfileName,
        generatedMode,
        generatedClassification,
        generatedOsTarget,
        generatedRoleTemplate,
        generatedAutoNa,
        generatedNaConfidence,
        generatedNaComment);
      var profile = await ResolveProfileAsync(profiles, profileId, profileJson, generatedProfile, saveProfile, CancellationToken.None);

      var controlList = await controlsRepo.ListControlsAsync(pack.PackId, CancellationToken.None);
      var overlays = await LoadSelectedOverlaysAsync(overlaysRepo, profile, CancellationToken.None);

      var buildResult = await builder.BuildAsync(new BundleBuildRequest
      {
        BundleId = ctx.ParseResult.GetValueForOption(bundleIdOpt) ?? string.Empty,
        OutputRoot = NullIfEmpty(ctx, outputOpt),
        Pack = pack,
        Profile = profile,
        Controls = controlList,
        Overlays = overlays,
        ToolVersion = "0.1.0-dev"
      }, CancellationToken.None);

      var autoNaSeeded = SeedAutoNaAnswers(scope, manualAnswers, buildResult.BundleRoot, pack, profile, controlList);

      var autoDetectTools = ctx.ParseResult.GetValueForOption(autoDetectToolsOpt);
      var evaluateStigRoot = NullIfEmpty(ctx, evalOpt);
      var scapCommandPath = NullIfEmpty(ctx, scapOpt);
      var powerStigModulePath = NullIfEmpty(ctx, psModOpt);

      if (autoDetectTools)
      {
        evaluateStigRoot ??= TryAutoDetectEvaluateStigRoot();
        scapCommandPath ??= TryAutoDetectScapCommand();
        powerStigModulePath ??= TryAutoDetectPowerStigModulePath();
      }

      if (string.IsNullOrWhiteSpace(powerStigModulePath) && allowRemoteDownloads)
      {
        var powerStigRemote = await DownloadAndExtractPowerStigModuleAsync(powerStigSourceUrl, airGapTransferRoot, CancellationToken.None);
        powerStigModulePath = powerStigRemote.ModulePath;
        if (!string.IsNullOrWhiteSpace(powerStigRemote.ArchivePath)) downloadedArtifacts.Add(powerStigRemote.ArchivePath);
        if (!string.IsNullOrWhiteSpace(powerStigModulePath))
          Console.WriteLine("PowerStig module resolved from source URL: " + powerStigModulePath);
        if (!string.IsNullOrWhiteSpace(powerStigRemote.ArchivePath))
          Console.WriteLine("Saved PowerStig source archive: " + powerStigRemote.ArchivePath);
      }

      if (string.IsNullOrWhiteSpace(evaluateStigRoot) && string.IsNullOrWhiteSpace(scapCommandPath))
        throw new ArgumentException("No scanner configured. Provide --evaluate-stig and/or --scap-cmd, or enable --auto-detect-tools with installed tools.");

      await RecordBreakGlassAuditAsync(
        audit,
        skipSnapshot,
        "mission-autopilot",
        buildResult.BundleRoot,
        "skip-snapshot",
        breakGlassReason,
        CancellationToken.None);

      await orchestrator.OrchestrateAsync(new OrchestrateRequest
      {
        BundleRoot = buildResult.BundleRoot,
        SkipSnapshot = skipSnapshot,
        BreakGlassAcknowledged = breakGlassAck,
        BreakGlassReason = breakGlassReason,
        PowerStigModulePath = powerStigModulePath,
        PowerStigDataFile = NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = NullIfEmpty(ctx, psOutOpt),
        PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt),
        EvaluateStigRoot = evaluateStigRoot,
        EvaluateStigArgs = NullIfEmpty(ctx, evalArgsOpt),
        ScapCommandPath = scapCommandPath,
        ScapArgs = NullIfEmpty(ctx, scapArgsOpt) ?? string.Empty,
        ScapToolLabel = NullIfEmpty(ctx, scapLabelOpt)
      }, CancellationToken.None);

      var manualTemplatePath = Path.Combine(buildResult.BundleRoot, BundlePaths.ManualDirectory, "answerfile.template.json");
      var manualAnswersPath = Path.Combine(buildResult.BundleRoot, BundlePaths.ManualDirectory, BundlePaths.AnswersFileName);
      Console.WriteLine("Mission autopilot completed.");
      Console.WriteLine("Bundle: " + buildResult.BundleRoot);
      Console.WriteLine("Manifest: " + buildResult.ManifestPath);
      Console.WriteLine("Manual template: " + manualTemplatePath);
      Console.WriteLine("Manual answers: " + manualAnswersPath);
      Console.WriteLine("Auto-NA seeded answers: " + autoNaSeeded);
      Console.WriteLine("Verify outputs: " + Path.Combine(buildResult.BundleRoot, BundlePaths.VerifyDirectory));
      Console.WriteLine("Coverage reports: " + Path.Combine(buildResult.BundleRoot, BundlePaths.ReportsDirectory));
      if (downloadedArtifacts.Count > 0)
      {
        Console.WriteLine("Air-gap transfer cache: " + airGapTransferRoot);
        foreach (var artifact in downloadedArtifacts)
          Console.WriteLine("Cached artifact: " + artifact);
      }

      if (skipSnapshot)
        Console.WriteLine("Image pipeline mode: snapshot skipped. Capture/golden-image steps remain external to STIGForge.");

      logger.LogInformation("mission-autopilot completed: bundle={Bundle}, autoNaSeeded={Seeded}", buildResult.BundleRoot, autoNaSeeded);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static string? NullIfEmpty(InvocationContext ctx, Option<string> opt)
  {
    var val = ctx.ParseResult.GetValueForOption(opt);
    return string.IsNullOrWhiteSpace(val) ? null : val;
  }

  private static Profile BuildGeneratedProfile(
    string profileName,
    string modeValue,
    string classificationValue,
    string osTargetValue,
    string roleTemplateValue,
    bool autoNa,
    string naConfidenceValue,
    string naCommentValue)
  {
    var name = (profileName ?? "Autopilot Classified Win11").Trim();
    if (string.IsNullOrWhiteSpace(name))
      name = "Autopilot Classified Win11";

    var mode = ParseEnumOrThrow<HardeningMode>(modeValue ?? HardeningMode.Safe.ToString(), "--mode");
    var classification = ParseEnumOrThrow<ClassificationMode>(classificationValue ?? ClassificationMode.Classified.ToString(), "--classification");
    var osTarget = ParseEnumOrThrow<OsTarget>(osTargetValue ?? OsTarget.Win11.ToString(), "--os-target");
    var role = ParseEnumOrThrow<RoleTemplate>(roleTemplateValue ?? RoleTemplate.Workstation.ToString(), "--role-template");
    var naConfidence = ParseEnumOrThrow<Confidence>(naConfidenceValue ?? Confidence.High.ToString(), "--na-confidence");
    var naComment = (naCommentValue ?? "Auto-NA (classification scope)").Trim();

    return new Profile
    {
      ProfileId = Guid.NewGuid().ToString("n"),
      Name = name,
      OsTarget = osTarget,
      RoleTemplate = role,
      HardeningMode = mode,
      ClassificationMode = classification,
      NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = autoNa,
        ConfidenceThreshold = naConfidence,
        DefaultNaCommentTemplate = string.IsNullOrWhiteSpace(naComment) ? "Auto-NA (classification scope)" : naComment
      },
      AutomationPolicy = new AutomationPolicy
      {
        Mode = AutomationMode.Standard,
        NewRuleGraceDays = 30,
        AutoApplyRequiresMapping = true,
        ReleaseDateSource = ReleaseDateSource.ContentPack
      },
      OverlayIds = Array.Empty<string>()
    };
  }

  private static async Task<Profile> ResolveProfileAsync(
    IProfileRepository profiles,
    string profileId,
    string profileJson,
    Profile generatedProfile,
    bool saveProfile,
    CancellationToken ct)
  {
    if (!string.IsNullOrWhiteSpace(profileId))
      return await profiles.GetAsync(profileId, ct) ?? throw new ArgumentException("Profile not found: " + profileId);

    if (!string.IsNullOrWhiteSpace(profileJson))
    {
      if (!File.Exists(profileJson))
        throw new FileNotFoundException("Profile JSON not found", profileJson);

      var json = File.ReadAllText(profileJson);
      var profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new ArgumentException("Invalid profile JSON.");

      if (string.IsNullOrWhiteSpace(profile.ProfileId))
        profile.ProfileId = Guid.NewGuid().ToString("n");

      if (saveProfile)
        await profiles.SaveAsync(profile, ct);

      return profile;
    }

    if (saveProfile)
      await profiles.SaveAsync(generatedProfile, ct);

    return generatedProfile;
  }

  private static async Task<IReadOnlyList<Overlay>> LoadSelectedOverlaysAsync(IOverlayRepository overlaysRepo, Profile profile, CancellationToken ct)
  {
    var overlays = new List<Overlay>();
    foreach (var overlayId in profile.OverlayIds ?? Array.Empty<string>())
    {
      if (string.IsNullOrWhiteSpace(overlayId))
        continue;

      var overlay = await overlaysRepo.GetAsync(overlayId, ct);
      if (overlay != null)
        overlays.Add(overlay);
    }

    return overlays;
  }

  private static int SeedAutoNaAnswers(
    IClassificationScopeService scope,
    ManualAnswerService manualAnswers,
    string bundleRoot,
    ContentPack pack,
    Profile profile,
    IReadOnlyList<ControlRecord> controls)
  {
    var compiled = scope.Compile(profile, controls);
    var count = 0;

    foreach (var compiledControl in compiled.Controls)
    {
      if (compiledControl.Status != ControlStatus.NotApplicable)
        continue;

      var reason = string.IsNullOrWhiteSpace(compiledControl.Comment)
        ? (string.IsNullOrWhiteSpace(profile.NaPolicy.DefaultNaCommentTemplate)
          ? "Auto-NA (classification scope)"
          : profile.NaPolicy.DefaultNaCommentTemplate)
        : compiledControl.Comment;

      manualAnswers.SaveAnswer(
        bundleRoot,
        new ManualAnswer
        {
          RuleId = compiledControl.Control.ExternalIds.RuleId,
          VulnId = compiledControl.Control.ExternalIds.VulnId,
          Status = ControlStatusStrings.NotApplicable,
          Reason = reason,
          Comment = "Auto-generated by mission-autopilot"
        },
        requireReasonForDecision: false,
        profileId: profile.ProfileId,
        packId: pack.PackId);

      count++;
    }

    return count;
  }

  private static T ParseEnumOrThrow<T>(string value, string optionName) where T : struct, Enum
  {
    if (Enum.TryParse<T>(value, true, out var parsed))
      return parsed;

    throw new ArgumentException($"Invalid value '{value}' for {optionName}.");
  }

  private static string? TryAutoDetectEvaluateStigRoot()
  {
    var candidates = new[]
    {
      Path.GetFullPath(@".\.stigforge\tools\Evaluate-STIG\Evaluate-STIG"),
      Path.GetFullPath(@".\.stigforge\tools\Evaluate-STIG"),
      @"C:\Evaluate-STIG",
      @"C:\Program Files\Evaluate-STIG"
    };

    foreach (var candidate in candidates)
    {
      var scriptPath = Path.Combine(candidate, "Evaluate-STIG.ps1");
      if (File.Exists(scriptPath))
        return candidate;
    }

    return null;
  }

  private static string? TryAutoDetectScapCommand()
  {
    var candidates = new[]
    {
      Path.GetFullPath(@".\.stigforge\tools\SCC\scc.exe"),
      @"C:\SCC\scc.exe",
      @"C:\Program Files\SCC\scc.exe",
      @"C:\Program Files (x86)\SCC\scc.exe"
    };

    foreach (var candidate in candidates)
    {
      if (File.Exists(candidate))
        return candidate;
    }

    return null;
  }

  private static string? TryAutoDetectPowerStigModulePath()
  {
    var modulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
    var separators = modulePath.Contains(';') ? new[] { ';' } : new[] { Path.PathSeparator };
    foreach (var segment in modulePath.Split(separators, StringSplitOptions.RemoveEmptyEntries))
    {
      var root = segment.Trim();
      if (root.Length == 0 || !Directory.Exists(root))
        continue;

      var directoryCandidate = Path.Combine(root, "PowerSTIG");
      if (Directory.Exists(directoryCandidate))
      {
        var psd1 = Path.Combine(directoryCandidate, "PowerSTIG.psd1");
        if (File.Exists(psd1))
          return psd1;

        return directoryCandidate;
      }
    }

    return null;
  }

  private static string GetAirGapTransferRoot(IPathBuilder paths)
  {
    var root = Path.Combine(paths.GetAppDataRoot(), "airgap-transfer");
    Directory.CreateDirectory(root);
    return root;
  }

  private static string CreateDownloadSessionFolder(string airGapTransferRoot, string sourceName)
  {
    var safeSourceName = SanitizeFileSegment(sourceName);
    var session = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    var folder = Path.Combine(airGapTransferRoot, safeSourceName, session);
    Directory.CreateDirectory(folder);
    return folder;
  }

  private static string SanitizeFileSegment(string value)
  {
    var input = string.IsNullOrWhiteSpace(value) ? "source" : value.Trim();
    var chars = input.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray();
    var sanitized = new string(chars);
    return string.IsNullOrWhiteSpace(sanitized) ? "source" : sanitized;
  }

  private static async Task<string> DownloadSourceZipAsync(string sourceUrl, string sourceName, string airGapTransferRoot, CancellationToken ct)
  {
    var resolvedUrl = await ResolveDownloadUrlAsync(sourceUrl, ct).ConfigureAwait(false);

    if (!resolvedUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !resolvedUrl.Contains(".zip?", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException("Source URL must resolve to a .zip file: " + resolvedUrl);

    if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri))
      throw new ArgumentException("Invalid source URL: " + sourceUrl);

    var downloadRoot = CreateDownloadSessionFolder(airGapTransferRoot, sourceName);

    var fileName = Path.GetFileName(uri.LocalPath);
    if (string.IsNullOrWhiteSpace(fileName))
      fileName = sourceName + ".zip";
    if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
      fileName += ".zip";

    var destination = Path.Combine(downloadRoot, fileName);
    using var http = CreateHttpClient();
    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    await using (var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
    await using (var local = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
    {
      await remote.CopyToAsync(local, ct).ConfigureAwait(false);
    }

    return destination;
  }

  private static async Task<string> ResolveDownloadUrlAsync(string sourceUrl, CancellationToken ct)
  {
    var raw = (sourceUrl ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(raw))
      throw new ArgumentException("Source URL is required.");

    if (raw.Contains("github.com/microsoft/PowerStig", StringComparison.OrdinalIgnoreCase) && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
      return "https://github.com/microsoft/PowerStig/archive/refs/heads/master.zip";

    if (raw.Contains("github.com/niwc-atlantic/scap-content-library", StringComparison.OrdinalIgnoreCase) && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
      return "https://github.com/niwc-atlantic/scap-content-library/archive/refs/heads/main.zip";

    if (raw.Contains("cyber.mil/stigs/downloads", StringComparison.OrdinalIgnoreCase) && !raw.Contains(".zip", StringComparison.OrdinalIgnoreCase))
      return await ResolveFirstZipFromHtmlAsync(raw, ct).ConfigureAwait(false);

    return raw;
  }

  private static async Task<string> ResolveFirstZipFromHtmlAsync(string pageUrl, CancellationToken ct)
  {
    using var http = CreateHttpClient();
    var html = await http.GetStringAsync(pageUrl, ct).ConfigureAwait(false);
    var regex = new Regex("href\\s*=\\s*[\"'](?<u>[^\"'#>]+?\\.zip(?:\\?[^\"'#>]*)?)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    var matches = regex.Matches(html);
    if (matches.Count == 0)
      throw new ArgumentException("No downloadable .zip links found at source page: " + pageUrl);

    var baseUri = new Uri(pageUrl);
    var links = new List<string>();
    foreach (Match match in matches)
    {
      var candidate = match.Groups["u"].Value;
      if (string.IsNullOrWhiteSpace(candidate))
        continue;

      if (Uri.TryCreate(baseUri, candidate, out var absolute))
        links.Add(absolute.ToString());
    }

    if (links.Count == 0)
      throw new ArgumentException("Unable to resolve absolute .zip links from source page: " + pageUrl);

    var scapPreferred = links.FirstOrDefault(link => link.IndexOf("scap", StringComparison.OrdinalIgnoreCase) >= 0);
    var firstLink = links.FirstOrDefault();
    return scapPreferred ?? firstLink ?? throw new ArgumentException("No downloadable .zip links found at source page: " + pageUrl);
  }

  private static async Task<(string? ModulePath, string? ArchivePath)> DownloadAndExtractPowerStigModuleAsync(string sourceUrl, string airGapTransferRoot, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(sourceUrl))
      return (null, null);

    var zipPath = await DownloadSourceZipAsync(sourceUrl, "powerstig", airGapTransferRoot, ct).ConfigureAwait(false);
    var extractRoot = CreateDownloadSessionFolder(airGapTransferRoot, "powerstig-extracted");
    Directory.CreateDirectory(extractRoot);
    ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

    var psd1Candidates = Directory
      .GetFiles(extractRoot, "*.psd1", SearchOption.AllDirectories)
      .Where(path => Path.GetFileName(path).Equals("PowerSTIG.psd1", StringComparison.OrdinalIgnoreCase))
      .OrderBy(path => path.Length)
      .ToList();

    if (psd1Candidates.Count == 0)
      return (null, zipPath);

    return (psd1Candidates[0], zipPath);
  }

  private static HttpClient CreateHttpClient()
  {
    var client = new HttpClient
    {
      Timeout = TimeSpan.FromMinutes(5)
    };

    client.DefaultRequestHeaders.UserAgent.ParseAdd("STIGForge/1.0 (+mission-autopilot)");
    return client;
  }

  private static string? ValidateBreakGlassArguments(bool highRiskOptionEnabled, bool breakGlassAck, string? breakGlassReason, string optionName)
  {
    if (!highRiskOptionEnabled)
      return null;

    if (!breakGlassAck)
      return $"{optionName} is high risk. Add {BreakGlassAckOptionName} and provide a specific reason with {BreakGlassReasonOptionName}.";

    try
    {
      new ManualAnswerService().ValidateBreakGlassReason(breakGlassReason);
    }
    catch (ArgumentException)
    {
      return $"{BreakGlassReasonOptionName} is required for {optionName} and must be specific (minimum 8 characters).";
    }

    return null;
  }

  private static async Task RecordBreakGlassAuditAsync(
    IAuditTrailService? audit,
    bool highRiskOptionEnabled,
    string action,
    string target,
    string bypassName,
    string? reason,
    CancellationToken ct)
  {
    if (!highRiskOptionEnabled || audit == null)
      return;

    await audit.RecordAsync(new AuditEntry
    {
      Action = "break-glass",
      Target = string.IsNullOrWhiteSpace(target) ? action : target,
      Result = "acknowledged",
      Detail = $"Action={action}; Bypass={bypassName}; Reason={reason?.Trim()}",
      User = Environment.UserName,
      Machine = Environment.MachineName,
      Timestamp = DateTimeOffset.Now
    }, ct).ConfigureAwait(false);
  }
}

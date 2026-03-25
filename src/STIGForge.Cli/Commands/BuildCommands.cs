using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class BuildCommands
{
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
    var breakGlassAckOpt = new Option<bool>(BreakGlassService.AckOptionName, "Acknowledge high-risk break-glass use for --force-auto-apply");
    var breakGlassReasonOpt = new Option<string>(BreakGlassService.ReasonOptionName, "Reason for break-glass use (required with --force-auto-apply)");

    cmd.AddOption(packIdOpt); cmd.AddOption(profileIdOpt); cmd.AddOption(profileJsonOpt);
    cmd.AddOption(bundleIdOpt); cmd.AddOption(outputOpt); cmd.AddOption(saveProfileOpt); cmd.AddOption(forceAutoApplyOpt);
    cmd.AddOption(breakGlassAckOpt); cmd.AddOption(breakGlassReasonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var packId = ctx.ParseResult.GetValueForOption(packIdOpt) ?? string.Empty;
      var profileId = ctx.ParseResult.GetValueForOption(profileIdOpt) ?? string.Empty;
      var profileJson = ctx.ParseResult.GetValueForOption(profileJsonOpt) ?? string.Empty;
      var bundleId = ctx.ParseResult.GetValueForOption(bundleIdOpt) ?? string.Empty;
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var saveProfile = ctx.ParseResult.GetValueForOption(saveProfileOpt);
      var forceAutoApply = ctx.ParseResult.GetValueForOption(forceAutoApplyOpt);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);

      var breakGlassValidationError = BreakGlassService.ValidateArguments(
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

      var pack = await packs.GetAsync(packId, ct) ?? throw new ArgumentException("Pack not found: " + packId);

      Profile profile;
      if (!string.IsNullOrWhiteSpace(profileId))
      {
        profile = await profiles.GetAsync(profileId, ct) ?? throw new ArgumentException("Profile not found: " + profileId);
      }
      else
      {
        var json = await File.ReadAllTextAsync(profileJson!).ConfigureAwait(false);
        profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions.CaseInsensitive) ?? throw new ArgumentException("Invalid profile JSON.");
        if (string.IsNullOrWhiteSpace(profile.ProfileId)) profile.ProfileId = Guid.NewGuid().ToString("n");
        if (saveProfile) await profiles.SaveAsync(profile, ct);
      }

      var list = await controls.ListControlsAsync(pack.PackId, ct);
      var overlays = new List<Overlay>();
      if (profile.OverlayIds != null && profile.OverlayIds.Count > 0)
        foreach (var oid in profile.OverlayIds)
        {
          if (string.IsNullOrWhiteSpace(oid)) continue;
          var ov = await overlaysRepo.GetAsync(oid, ct);
          if (ov != null) overlays.Add(ov);
        }

      var result = await builder.BuildAsync(new BundleBuildRequest
      {
        BundleId = bundleId ?? string.Empty,
        OutputRoot = string.IsNullOrWhiteSpace(output) ? null : output,
        Pack = pack, Profile = profile, Controls = list, Overlays = overlays,
        ToolVersion = "0.1.0-dev", ForceAutoApply = forceAutoApply
      }, ct);

      await BreakGlassService.RecordAuditAsync(
        audit,
        forceAutoApply,
        "build-bundle",
        result.BundleRoot,
        "force-auto-apply",
        breakGlassReason,
        ct);

      logger.LogInformation("build-bundle completed: bundleRoot={BundleRoot}", result.BundleRoot);
      Console.WriteLine("Bundle created: " + result.BundleRoot);
      Console.WriteLine("Manifest: " + result.ManifestPath);
      var gatePath = Path.Combine(result.BundleRoot, "Reports", "automation_gate.json");
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
    var skipSnapOpt = new Option<bool>("--skip-snapshot", "Skip pre-apply snapshot during orchestration (high risk)");
    var breakGlassAckOpt = new Option<bool>(BreakGlassService.AckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassService.ReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");
    var dryRunOpt = new Option<bool>("--dry-run", "Simulate apply phase without changes (dry-run report)");
    var ruleIdOpt = new Option<string[]>("--rule-id", "Filter: only apply matching Rule IDs (repeatable)") { AllowMultipleArgumentsPerToken = true };
    var severityOpt = new Option<string[]>("--severity", "Filter: only apply matching severities (high/medium/low/CAT I/II/III)") { AllowMultipleArgumentsPerToken = true };
    var categoryOpt = new Option<string[]>("--category", "Filter: only apply matching categories (benchmark/SRG IDs)") { AllowMultipleArgumentsPerToken = true };

    foreach (var o in new Option[] { bOpt, dscOpt, dscVOpt, psModOpt, psDataOpt, psOutOpt, psVOpt, evalOpt, evalArgsOpt, scapOpt, scapArgsOpt, scapLabelOpt, skipSnapOpt, breakGlassAckOpt, breakGlassReasonOpt, dryRunOpt, ruleIdOpt, severityOpt, categoryOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnapOpt);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);
      var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
      var filterRuleIds = ctx.ParseResult.GetValueForOption(ruleIdOpt);
      var filterSeverities = ctx.ParseResult.GetValueForOption(severityOpt);
      var filterCategories = ctx.ParseResult.GetValueForOption(categoryOpt);
      var breakGlassValidationError = BreakGlassService.ValidateArguments(
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
        DryRun = dryRun,
        FilterRuleIds = filterRuleIds,
        FilterSeverities = filterSeverities,
        FilterCategories = filterCategories,
        DscMofPath = BuildCommandHelpers.NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        PowerStigModulePath = BuildCommandHelpers.NullIfEmpty(ctx, psModOpt), PowerStigDataFile = BuildCommandHelpers.NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = BuildCommandHelpers.NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt),
        EvaluateStigRoot = BuildCommandHelpers.NullIfEmpty(ctx, evalOpt), EvaluateStigArgs = BuildCommandHelpers.NullIfEmpty(ctx, evalArgsOpt),
        ScapCommandPath = BuildCommandHelpers.NullIfEmpty(ctx, scapOpt), ScapArgs = BuildCommandHelpers.NullIfEmpty(ctx, scapArgsOpt),
        ScapToolLabel = BuildCommandHelpers.NullIfEmpty(ctx, scapLabelOpt)
      }, ct);
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
    var breakGlassAckOpt = new Option<bool>(BreakGlassService.AckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassService.ReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");
    var psModOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module folder");
    var psDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file");
    var psOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG MOF output folder");
    var psVOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");
    var dryRunOpt = new Option<bool>("--dry-run", "Simulate all steps without making changes (dry-run report)");
    var ruleIdOpt = new Option<string[]>("--rule-id", "Filter: only apply matching Rule IDs (repeatable)") { AllowMultipleArgumentsPerToken = true };
    var severityOpt = new Option<string[]>("--severity", "Filter: only apply matching severities (high/medium/low/CAT I/II/III)") { AllowMultipleArgumentsPerToken = true };
    var categoryOpt = new Option<string[]>("--category", "Filter: only apply matching categories (benchmark/SRG IDs)") { AllowMultipleArgumentsPerToken = true };

    foreach (var o in new Option[] { bOpt, modeOpt, scriptOpt, scriptArgsOpt, dscOpt, dscVOpt, skipSnap, breakGlassAckOpt, breakGlassReasonOpt, psModOpt, psDataOpt, psOutOpt, psVOpt, dryRunOpt, ruleIdOpt, severityOpt, categoryOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? string.Empty;
      HardeningMode? parsedMode = null;
      if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse<HardeningMode>(mode, true, out var m)) parsedMode = m;

      var skipSnapshot = ctx.ParseResult.GetValueForOption(skipSnap);
      var breakGlassAck = ctx.ParseResult.GetValueForOption(breakGlassAckOpt);
      var breakGlassReason = ctx.ParseResult.GetValueForOption(breakGlassReasonOpt);
      var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
      var filterRuleIds = ctx.ParseResult.GetValueForOption(ruleIdOpt);
      var filterSeverities = ctx.ParseResult.GetValueForOption(severityOpt);
      var filterCategories = ctx.ParseResult.GetValueForOption(categoryOpt);
      var breakGlassValidationError = BreakGlassService.ValidateArguments(
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
      await BreakGlassService.RecordAuditAsync(
        audit,
        skipSnapshot,
        "apply-run",
        bundle,
        "skip-snapshot",
        breakGlassReason,
        ct);

      var runner = host.Services.GetRequiredService<STIGForge.Apply.ApplyRunner>();
      var result = await runner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = bundle, ModeOverride = parsedMode,
        ScriptPath = BuildCommandHelpers.NullIfEmpty(ctx, scriptOpt), ScriptArgs = BuildCommandHelpers.NullIfEmpty(ctx, scriptArgsOpt),
        DscMofPath = BuildCommandHelpers.NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        SkipSnapshot = skipSnapshot,
        DryRun = dryRun,
        FilterRuleIds = filterRuleIds,
        FilterSeverities = filterSeverities,
        FilterCategories = filterCategories,
        PowerStigModulePath = BuildCommandHelpers.NullIfEmpty(ctx, psModOpt), PowerStigDataFile = BuildCommandHelpers.NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = BuildCommandHelpers.NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt)
      }, ct);
      logger.LogInformation("apply-run completed: log={LogPath}", result.LogPath);
      Console.WriteLine("Apply completed. Log: " + result.LogPath);
      if (dryRun && result.DryRunReport != null)
      {
        Console.WriteLine("Dry-run report: " + Path.Combine(bundle, "Apply", "dry_run_report.json"));
        Console.WriteLine("Changes proposed: " + result.DryRunReport.Changes.Count);
      }
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
    var scapArgsOpt = new Option<string>("--scap-args", () => "-u -s -r -f", "SCAP/SCC arguments");
    var scapLabelOpt = new Option<string>("--scap-label", () => "DISA SCAP", "SCAP tool label");

    var skipSnapOpt = new Option<bool>("--skip-snapshot", "Skip snapshot generation for image pipeline scenarios (high risk)");
    var breakGlassAckOpt = new Option<bool>(BreakGlassService.AckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassService.ReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");

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
      var ct = ctx.GetCancellationToken();
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

      var breakGlassValidationError = BreakGlassService.ValidateArguments(
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
      var airGapTransferRoot = AirGapDownloadService.GetAirGapTransferRoot(paths);
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

        var imported = await importer.ImportZipAsync(niwcZip, effectivePackName, sourceLabel, ct);
        effectivePackId = imported.PackId;
        Console.WriteLine("Imported NIWC enhanced SCAP pack: " + imported.Name + " (" + imported.PackId + ")");
      }

      if (string.IsNullOrWhiteSpace(effectivePackId) && !string.IsNullOrWhiteSpace(disaStigUrl))
      {
        var downloadedDisaZip = await AirGapDownloadService.DownloadSourceZipAsync(disaStigUrl, "disa-stig", airGapTransferRoot, ct);
        downloadedArtifacts.Add(downloadedDisaZip);
        var disaPackName = string.IsNullOrWhiteSpace(packName)
          ? "DISA_STIG_" + Path.GetFileNameWithoutExtension(downloadedDisaZip)
          : packName;
        var imported = await importer.ImportZipAsync(downloadedDisaZip, disaPackName, "disa_stig_library", ct);
        effectivePackId = imported.PackId;
        Console.WriteLine("Imported DISA STIG pack: " + imported.Name + " (" + imported.PackId + ")");
        Console.WriteLine("Saved DISA source archive: " + downloadedDisaZip);
      }

      if (string.IsNullOrWhiteSpace(effectivePackId) && allowRemoteDownloads)
      {
        var downloadedNiwcZip = await AirGapDownloadService.DownloadSourceZipAsync(niwcSourceUrl, "niwc-enhanced", airGapTransferRoot, ct);
        downloadedArtifacts.Add(downloadedNiwcZip);
        var effectivePackName = string.IsNullOrWhiteSpace(packName)
          ? "NIWC_Enhanced_" + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmm")
          : packName;
        var imported = await importer.ImportZipAsync(downloadedNiwcZip, effectivePackName, sourceLabel, ct);
        effectivePackId = imported.PackId;
        Console.WriteLine("Downloaded and imported NIWC enhanced SCAP pack: " + imported.Name + " (" + imported.PackId + ")");
        Console.WriteLine("Saved NIWC source archive: " + downloadedNiwcZip);
      }

      if (string.IsNullOrWhiteSpace(effectivePackId))
        throw new ArgumentException("Unable to resolve a content pack. Provide --pack-id, --niwc-enhanced-zip, --disa-stig-url, or enable --allow-remote-downloads.");

      var pack = await packs.GetAsync(effectivePackId, ct)
        ?? throw new ArgumentException("Pack not found: " + effectivePackId);

      var generatedProfile = BuildCommandHelpers.BuildGeneratedProfile(
        generatedProfileName,
        generatedMode,
        generatedClassification,
        generatedOsTarget,
        generatedRoleTemplate,
        generatedAutoNa,
        generatedNaConfidence,
        generatedNaComment);
      var profile = await BuildCommandHelpers.ResolveProfileAsync(profiles, profileId, profileJson, generatedProfile, saveProfile, ct);

      var controlList = await controlsRepo.ListControlsAsync(pack.PackId, ct);
      var overlays = await BuildCommandHelpers.LoadSelectedOverlaysAsync(overlaysRepo, profile, ct);

      var buildResult = await builder.BuildAsync(new BundleBuildRequest
      {
        BundleId = ctx.ParseResult.GetValueForOption(bundleIdOpt) ?? string.Empty,
        OutputRoot = BuildCommandHelpers.NullIfEmpty(ctx, outputOpt),
        Pack = pack,
        Profile = profile,
        Controls = controlList,
        Overlays = overlays,
        ToolVersion = "0.1.0-dev"
      }, ct);

      var autoNaSeeded = BuildCommandHelpers.SeedAutoNaAnswers(scope, manualAnswers, buildResult.BundleRoot, pack, profile, controlList);

      var autoDetectTools = ctx.ParseResult.GetValueForOption(autoDetectToolsOpt);
      var evaluateStigRoot = BuildCommandHelpers.NullIfEmpty(ctx, evalOpt);
      var scapCommandPath = BuildCommandHelpers.NullIfEmpty(ctx, scapOpt);
      var powerStigModulePath = BuildCommandHelpers.NullIfEmpty(ctx, psModOpt);

      if (autoDetectTools)
      {
        evaluateStigRoot ??= ToolPathAutoDetector.TryAutoDetectEvaluateStigRoot();
        scapCommandPath ??= ToolPathAutoDetector.TryAutoDetectScapCommand();
        powerStigModulePath ??= ToolPathAutoDetector.TryAutoDetectPowerStigModulePath();
      }

      if (string.IsNullOrWhiteSpace(powerStigModulePath) && allowRemoteDownloads)
      {
        var powerStigRemote = await AirGapDownloadService.DownloadAndExtractPowerStigModuleAsync(powerStigSourceUrl, airGapTransferRoot, ct);
        powerStigModulePath = powerStigRemote.ModulePath;
        if (!string.IsNullOrWhiteSpace(powerStigRemote.ArchivePath)) downloadedArtifacts.Add(powerStigRemote.ArchivePath);
        if (!string.IsNullOrWhiteSpace(powerStigModulePath))
          Console.WriteLine("PowerStig module resolved from source URL: " + powerStigModulePath);
        if (!string.IsNullOrWhiteSpace(powerStigRemote.ArchivePath))
          Console.WriteLine("Saved PowerStig source archive: " + powerStigRemote.ArchivePath);
      }

      if (string.IsNullOrWhiteSpace(evaluateStigRoot) && string.IsNullOrWhiteSpace(scapCommandPath))
        throw new ArgumentException("No scanner configured. Provide --evaluate-stig and/or --scap-cmd, or enable --auto-detect-tools with installed tools.");

      await BreakGlassService.RecordAuditAsync(
        audit,
        skipSnapshot,
        "mission-autopilot",
        buildResult.BundleRoot,
        "skip-snapshot",
        breakGlassReason,
        ct);

      await orchestrator.OrchestrateAsync(new OrchestrateRequest
      {
        BundleRoot = buildResult.BundleRoot,
        SkipSnapshot = skipSnapshot,
        BreakGlassAcknowledged = breakGlassAck,
        BreakGlassReason = breakGlassReason,
        PowerStigModulePath = powerStigModulePath,
        PowerStigDataFile = BuildCommandHelpers.NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = BuildCommandHelpers.NullIfEmpty(ctx, psOutOpt),
        PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt),
        EvaluateStigRoot = evaluateStigRoot,
        EvaluateStigArgs = BuildCommandHelpers.NullIfEmpty(ctx, evalArgsOpt),
        ScapCommandPath = scapCommandPath,
        ScapArgs = BuildCommandHelpers.NullIfEmpty(ctx, scapArgsOpt) ?? string.Empty,
        ScapToolLabel = BuildCommandHelpers.NullIfEmpty(ctx, scapLabelOpt)
      }, ct);

      var manualTemplatePath = Path.Combine(buildResult.BundleRoot, "Manual", "answerfile.template.json");
      var manualAnswersPath = Path.Combine(buildResult.BundleRoot, "Manual", "answers.json");
      Console.WriteLine("Mission autopilot completed.");
      Console.WriteLine("Bundle: " + buildResult.BundleRoot);
      Console.WriteLine("Manifest: " + buildResult.ManifestPath);
      Console.WriteLine("Manual template: " + manualTemplatePath);
      Console.WriteLine("Manual answers: " + manualAnswersPath);
      Console.WriteLine("Auto-NA seeded answers: " + autoNaSeeded);
      Console.WriteLine("Verify outputs: " + Path.Combine(buildResult.BundleRoot, "Verify"));
      Console.WriteLine("Coverage reports: " + Path.Combine(buildResult.BundleRoot, "Reports"));
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

}

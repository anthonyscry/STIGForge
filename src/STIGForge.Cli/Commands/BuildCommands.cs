using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
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
    var breakGlassAckOpt = new Option<bool>(BreakGlassAckOptionName, "Acknowledge high-risk break-glass use for --skip-snapshot");
    var breakGlassReasonOpt = new Option<string>(BreakGlassReasonOptionName, "Reason for break-glass use (required with --skip-snapshot)");

    foreach (var o in new Option[] { bOpt, dscOpt, dscVOpt, psModOpt, psDataOpt, psOutOpt, psVOpt, evalOpt, evalArgsOpt, scapOpt, scapArgsOpt, scapLabelOpt, skipSnapOpt, breakGlassAckOpt, breakGlassReasonOpt })
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
        ScapToolLabel = NullIfEmpty(ctx, scapLabelOpt)
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

  private static string? NullIfEmpty(InvocationContext ctx, Option<string> opt)
  {
    var val = ctx.ParseResult.GetValueForOption(opt);
    return string.IsNullOrWhiteSpace(val) ? null : val;
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

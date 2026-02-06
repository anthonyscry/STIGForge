using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Cli.Commands;

internal static class BuildCommands
{
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

    cmd.AddOption(packIdOpt); cmd.AddOption(profileIdOpt); cmd.AddOption(profileJsonOpt);
    cmd.AddOption(bundleIdOpt); cmd.AddOption(outputOpt); cmd.AddOption(saveProfileOpt); cmd.AddOption(forceAutoApplyOpt);

    cmd.SetHandler(async (packId, profileId, profileJson, bundleId, output, saveProfile, forceAutoApply) =>
    {
      using var host = buildHost();
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

      Console.WriteLine("Bundle created: " + result.BundleRoot);
      Console.WriteLine("Manifest: " + result.ManifestPath);
      var gatePath = Path.Combine(result.BundleRoot, "Reports", "automation_gate.json");
      if (File.Exists(gatePath)) Console.WriteLine("Automation gate: " + gatePath);
      await host.StopAsync();
    }, packIdOpt, profileIdOpt, profileJsonOpt, bundleIdOpt, outputOpt, saveProfileOpt, forceAutoApplyOpt);

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

    foreach (var o in new Option[] { bOpt, dscOpt, dscVOpt, psModOpt, psDataOpt, psOutOpt, psVOpt, evalOpt, evalArgsOpt, scapOpt, scapArgsOpt, scapLabelOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      using var host = buildHost();
      await host.StartAsync();
      var orchestrator = host.Services.GetRequiredService<BundleOrchestrator>();
      await orchestrator.OrchestrateAsync(new OrchestrateRequest
      {
        BundleRoot = bundle,
        DscMofPath = NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        PowerStigModulePath = NullIfEmpty(ctx, psModOpt), PowerStigDataFile = NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt),
        EvaluateStigRoot = NullIfEmpty(ctx, evalOpt), EvaluateStigArgs = NullIfEmpty(ctx, evalArgsOpt),
        ScapCommandPath = NullIfEmpty(ctx, scapOpt), ScapArgs = NullIfEmpty(ctx, scapArgsOpt),
        ScapToolLabel = NullIfEmpty(ctx, scapLabelOpt)
      }, CancellationToken.None);
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
    var psModOpt = new Option<string>("--powerstig-module", () => string.Empty, "PowerSTIG module folder");
    var psDataOpt = new Option<string>("--powerstig-data", () => string.Empty, "PowerSTIG data file");
    var psOutOpt = new Option<string>("--powerstig-out", () => string.Empty, "PowerSTIG MOF output folder");
    var psVOpt = new Option<bool>("--powerstig-verbose", "Verbose PowerSTIG compile");

    foreach (var o in new Option[] { bOpt, modeOpt, scriptOpt, scriptArgsOpt, dscOpt, dscVOpt, skipSnap, psModOpt, psDataOpt, psOutOpt, psVOpt })
      cmd.AddOption(o);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bOpt) ?? string.Empty;
      var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? string.Empty;
      HardeningMode? parsedMode = null;
      if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse<HardeningMode>(mode, true, out var m)) parsedMode = m;

      using var host = buildHost();
      await host.StartAsync();
      var runner = host.Services.GetRequiredService<STIGForge.Apply.ApplyRunner>();
      var result = await runner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = bundle, ModeOverride = parsedMode,
        ScriptPath = NullIfEmpty(ctx, scriptOpt), ScriptArgs = NullIfEmpty(ctx, scriptArgsOpt),
        DscMofPath = NullIfEmpty(ctx, dscOpt), DscVerbose = ctx.ParseResult.GetValueForOption(dscVOpt),
        SkipSnapshot = ctx.ParseResult.GetValueForOption(skipSnap),
        PowerStigModulePath = NullIfEmpty(ctx, psModOpt), PowerStigDataFile = NullIfEmpty(ctx, psDataOpt),
        PowerStigOutputPath = NullIfEmpty(ctx, psOutOpt), PowerStigVerbose = ctx.ParseResult.GetValueForOption(psVOpt)
      }, CancellationToken.None);
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
}

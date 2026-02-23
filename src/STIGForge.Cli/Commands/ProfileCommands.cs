using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class ProfileCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var profileCmd = new Command("profile", "Manage policy profiles");

    RegisterProfileList(profileCmd, buildHost);
    RegisterProfileShow(profileCmd, buildHost);
    RegisterProfileCreate(profileCmd, buildHost);
    RegisterProfileUpdate(profileCmd, buildHost);
    RegisterProfileExport(profileCmd, buildHost);
    RegisterProfileValidate(profileCmd, buildHost);

    rootCmd.AddCommand(profileCmd);
  }

  private static void RegisterProfileList(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("list", "List all profiles");

    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();

      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var list = await profiles.ListAsync(CancellationToken.None);

      if (list.Count == 0)
      {
        Console.WriteLine("No profiles found.");
        await host.StopAsync();
        return;
      }

      Console.WriteLine($"{"ProfileId",-36} {"Name",-30} {"Classification",-15} {"Mode",-12} {"GraceDays",-10}");
      Console.WriteLine(new string('-', 103));

      foreach (var p in list)
      {
        Console.WriteLine($"{p.ProfileId,-36} {p.Name,-30} {p.ClassificationMode,-15} {p.HardeningMode,-12} {p.AutomationPolicy?.NewRuleGraceDays ?? 30,-10}");
      }

      Console.WriteLine($"\n{list.Count} profile(s) found.");
      await host.StopAsync();
    });

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterProfileShow(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("show", "Show full profile details");
    var idArg = new Argument<string>("id", "Profile ID to show");
    cmd.AddArgument(idArg);

    cmd.SetHandler(async (string id) =>
    {
      using var host = buildHost();
      await host.StartAsync();

      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var profile = await profiles.GetAsync(id, CancellationToken.None);

      if (profile == null)
      {
        Console.Error.WriteLine($"Profile not found: {id}");
        await host.StopAsync();
        return;
      }

      Console.WriteLine($"Profile: {profile.Name}");
      Console.WriteLine($"  ProfileId:          {profile.ProfileId}");
      Console.WriteLine($"  OsTarget:           {profile.OsTarget}");
      Console.WriteLine($"  RoleTemplate:       {profile.RoleTemplate}");
      Console.WriteLine($"  HardeningMode:      {profile.HardeningMode}");
      Console.WriteLine($"  ClassificationMode: {profile.ClassificationMode}");
      Console.WriteLine();
      Console.WriteLine("NaPolicy:");
      Console.WriteLine($"  AutoNaOutOfScope:       {profile.NaPolicy?.AutoNaOutOfScope}");
      Console.WriteLine($"  ConfidenceThreshold:    {profile.NaPolicy?.ConfidenceThreshold}");
      Console.WriteLine($"  DefaultNaComment:       {profile.NaPolicy?.DefaultNaCommentTemplate}");
      Console.WriteLine();
      Console.WriteLine("AutomationPolicy:");
      Console.WriteLine($"  Mode:                   {profile.AutomationPolicy?.Mode}");
      Console.WriteLine($"  NewRuleGraceDays:       {profile.AutomationPolicy?.NewRuleGraceDays}");
      Console.WriteLine($"  AutoApplyRequiresMap:   {profile.AutomationPolicy?.AutoApplyRequiresMapping}");
      Console.WriteLine($"  ReleaseDateSource:      {profile.AutomationPolicy?.ReleaseDateSource}");
      Console.WriteLine();
      Console.WriteLine($"Overlays: [{string.Join(", ", profile.OverlayIds ?? Array.Empty<string>())}]");

      await host.StopAsync();
    }, idArg);

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterProfileCreate(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("create", "Create a profile from a JSON file");
    var fromJsonOpt = new Option<string>("--from-json", "Path to profile JSON file") { IsRequired = true };
    var saveOpt = new Option<bool>("--save", () => true, "Save profile to repository");
    cmd.AddOption(fromJsonOpt);
    cmd.AddOption(saveOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var fromJson = ctx.ParseResult.GetValueForOption(fromJsonOpt) ?? string.Empty;
      var save = ctx.ParseResult.GetValueForOption(saveOpt);

      if (!File.Exists(fromJson))
        throw new FileNotFoundException("Profile JSON file not found", fromJson);

      var json = File.ReadAllText(fromJson);
      var profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new ArgumentException("Invalid profile JSON.");

      if (string.IsNullOrWhiteSpace(profile.ProfileId))
        profile.ProfileId = Guid.NewGuid().ToString("n");

      var validator = new ProfileValidator();
      var result = validator.Validate(profile);
      if (!result.IsValid)
      {
        Console.Error.WriteLine("Profile validation failed:");
        foreach (var error in result.Errors)
          Console.Error.WriteLine($"  - {error}");
        throw new ArgumentException("Profile is invalid. Fix errors and retry.");
      }

      if (save)
      {
        using var host = buildHost();
        await host.StartAsync();
        var profiles = host.Services.GetRequiredService<IProfileRepository>();
        await profiles.SaveAsync(profile, CancellationToken.None);
        Console.WriteLine($"Profile created and saved: {profile.Name} ({profile.ProfileId})");
        await host.StopAsync();
      }
      else
      {
        Console.WriteLine($"Profile validated: {profile.Name} ({profile.ProfileId})");
        Console.WriteLine("Use --save to persist to repository.");
      }
    });

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterProfileUpdate(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("update", "Update an existing profile from a JSON file");
    var idArg = new Argument<string>("id", "Profile ID to update");
    var fromJsonOpt = new Option<string>("--from-json", "Path to profile JSON with updated values") { IsRequired = true };
    cmd.AddArgument(idArg);
    cmd.AddOption(fromJsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var id = ctx.ParseResult.GetValueForArgument(idArg);
      var fromJson = ctx.ParseResult.GetValueForOption(fromJsonOpt) ?? string.Empty;

      if (!File.Exists(fromJson))
        throw new FileNotFoundException("Profile JSON file not found", fromJson);

      using var host = buildHost();
      await host.StartAsync();

      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var existing = await profiles.GetAsync(id, CancellationToken.None);
      if (existing == null)
        throw new ArgumentException($"Profile not found: {id}");

      var json = File.ReadAllText(fromJson);
      var updated = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new ArgumentException("Invalid profile JSON.");

      // Preserve original ID
      updated.ProfileId = id;

      var validator = new ProfileValidator();
      var result = validator.Validate(updated);
      if (!result.IsValid)
      {
        Console.Error.WriteLine("Profile validation failed:");
        foreach (var error in result.Errors)
          Console.Error.WriteLine($"  - {error}");
        throw new ArgumentException("Profile is invalid. Fix errors and retry.");
      }

      await profiles.SaveAsync(updated, CancellationToken.None);
      Console.WriteLine($"Profile updated: {updated.Name} ({updated.ProfileId})");

      await host.StopAsync();
    });

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterProfileExport(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("export", "Export a profile to JSON file");
    var idArg = new Argument<string>("id", "Profile ID to export");
    var outputOpt = new Option<string>("--output", "Output file path") { IsRequired = true };
    cmd.AddArgument(idArg);
    cmd.AddOption(outputOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var id = ctx.ParseResult.GetValueForArgument(idArg);
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();

      var profiles = host.Services.GetRequiredService<IProfileRepository>();
      var profile = await profiles.GetAsync(id, CancellationToken.None);

      if (profile == null)
        throw new ArgumentException($"Profile not found: {id}");

      var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(output, json, System.Text.Encoding.UTF8);
      Console.WriteLine($"Profile exported to: {output}");

      await host.StopAsync();
    });

    parentCmd.AddCommand(cmd);
  }

  private static void RegisterProfileValidate(Command parentCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("validate", "Validate a profile for policy consistency");
    var idArg = new Argument<string?>("id", () => null, "Profile ID to validate (optional if --from-json provided)");
    var fromJsonOpt = new Option<string?>("--from-json", "Path to profile JSON file to validate");
    cmd.AddArgument(idArg);
    cmd.AddOption(fromJsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var id = ctx.ParseResult.GetValueForArgument(idArg);
      var fromJson = ctx.ParseResult.GetValueForOption(fromJsonOpt);

      Profile? profile = null;

      if (!string.IsNullOrWhiteSpace(fromJson))
      {
        if (!File.Exists(fromJson))
          throw new FileNotFoundException("Profile JSON file not found", fromJson);

        var json = File.ReadAllText(fromJson);
        profile = JsonSerializer.Deserialize<Profile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
          ?? throw new ArgumentException("Invalid profile JSON.");
      }
      else if (!string.IsNullOrWhiteSpace(id))
      {
        using var host = buildHost();
        await host.StartAsync();
        var profiles = host.Services.GetRequiredService<IProfileRepository>();
        profile = await profiles.GetAsync(id, CancellationToken.None);
        if (profile == null)
          throw new ArgumentException($"Profile not found: {id}");
        await host.StopAsync();
      }
      else
      {
        throw new ArgumentException("Provide a profile ID or --from-json path.");
      }

      var validator = new ProfileValidator();
      var result = validator.Validate(profile);

      if (result.IsValid)
      {
        Console.WriteLine($"Profile valid: {profile.Name} ({profile.ProfileId})");
      }
      else
      {
        Console.Error.WriteLine($"Profile invalid: {profile.Name} ({profile.ProfileId})");
        foreach (var error in result.Errors)
          Console.Error.WriteLine($"  - {error}");
      }
    });

    parentCmd.AddCommand(cmd);
  }
}

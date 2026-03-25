using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

/// <summary>Profile construction, resolution, and overlay loading helpers for build commands.</summary>
internal static class BuildCommandHelpers
{
    public static string? NullIfEmpty(InvocationContext ctx, Option<string> opt)
    {
        var val = ctx.ParseResult.GetValueForOption(opt);
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    public static Profile BuildGeneratedProfile(
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
        var classification = ParseEnumOrThrow<ClassificationMode>(
            classificationValue ?? ClassificationMode.Classified.ToString(), "--classification");
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
                DefaultNaCommentTemplate = string.IsNullOrWhiteSpace(naComment)
                    ? "Auto-NA (classification scope)"
                    : naComment
            },
            AutomationPolicy = new AutomationPolicy
            {
                Mode = AutomationMode.Standard,
                NewRuleGraceDays = 30,
                AutoApplyRequiresMapping = true,
                ReleaseDateSource = ReleaseDateSource.ContentPack
            },
            OverlayIds = []
        };
    }

    public static async Task<Profile> ResolveProfileAsync(
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

            var json = await File.ReadAllTextAsync(profileJson, ct).ConfigureAwait(false);
            var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions.CaseInsensitive)
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

    public static async Task<IReadOnlyList<Overlay>> LoadSelectedOverlaysAsync(
        IOverlayRepository overlaysRepo, Profile profile, CancellationToken ct)
    {
        var overlays = new List<Overlay>();
        foreach (var overlayId in profile.OverlayIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(overlayId)) continue;
            var overlay = await overlaysRepo.GetAsync(overlayId, ct);
            if (overlay != null)
                overlays.Add(overlay);
        }
        return overlays;
    }

    public static int SeedAutoNaAnswers(
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
                    Status = "NotApplicable",
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

    public static T ParseEnumOrThrow<T>(string value, string optionName) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, true, out var parsed))
            return parsed;

        throw new ArgumentException($"Invalid value '{value}' for {optionName}.");
    }
}

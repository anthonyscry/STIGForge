using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ProfileValidationResult
{
  public bool IsValid => Errors.Count == 0;
  public IReadOnlyList<string> Errors { get; }

  public ProfileValidationResult(IReadOnlyList<string> errors)
  {
    Errors = errors;
  }

  public static ProfileValidationResult Success => new(Array.Empty<string>());
}

public sealed class ProfileValidator
{
  public ProfileValidationResult Validate(Profile profile)
  {
    if (profile == null)
      return new ProfileValidationResult(new[] { "Profile is null." });

    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(profile.ProfileId))
      errors.Add("ProfileId is required.");

    if (string.IsNullOrWhiteSpace(profile.Name))
      errors.Add("Name is required.");

    if (!Enum.IsDefined(typeof(ClassificationMode), profile.ClassificationMode))
      errors.Add($"ClassificationMode '{profile.ClassificationMode}' is not a valid value.");

    if (!Enum.IsDefined(typeof(HardeningMode), profile.HardeningMode))
      errors.Add($"HardeningMode '{profile.HardeningMode}' is not a valid value.");

    if (!Enum.IsDefined(typeof(OsTarget), profile.OsTarget))
      errors.Add($"OsTarget '{profile.OsTarget}' is not a valid value.");

    if (!Enum.IsDefined(typeof(RoleTemplate), profile.RoleTemplate))
      errors.Add($"RoleTemplate '{profile.RoleTemplate}' is not a valid value.");

    if (profile.NaPolicy == null)
    {
      errors.Add("NaPolicy is required.");
    }
    else
    {
      if (!Enum.IsDefined(typeof(Confidence), profile.NaPolicy.ConfidenceThreshold))
        errors.Add($"NaPolicy.ConfidenceThreshold '{profile.NaPolicy.ConfidenceThreshold}' is not a valid value.");
    }

    if (profile.AutomationPolicy == null)
    {
      errors.Add("AutomationPolicy is required.");
    }
    else
    {
      if (profile.AutomationPolicy.NewRuleGraceDays < 0)
        errors.Add($"AutomationPolicy.NewRuleGraceDays must be >= 0 (was {profile.AutomationPolicy.NewRuleGraceDays}).");

      if (!Enum.IsDefined(typeof(AutomationMode), profile.AutomationPolicy.Mode))
        errors.Add($"AutomationPolicy.Mode '{profile.AutomationPolicy.Mode}' is not a valid value.");
    }

    if (profile.OverlayIds != null)
    {
      for (int i = 0; i < profile.OverlayIds.Count; i++)
      {
        if (string.IsNullOrWhiteSpace(profile.OverlayIds[i]))
          errors.Add($"OverlayIds[{i}] is null or empty.");
      }
    }

    return new ProfileValidationResult(errors);
  }
}

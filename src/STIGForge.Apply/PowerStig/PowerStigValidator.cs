namespace STIGForge.Apply.PowerStig;

/// <summary>
/// Result of PowerSTIG data validation.
/// </summary>
public sealed class ValidationResult
{
  public bool IsValid { get; set; }
  public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Validates PowerSTIG data structure before serialization.
/// </summary>
public static class PowerStigValidator
{
  /// <summary>
  /// Validates PowerSTIG data structure.
  /// </summary>
  /// <param name="data">PowerSTIG data to validate</param>
  /// <returns>Validation result with IsValid flag and error list</returns>
  public static ValidationResult Validate(PowerStigData data)
  {
    var result = new ValidationResult();

    // Validate StigVersion
    if (string.IsNullOrWhiteSpace(data.StigVersion))
    {
      result.Errors.Add("StigVersion is required and cannot be null or empty");
    }

    // Validate GlobalSettings
    if (data.GlobalSettings == null || data.GlobalSettings.Count == 0)
    {
      result.Errors.Add("GlobalSettings is required and cannot be null or empty");
    }
    else
    {
      // Check for required keys
      var hasOrgName = data.GlobalSettings.ContainsKey("OrganizationName");
      var hasProfile = data.GlobalSettings.ContainsKey("ApplyProfile");

      // Validate OrganizationName
      if (!hasOrgName || string.IsNullOrWhiteSpace(data.GlobalSettings["OrganizationName"]))
      {
        result.Errors.Add("GlobalSettings must contain 'OrganizationName' with a non-empty value");
      }

      // Validate ApplyProfile
      if (!hasProfile || string.IsNullOrWhiteSpace(data.GlobalSettings["ApplyProfile"]))
      {
        result.Errors.Add("GlobalSettings must contain 'ApplyProfile' with a non-empty value");
      }
    }

    // Validate RuleSettings
    if (data.RuleSettings == null)
    {
      result.Errors.Add("RuleSettings is required and cannot be null");
    }
    else
    {
      for (int i = 0; i < data.RuleSettings.Count; i++)
      {
        var rule = data.RuleSettings[i];
        ValidateRuleSetting(rule, i, result.Errors);
      }
    }

    result.IsValid = result.Errors.Count == 0;
    return result;
  }

  /// <summary>
  /// Validates a single PowerStigRuleSetting.
  /// </summary>
  private static void ValidateRuleSetting(PowerStigRuleSetting rule, int index, List<string> errors)
  {
    // Validate RuleId
    if (string.IsNullOrWhiteSpace(rule.RuleId))
    {
      errors.Add($"RuleSettings[{index}]: RuleId is required and cannot be null or empty");
      return;
    }

    // Validate RuleId format (SV-<digits>r<digits>_rule)
    if (!System.Text.RegularExpressions.Regex.IsMatch(rule.RuleId, @"^SV-\d+r\d+_rule$"))
    {
      errors.Add($"RuleSettings[{index}]: RuleId '{rule.RuleId}' does not match required pattern SV-<digits>r<digits>_rule");
    }

    // Validate SettingName and Value are valid types
    // They can be null or string, so we just check they're not invalid objects
    // (PowerStigRuleSetting already has them as string? which is correct)
  }
}

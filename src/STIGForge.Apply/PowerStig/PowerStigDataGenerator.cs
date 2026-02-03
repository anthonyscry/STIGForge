using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Apply.PowerStig;

public static class PowerStigDataGenerator
{
  private static ReleaseAgeGate? _releaseAgeGate;
  private static ClassificationScopeService? _scopeService;

  /// <summary>
  /// Initialize services for data generation.
  /// Must be called before CreateFromControls with filtering.
  /// </summary>
  public static void Initialize(
    ReleaseAgeGate releaseAgeGate,
    ClassificationScopeService scopeService)
  {
    _releaseAgeGate = releaseAgeGate;
    _scopeService = scopeService;
  }

  public static PowerStigData CreateDefault(string powerStigModulePath, string bundleRoot)
  {
    return new PowerStigData
    {
      StigVersion = "Unknown",
      StigRelease = "Unknown",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "STIGForge" },
        { "ApplyProfile", "Baseline" }
      },
      RuleSettings = new List<PowerStigRuleSetting>()
    };
  }

  public static PowerStigData CreateFromControls(
    IEnumerable<ControlRecord> controls,
    IEnumerable<PowerStigOverride>? overrides,
    Profile? profile = null)
  {
    var data = CreateDefault(string.Empty, string.Empty);

    // Apply release age filter (if service initialized and profile provided)
    if (_releaseAgeGate != null && profile != null)
    {
      var gracePeriod = profile.AutomationPolicy.NewRuleGraceDays;
      var filteredByAge = _releaseAgeGate.FilterControls(controls, gracePeriod);
      controls = filteredByAge;
    }

    // Apply classification scope filter (if service initialized and profile provided)
    if (_scopeService != null && profile != null)
    {
      var filteredByScope = ClassificationScopeService.FilterControls(
        controls,
        profile.ClassificationMode);
      controls = filteredByScope;
    }

    foreach (var c in controls)
    {
      var ruleId = c.ExternalIds?.RuleId;
      if (string.IsNullOrWhiteSpace(ruleId)) continue;

      data.RuleSettings.Add(new PowerStigRuleSetting
      {
        RuleId = ruleId!.Trim(),
        SettingName = null,
        Value = null
      });
    }

    if (overrides != null)
    {
      foreach (var o in overrides)
      {
        if (string.IsNullOrWhiteSpace(o.RuleId)) continue;
        var existing = data.RuleSettings.FirstOrDefault(r =>
          string.Equals(r.RuleId, o.RuleId, StringComparison.OrdinalIgnoreCase));

        // Skip overrides for non-existent rules (only apply to controls we have)
        if (existing == null) continue;

        // Apply override to existing rule
        if (!string.IsNullOrWhiteSpace(o.SettingName)) existing.SettingName = o.SettingName;
        if (!string.IsNullOrWhiteSpace(o.Value)) existing.Value = o.Value;
      }
    }

    return data;
  }
}

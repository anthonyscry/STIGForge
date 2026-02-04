namespace STIGForge.Apply.PowerStig;

public sealed class PowerStigRuleSetting
{
  public string RuleId { get; set; } = string.Empty;
  public string? SettingName { get; set; }
  public string? Value { get; set; }
}

public sealed class PowerStigData
{
  public string? StigVersion { get; set; }
  public string? StigRelease { get; set; }
  public Dictionary<string, string> GlobalSettings { get; set; } = new();
  public List<PowerStigRuleSetting> RuleSettings { get; set; } = new();
}

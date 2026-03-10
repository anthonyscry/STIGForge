namespace STIGForge.Core.Models;

/// <summary>
/// A single organizational setting entry that requires user input.
/// Maps to a PowerSTIG OrgSettings XML element.
/// </summary>
public sealed class OrgSettingEntry
{
  public string RuleId { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;
  public string Severity { get; set; } = "medium";
  public string Category { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string DefaultValue { get; set; } = string.Empty;
  public bool IsRequired { get; set; }
}

/// <summary>
/// Portable answer file for organizational settings.
/// Contains user-provided values for PowerSTIG OrgSettings that are
/// site-specific (e.g., root certificate thumbprints, legal banner text,
/// service names, security option thresholds).
/// Can be exported/imported across systems and missions.
/// </summary>
public sealed class OrgSettingsProfile
{
  public string ProfileName { get; set; } = string.Empty;
  public string OsTarget { get; set; } = string.Empty;
  public string RoleTemplate { get; set; } = string.Empty;
  public string StigVersion { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public string CreatedBy { get; set; } = string.Empty;
  public List<OrgSettingEntry> Entries { get; set; } = new();
}

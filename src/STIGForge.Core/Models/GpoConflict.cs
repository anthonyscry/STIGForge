namespace STIGForge.Core.Models;

public sealed class GpoConflict
{
  public string SettingPath { get; set; } = string.Empty;
  public string LocalValue { get; set; } = string.Empty;
  public string GpoValue { get; set; } = string.Empty;
  public string GpoName { get; set; } = string.Empty;
  public string ConflictType { get; set; } = string.Empty;
}

namespace STIGForge.Core.Models;

public sealed class RunManifest
{
  public string RunId { get; set; } = string.Empty;
  public string SystemName { get; set; } = string.Empty;
  public OsTarget OsTarget { get; set; }
  public RoleTemplate RoleTemplate { get; set; }
  public string ProfileId { get; set; } = string.Empty;
  public string ProfileName { get; set; } = string.Empty;
  public string PackId { get; set; } = string.Empty;
  public string PackName { get; set; } = string.Empty;
  public DateTimeOffset Timestamp { get; set; }
  public string ToolVersion { get; set; } = string.Empty;
}

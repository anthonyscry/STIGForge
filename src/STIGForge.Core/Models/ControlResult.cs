namespace STIGForge.Core.Models;

public sealed class ControlResult
{
  public string RuleId { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string Status { get; set; } = string.Empty;
  public string? Title { get; set; }
  public string? Severity { get; set; }
  public string? Comments { get; set; }
  public string? FindingDetails { get; set; }
  public string SourceFile { get; set; } = string.Empty;
}

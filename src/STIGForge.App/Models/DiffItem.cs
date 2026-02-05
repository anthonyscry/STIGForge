namespace STIGForge.App.Models;

public sealed class DiffItem
{
  public string RuleId { get; set; } = string.Empty;
  public string VulnId { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public bool IsManual { get; set; }
  public bool ManualChanged { get; set; }
  public string ChangedFields { get; set; } = string.Empty;
}

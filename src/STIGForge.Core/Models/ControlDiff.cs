namespace STIGForge.Core.Models;

public enum DiffKind { Added, Removed, Changed, Unchanged }

public sealed class ControlDiff
{
  public string Key { get; set; } = string.Empty;
  public string? RuleId { get; set; }
  public string? VulnId { get; set; }
  public string Title { get; set; } = string.Empty;
  public DiffKind Kind { get; set; }
  public bool IsManual { get; set; }
  public bool ManualChanged { get; set; }
  public IReadOnlyList<string> ChangedFields { get; set; } = Array.Empty<string>();
  public string? FromHash { get; set; }
  public string? ToHash { get; set; }
}

public sealed class ReleaseDiff
{
  public string FromPackId { get; set; } = string.Empty;
  public string ToPackId { get; set; } = string.Empty;
  public IReadOnlyList<ControlDiff> Items { get; set; } = Array.Empty<ControlDiff>();
}

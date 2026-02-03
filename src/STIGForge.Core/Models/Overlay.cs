namespace STIGForge.Core.Models;

public sealed class ControlOverride
{
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public ControlStatus? StatusOverride { get; set; }
  public string? NaReason { get; set; }
  public string? Notes { get; set; }
}

public sealed class Overlay
{
  public string OverlayId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public DateTimeOffset UpdatedAt { get; set; }
  public IReadOnlyList<ControlOverride> Overrides { get; set; } = Array.Empty<ControlOverride>();
}

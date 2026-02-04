namespace STIGForge.Verify;

public sealed class CoverageOverlap
{
  public string SourcesKey { get; set; } = string.Empty;
  public int SourceCount { get; set; }
  public int ControlsCount { get; set; }
  public int ClosedCount { get; set; }
  public int OpenCount { get; set; }
}

public sealed class ControlSourceMap
{
  public string ControlKey { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public string SourcesKey { get; set; } = string.Empty;
  public bool IsClosed { get; set; }
}

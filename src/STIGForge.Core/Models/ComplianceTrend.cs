namespace STIGForge.Core.Models;

public sealed class TrendSnapshot
{
  public DateTimeOffset Timestamp { get; set; }
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int OpenCount { get; set; }
  public int NotApplicableCount { get; set; }
  public int TotalControls { get; set; }
}

public sealed class ComplianceTrendFile
{
  public List<TrendSnapshot> Snapshots { get; set; } = new();
}

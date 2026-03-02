namespace STIGForge.Core.Models;

/// <summary>
/// Point-in-time compliance score snapshot captured after a verification run.
/// </summary>
public sealed class ComplianceSnapshot
{
  public string SnapshotId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string? RunId { get; set; }
  public string? PackId { get; set; }
  public DateTimeOffset CapturedAt { get; set; }
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int ErrorCount { get; set; }
  public int NotApplicableCount { get; set; }
  public int NotReviewedCount { get; set; }
  public int TotalCount { get; set; }
  public double CompliancePercent { get; set; }
  public string Tool { get; set; } = string.Empty;
}

/// <summary>
/// Compliance trend analysis comparing current state to historical snapshots.
/// </summary>
public sealed class ComplianceTrend
{
  public IReadOnlyList<ComplianceSnapshot> Snapshots { get; set; } = Array.Empty<ComplianceSnapshot>();
  public double CurrentPercent { get; set; }
  public double PreviousPercent { get; set; }
  public double Delta { get; set; }
  public bool IsRegression { get; set; }
  public string? RegressionSummary { get; set; }
}

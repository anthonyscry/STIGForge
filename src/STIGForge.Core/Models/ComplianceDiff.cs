namespace STIGForge.Core.Models;

/// <summary>
/// Result of comparing two verification runs, identifying regressions, remediations, and scope changes.
/// </summary>
public sealed class ComplianceDiff
{
  public string BaselineLabel { get; set; } = string.Empty;
  public string TargetLabel { get; set; } = string.Empty;
  public DateTimeOffset? BaselineTimestamp { get; set; }
  public DateTimeOffset? TargetTimestamp { get; set; }
  public double BaselineCompliancePercent { get; set; }
  public double TargetCompliancePercent { get; set; }
  public double DeltaPercent { get; set; }
  public IReadOnlyList<ControlStatusChange> Regressions { get; set; } = [];
  public IReadOnlyList<ControlStatusChange> Remediations { get; set; } = [];
  public IReadOnlyList<ControlStatusChange> Added { get; set; } = [];
  public IReadOnlyList<ControlStatusChange> Removed { get; set; } = [];
  public DiffSeveritySummary SeveritySummary { get; set; } = new();
}

/// <summary>
/// A single control whose status changed between baseline and target runs.
/// </summary>
public sealed class ControlStatusChange
{
  public string VulnId { get; set; } = string.Empty;
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public string? Severity { get; set; }
  public string OldStatus { get; set; } = string.Empty;
  public string NewStatus { get; set; } = string.Empty;
  public string? Tool { get; set; }
}

/// <summary>
/// Severity-level summary of regressions and remediations in a compliance diff.
/// </summary>
public sealed class DiffSeveritySummary
{
  public int CatIRegressions { get; set; }
  public int CatIRemediations { get; set; }
  public int CatIIRegressions { get; set; }
  public int CatIIRemediations { get; set; }
  public int CatIIIRegressions { get; set; }
  public int CatIIIRemediations { get; set; }
  public int NetChange { get; set; }
}

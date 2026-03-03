namespace STIGForge.Core.Models;

/// <summary>
/// Record of a STIG release check comparing a baseline pack against newer packs.
/// </summary>
public sealed class ReleaseCheck
{
  public string CheckId { get; set; } = string.Empty;
  public DateTimeOffset CheckedAt { get; set; }
  public string BaselinePackId { get; set; } = string.Empty;
  public string? TargetPackId { get; set; }
  public string Status { get; set; } = string.Empty; // NoNewRelease, NewReleaseFound, DiffGenerated
  public string? SummaryJson { get; set; }
  public string? ReleaseNotesPath { get; set; }
}

/// <summary>
/// Structured release notes generated from a pack-to-pack diff.
/// </summary>
public sealed class ReleaseNotes
{
  public string BaselinePackId { get; set; } = string.Empty;
  public string TargetPackId { get; set; } = string.Empty;
  public DateTimeOffset GeneratedAt { get; set; }
  public int AddedCount { get; set; }
  public int RemovedCount { get; set; }
  public int ModifiedCount { get; set; }
  public int SeverityChangedCount { get; set; }
  public IReadOnlyList<string> HighlightedChanges { get; set; } = [];
  public ComplianceImpactEstimate? ComplianceImpact { get; set; }
}

/// <summary>
/// Estimated impact of a new STIG release on compliance scores.
/// </summary>
public sealed class ComplianceImpactEstimate
{
  public int NewControlsRequiringReview { get; set; }
  public int RemovedControlsAffectingScore { get; set; }
  public int SeverityEscalations { get; set; }
  public int SeverityDeescalations { get; set; }
}

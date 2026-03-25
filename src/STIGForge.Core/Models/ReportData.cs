namespace STIGForge.Core.Models;

/// <summary>
/// Pre-computed data container for the HTML executive compliance report.
/// All values are calculated before template rendering  -  the template only formats.
/// </summary>
public sealed class ExecutiveReportData
{
  // Header
  public string SystemName { get; set; } = string.Empty;
  public string BundleId { get; set; } = string.Empty;
  public DateTimeOffset GeneratedAt { get; set; }
  /// <summary>Pre-formatted date string for HTML templates (yyyy-MM-dd HH:mm).</summary>
  public string GeneratedAtFormatted { get; set; } = string.Empty;
  public double OverallCompliancePercent { get; set; }

  // Aggregate counts
  public int TotalControls { get; set; }
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int ErrorCount { get; set; }
  public int NotApplicableCount { get; set; }
  public int NotReviewedCount { get; set; }

  // Severity breakdown
  public SeverityBreakdown Severity { get; set; } = new();

  // Trend data (sparkline)
  public IReadOnlyList<TrendPoint> TrendData { get; set; } = [];

  // Top open findings
  public IReadOnlyList<OpenFinding> OpenFindings { get; set; } = [];

  // POA&M age summary
  public PoamAgeSummary PoamAges { get; set; } = new();

  // Per-STIG breakdown
  public IReadOnlyList<StigBreakdown> StigBreakdowns { get; set; } = [];

  // Audience level: executive, admin, auditor
  public string Audience { get; set; } = "executive";
}

/// <summary>CAT I/II/III pass and fail counts.</summary>
public sealed class SeverityBreakdown
{
  public int CatIPass { get; set; }
  public int CatIFail { get; set; }
  public int CatITotal { get; set; }
  public int CatIIPass { get; set; }
  public int CatIIFail { get; set; }
  public int CatIITotal { get; set; }
  public int CatIIIPass { get; set; }
  public int CatIIIFail { get; set; }
  public int CatIIITotal { get; set; }
}

/// <summary>Single point on a compliance trend sparkline.</summary>
public sealed class TrendPoint
{
  public DateTimeOffset CapturedAt { get; set; }
  public double CompliancePercent { get; set; }
}

/// <summary>An open (failed/error) finding for display in the report.</summary>
public sealed class OpenFinding
{
  public string VulnId { get; set; } = string.Empty;
  public string? Title { get; set; }
  public string? Severity { get; set; }
  public string Status { get; set; } = string.Empty;
  public string? Tool { get; set; }
  public string? SourceFile { get; set; }
  public DateTimeOffset? VerifiedAt { get; set; }
}

/// <summary>POA&M age distribution in buckets.</summary>
public sealed class PoamAgeSummary
{
  public int Age0To30 { get; set; }
  public int Age31To90 { get; set; }
  public int Age91Plus { get; set; }
}

/// <summary>Per-STIG compliance breakdown row.</summary>
public sealed class StigBreakdown
{
  public string BenchmarkId { get; set; } = string.Empty;
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int TotalCount { get; set; }
  public double CompliancePercent { get; set; }
}

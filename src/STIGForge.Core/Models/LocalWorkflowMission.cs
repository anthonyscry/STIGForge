namespace STIGForge.Core.Models;

public sealed class LocalWorkflowMission
{
  public IReadOnlyList<LocalWorkflowChecklistItem> CanonicalChecklist { get; set; } = Array.Empty<LocalWorkflowChecklistItem>();

  public IReadOnlyList<LocalWorkflowScannerEvidence> ScannerEvidence { get; set; } = Array.Empty<LocalWorkflowScannerEvidence>();

  public IReadOnlyList<LocalWorkflowUnmappedEvidence> Unmapped { get; set; } = Array.Empty<LocalWorkflowUnmappedEvidence>();

  public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

  public LocalWorkflowStageMetadata StageMetadata { get; set; } = new();
}

public sealed class LocalWorkflowStageMetadata
{
  public string MissionJsonPath { get; set; } = string.Empty;

  public string ConsolidatedJsonPath { get; set; } = string.Empty;

  public string ConsolidatedCsvPath { get; set; } = string.Empty;

  public string CoverageSummaryJsonPath { get; set; } = string.Empty;

  public string CoverageSummaryCsvPath { get; set; } = string.Empty;

  public DateTimeOffset StartedAt { get; set; }

  public DateTimeOffset FinishedAt { get; set; }
}

public sealed class LocalWorkflowChecklistItem
{
  public string StigId { get; set; } = string.Empty;

  public string RuleId { get; set; } = string.Empty;
}

public sealed class LocalWorkflowUnmappedEvidence
{
  public string Source { get; set; } = string.Empty;

  public string Reason { get; set; } = string.Empty;
}

public sealed class LocalWorkflowScannerEvidence
{
  public string Source { get; set; } = string.Empty;

  public string RuleId { get; set; } = string.Empty;
}

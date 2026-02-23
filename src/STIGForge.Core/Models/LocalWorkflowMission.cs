namespace STIGForge.Core.Models;

public sealed class LocalWorkflowMission
{
  public IReadOnlyList<LocalWorkflowChecklistItem> CanonicalChecklist { get; set; } = Array.Empty<LocalWorkflowChecklistItem>();

  public IReadOnlyList<LocalWorkflowUnmappedEvidence> Unmapped { get; set; } = Array.Empty<LocalWorkflowUnmappedEvidence>();
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

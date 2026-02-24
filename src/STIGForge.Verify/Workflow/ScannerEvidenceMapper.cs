using STIGForge.Core.Models;

namespace STIGForge.Verify;

public sealed class ScannerEvidenceMapper
{
  public ScannerEvidenceMapResult Map(
    IReadOnlyList<LocalWorkflowChecklistItem> canonicalChecklist,
    IReadOnlyList<ControlResult> findings)
  {
    if (canonicalChecklist == null)
      throw new ArgumentNullException(nameof(canonicalChecklist));

    if (findings == null)
      throw new ArgumentNullException(nameof(findings));

    var canonicalRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in canonicalChecklist)
    {
      var ruleId = item?.RuleId?.Trim() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(ruleId))
        canonicalRuleIds.Add(ruleId);
    }

    var mapped = new List<LocalWorkflowScannerEvidence>();
    var unmapped = new List<LocalWorkflowUnmappedEvidence>();
    var diagnostics = new List<string>();

    foreach (var finding in findings)
    {
      var ruleId = finding?.RuleId?.Trim() ?? string.Empty;
      var source = BuildSource(finding);

      if (string.IsNullOrWhiteSpace(ruleId))
      {
        unmapped.Add(new LocalWorkflowUnmappedEvidence
        {
          Source = source,
          Reason = "Missing RuleId in scanner finding."
        });

        diagnostics.Add("Unmapped scanner finding: missing RuleId (source=" + source + ").");
        continue;
      }

      if (!canonicalRuleIds.Contains(ruleId))
      {
        unmapped.Add(new LocalWorkflowUnmappedEvidence
        {
          Source = source + " rule=" + ruleId,
          Reason = "No canonical checklist match by RuleId."
        });

        diagnostics.Add("Unmapped scanner finding: RuleId " + ruleId + " not in canonical checklist.");
        continue;
      }

      mapped.Add(new LocalWorkflowScannerEvidence
      {
        Source = source,
        RuleId = ruleId
      });
    }

    return new ScannerEvidenceMapResult
    {
      ScannerEvidence = mapped,
      Unmapped = unmapped,
      Diagnostics = diagnostics
    };
  }

  private static string BuildSource(ControlResult? finding)
  {
    var sourceFile = finding?.SourceFile?.Trim() ?? string.Empty;
    var tool = finding?.Tool?.Trim() ?? string.Empty;

    if (!string.IsNullOrWhiteSpace(tool) && !string.IsNullOrWhiteSpace(sourceFile))
      return tool + ":" + sourceFile;

    if (!string.IsNullOrWhiteSpace(sourceFile))
      return sourceFile;

    if (!string.IsNullOrWhiteSpace(tool))
      return tool;

    return "scanner";
  }
}

public sealed class ScannerEvidenceMapResult
{
  public IReadOnlyList<LocalWorkflowScannerEvidence> ScannerEvidence { get; set; } = Array.Empty<LocalWorkflowScannerEvidence>();

  public IReadOnlyList<LocalWorkflowUnmappedEvidence> Unmapped { get; set; } = Array.Empty<LocalWorkflowUnmappedEvidence>();

  public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();
}

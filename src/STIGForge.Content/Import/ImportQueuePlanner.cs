namespace STIGForge.Content.Import;

public enum ContentImportRoute
{
  ConsolidatedZip,
  AdmxTemplatesFromZip
}

/// <summary>
/// Explicit staged transition states for a planned content import operation.
/// These represent the lifecycle of an import operation from initial detection
/// through final commit or failure, enabling observable and auditable workflows.
/// </summary>
public enum ImportOperationState
{
  /// <summary>Candidate detected in the inbox scan but not yet planned.</summary>
  Detected,
  /// <summary>Operation included in the deterministic import plan, ready to execute.</summary>
  Planned,
  /// <summary>Execution has begun; the import is in progress (staged before commit).</summary>
  Staged,
  /// <summary>Import completed successfully and content was persisted.</summary>
  Committed,
  /// <summary>Import failed during staging or commit; no content was persisted.</summary>
  Failed
}

public sealed class PlannedContentImport
{
  public string ZipPath { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public ImportArtifactKind ArtifactKind { get; set; }
  public ContentImportRoute Route { get; set; }
  public string SourceLabel { get; set; } = string.Empty;

  /// <summary>
  /// Current staged transition state of this planned import operation.
  /// Starts as <see cref="ImportOperationState.Planned"/> when emitted by the planner.
  /// Callers update this field as execution progresses to reflect observable staging transitions.
  /// </summary>
  public ImportOperationState State { get; set; } = ImportOperationState.Planned;

  /// <summary>
  /// Optional diagnostic detail captured when <see cref="State"/> transitions to
  /// <see cref="ImportOperationState.Failed"/>. Null for non-failed states.
  /// </summary>
  public string? FailureReason { get; set; }
}

public static class ImportQueuePlanner
{
  public static IReadOnlyList<PlannedContentImport> BuildContentImportPlan(IEnumerable<ImportInboxCandidate> winners)
  {
    if (winners == null)
      throw new ArgumentNullException(nameof(winners));

    var plan = new List<PlannedContentImport>();
    var grouped = winners
      .Where(c => c != null)
      .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
      .GroupBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .ToList();

    foreach (var group in grouped)
    {
      var candidates = group.ToList();
      if (candidates.Count == 0)
        continue;

      var hasStigOrScap = candidates.Any(c => c.ArtifactKind == ImportArtifactKind.Stig || c.ArtifactKind == ImportArtifactKind.Scap);

      var bestGpo = SelectBest(candidates, ImportArtifactKind.Gpo);
      var bestAdmx = SelectBest(candidates, ImportArtifactKind.Admx);

      if (!hasStigOrScap && bestGpo != null && bestAdmx != null)
      {
        plan.Add(ToOperation(bestGpo, ContentImportRoute.ConsolidatedZip, "gpo_lgpo_import"));
        plan.Add(ToOperation(bestAdmx, ContentImportRoute.AdmxTemplatesFromZip, "admx_template_import"));
        continue;
      }

      var primary = candidates
        .OrderBy(c => GetImportPriority(c.ArtifactKind))
        .ThenByDescending(c => c.Confidence)
        .ThenBy(c => c.ContentKey, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
        .First();

      var route = primary.ArtifactKind == ImportArtifactKind.Admx
        ? ContentImportRoute.AdmxTemplatesFromZip
        : ContentImportRoute.ConsolidatedZip;

      plan.Add(ToOperation(primary, route, MapSourceLabel(primary.ArtifactKind, route)));
    }

    return plan;
  }

  private static ImportInboxCandidate? SelectBest(IReadOnlyList<ImportInboxCandidate> candidates, ImportArtifactKind kind)
  {
    return candidates
      .Where(c => c.ArtifactKind == kind)
      .OrderBy(c => GetImportPriority(c.ArtifactKind))
      .ThenByDescending(c => c.Confidence)
      .ThenBy(c => c.ContentKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
      .FirstOrDefault();
  }

  private static PlannedContentImport ToOperation(ImportInboxCandidate candidate, ContentImportRoute route, string sourceLabel)
  {
    return new PlannedContentImport
    {
      ZipPath = candidate.ZipPath,
      FileName = candidate.FileName,
      ArtifactKind = candidate.ArtifactKind,
      Route = route,
      SourceLabel = sourceLabel,
      State = ImportOperationState.Planned
    };
  }

  private static string MapSourceLabel(ImportArtifactKind kind, ContentImportRoute route)
  {
    if (route == ContentImportRoute.AdmxTemplatesFromZip)
      return "admx_template_import";

    return kind switch
    {
      ImportArtifactKind.Stig => "stig_import",
      ImportArtifactKind.Scap => "scap_import",
      ImportArtifactKind.Gpo => "gpo_lgpo_import",
      ImportArtifactKind.Admx => "admx_import",
      _ => "import_scan"
    };
  }

  private static int GetImportPriority(ImportArtifactKind kind)
  {
    return kind switch
    {
      ImportArtifactKind.Stig => 0,
      ImportArtifactKind.Scap => 1,
      ImportArtifactKind.Gpo => 2,
      ImportArtifactKind.Admx => 3,
      _ => 10
    };
  }
}

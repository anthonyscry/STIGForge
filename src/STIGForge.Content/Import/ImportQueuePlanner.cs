namespace STIGForge.Content.Import;

public enum ContentImportRoute
{
  ConsolidatedZip,
  AdmxTemplatesFromZip
}

public sealed class PlannedContentImport
{
  public string ZipPath { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;
  public ImportArtifactKind ArtifactKind { get; set; }
  public ContentImportRoute Route { get; set; }
  public string SourceLabel { get; set; } = string.Empty;
}

public static class ImportQueuePlanner
{
  public static IReadOnlyList<PlannedContentImport> BuildContentImportPlan(
    IEnumerable<ImportInboxCandidate> winners,
    ImportProcessedArtifactLedger? processedLedger = null)
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
        TryAddPlannedOperation(plan, processedLedger, ToOperation(bestGpo, ContentImportRoute.ConsolidatedZip, "gpo_lgpo_import"));
        TryAddPlannedOperation(plan, processedLedger, ToOperation(bestAdmx, ContentImportRoute.AdmxTemplatesFromZip, "admx_template_import"));
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

      TryAddPlannedOperation(plan, processedLedger, ToOperation(primary, route, MapSourceLabel(primary.ArtifactKind, route)));
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
      Sha256 = candidate.Sha256,
      ArtifactKind = candidate.ArtifactKind,
      Route = route,
      SourceLabel = sourceLabel
    };
  }

  private static void TryAddPlannedOperation(
    ICollection<PlannedContentImport> plan,
    ImportProcessedArtifactLedger? processedLedger,
    PlannedContentImport operation)
  {
    if (processedLedger == null)
    {
      plan.Add(operation);
      return;
    }

    if (!processedLedger.TryBegin(operation.Sha256, operation.Route))
      return;

    plan.Add(operation);
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

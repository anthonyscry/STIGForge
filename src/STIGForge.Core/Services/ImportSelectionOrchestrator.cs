namespace STIGForge.Core.Services;

public enum ImportSelectionArtifactType
{
  Stig,
  Scap,
  Gpo,
  Admx
}

public sealed class ImportSelectionCandidate
{
  public ImportSelectionArtifactType ArtifactType { get; set; }
  public string Id { get; set; } = string.Empty;
  public bool IsSelected { get; set; }
}

public sealed class ImportSelectionPlanRow
{
  public ImportSelectionArtifactType ArtifactType { get; set; }
  public string Id { get; set; } = string.Empty;
  public bool IsSelected { get; set; }
  public bool IsLocked { get; set; }
}

public sealed class ImportSelectionPlanWarning
{
  public string Code { get; set; } = string.Empty;
  public string Severity { get; set; } = string.Empty;
}

public sealed class ImportSelectionPlan
{
  public IReadOnlyList<ImportSelectionPlanRow> Rows { get; init; } = Array.Empty<ImportSelectionPlanRow>();
  public IReadOnlyList<ImportSelectionPlanWarning> Warnings { get; init; } = Array.Empty<ImportSelectionPlanWarning>();
}

public sealed class ImportSelectionOrchestrator
{
  public ImportSelectionPlan BuildPlan(IEnumerable<ImportSelectionCandidate> candidates)
  {
    if (candidates == null)
      throw new ArgumentNullException(nameof(candidates));

    var rows = candidates
      .Where(c => c != null)
      .OrderBy(c => GetPriority(c.ArtifactType))
      .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
      .Select(c => new ImportSelectionPlanRow
      {
        ArtifactType = c.ArtifactType,
        Id = c.Id,
        IsSelected = c.IsSelected
      })
      .ToList();

    var hasSelectedStig = rows.Any(x => x.ArtifactType == ImportSelectionArtifactType.Stig && x.IsSelected);
    if (!hasSelectedStig)
      return new ImportSelectionPlan { Rows = rows };

    foreach (var row in rows)
    {
      if (row.ArtifactType == ImportSelectionArtifactType.Scap
          || row.ArtifactType == ImportSelectionArtifactType.Gpo
          || row.ArtifactType == ImportSelectionArtifactType.Admx)
      {
        row.IsSelected = true;
        row.IsLocked = true;
      }
    }

    if (rows.All(x => x.ArtifactType != ImportSelectionArtifactType.Scap))
    {
      return new ImportSelectionPlan
      {
        Rows = rows,
        Warnings =
        [
          new ImportSelectionPlanWarning
          {
            Code = "missing_scap_dependency",
            Severity = "warning"
          }
        ]
      };
    }

    return new ImportSelectionPlan { Rows = rows };
  }

  private static int GetPriority(ImportSelectionArtifactType artifactType)
  {
    return artifactType switch
    {
      ImportSelectionArtifactType.Stig => 0,
      ImportSelectionArtifactType.Scap => 1,
      ImportSelectionArtifactType.Gpo => 2,
      ImportSelectionArtifactType.Admx => 3,
      _ => 10
    };
  }
}

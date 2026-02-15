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
  public int StigRuleCount { get; set; }
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
  public ImportSelectionPlanCounts Counts { get; init; } = new();
  public string Fingerprint { get; init; } = string.Empty;
}

public sealed class ImportSelectionPlanCounts
{
  public int StigSelected { get; init; }
  public int ScapAutoIncluded { get; init; }
  public int RuleCount { get; init; }
}

public sealed class ImportSelectionOrchestrator
{
  public ImportSelectionPlan BuildPlan(IEnumerable<ImportSelectionCandidate> candidates)
  {
    if (candidates == null)
      throw new ArgumentNullException(nameof(candidates));

    var normalizedCandidates = candidates
      .Where(c => c != null)
      .ToList();

    var rows = normalizedCandidates
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
      return BuildResult(rows, Array.Empty<ImportSelectionPlanWarning>(), normalizedCandidates);

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
      return BuildResult(
        rows,
        [
          new ImportSelectionPlanWarning
          {
            Code = "missing_scap_dependency",
            Severity = "warning"
          }
        ],
        normalizedCandidates);
    }

    return BuildResult(rows, Array.Empty<ImportSelectionPlanWarning>(), normalizedCandidates);
  }

  private static ImportSelectionPlan BuildResult(
    IReadOnlyList<ImportSelectionPlanRow> rows,
    IReadOnlyList<ImportSelectionPlanWarning> warnings,
    IReadOnlyList<ImportSelectionCandidate> candidates)
  {
    var counts = new ImportSelectionPlanCounts
    {
      StigSelected = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Stig && x.IsSelected),
      ScapAutoIncluded = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Scap && x.IsSelected && x.IsLocked),
      RuleCount = candidates
        .Where(x => x.ArtifactType == ImportSelectionArtifactType.Stig && x.IsSelected)
        .Sum(x => x.StigRuleCount)
    };

    return new ImportSelectionPlan
    {
      Rows = rows,
      Warnings = warnings,
      Counts = counts,
      Fingerprint = BuildFingerprint(rows, warnings, counts)
    };
  }

  private static string BuildFingerprint(
    IReadOnlyList<ImportSelectionPlanRow> rows,
    IReadOnlyList<ImportSelectionPlanWarning> warnings,
    ImportSelectionPlanCounts counts)
  {
    var fingerprintSource = string.Join("|", rows.Select(x =>
      $"row:{(int)x.ArtifactType}:{x.Id.ToUpperInvariant()}:{x.IsSelected}:{x.IsLocked}"));
    var warningSource = string.Join("|", warnings
      .OrderBy(x => x.Code, StringComparer.Ordinal)
      .ThenBy(x => x.Severity, StringComparer.Ordinal)
      .Select(x => $"warn:{x.Code}:{x.Severity}"));
    var countSource = $"counts:{counts.StigSelected}:{counts.ScapAutoIncluded}:{counts.RuleCount}";
    var payload = string.Join("|", fingerprintSource, warningSource, countSource);

    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(hash).ToLowerInvariant();
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

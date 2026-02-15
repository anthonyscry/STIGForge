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
  public IReadOnlyList<ImportSelectionPlanRow> Rows { get; set; } = Array.Empty<ImportSelectionPlanRow>();
  public IReadOnlyList<ImportSelectionPlanWarning> Warnings { get; set; } = Array.Empty<ImportSelectionPlanWarning>();
  public IReadOnlyList<string> WarningLines { get; set; } = Array.Empty<string>();
  public ImportSelectionPlanCounts Counts { get; set; } = new();
  public string StatusSummaryText { get; set; } = string.Empty;
  public string Fingerprint { get; set; } = string.Empty;
}

public sealed class ImportSelectionPlanCounts
{
  public int StigSelected { get; set; }
  public int ScapAutoIncluded { get; set; }
  public int RuleCount { get; set; }
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
      WarningLines = BuildWarningLines(warnings),
      Counts = counts,
      StatusSummaryText = BuildStatusSummaryText(rows),
      Fingerprint = BuildFingerprint(rows, warnings, counts)
    };
  }

  private static IReadOnlyList<string> BuildWarningLines(IReadOnlyList<ImportSelectionPlanWarning> warnings)
  {
    if (warnings.Count == 0)
      return Array.Empty<string>();

    var lines = new List<string>(warnings.Count);
    foreach (var warning in warnings
      .OrderBy(x => x.Code, StringComparer.Ordinal)
      .ThenBy(x => x.Severity, StringComparer.Ordinal))
    {
      if (string.Equals(warning.Code, "missing_scap_dependency", StringComparison.Ordinal))
        lines.Add("Missing SCAP dependency: selected STIG content has no matching SCAP package.");
      else
        lines.Add("Warning: " + warning.Code);
    }

    return lines;
  }

  private static string BuildStatusSummaryText(IReadOnlyList<ImportSelectionPlanRow> rows)
  {
    var stigCount = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Stig && x.IsSelected);
    var scapCount = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Scap && x.IsSelected);
    var gpoCount = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Gpo && x.IsSelected);
    var admxCount = rows.Count(x => x.ArtifactType == ImportSelectionArtifactType.Admx && x.IsSelected);

    return "STIG: " + stigCount
      + " | Auto SCAP: " + scapCount
      + " | Auto GPO: " + gpoCount
      + " | Auto ADMX: " + admxCount;
  }

  private static string BuildFingerprint(
    IReadOnlyList<ImportSelectionPlanRow> rows,
    IReadOnlyList<ImportSelectionPlanWarning> warnings,
    ImportSelectionPlanCounts counts)
  {
    var canonical = new
    {
      rows = rows.Select(x => new
      {
        artifactType = (int)x.ArtifactType,
        id = x.Id.ToUpperInvariant(),
        isSelected = x.IsSelected,
        isLocked = x.IsLocked
      }),
      warnings = warnings
        .OrderBy(x => x.Code, StringComparer.Ordinal)
        .ThenBy(x => x.Severity, StringComparer.Ordinal)
        .Select(x => new
        {
          code = x.Code,
          severity = x.Severity
        }),
      counts = new
      {
        stigSelected = counts.StigSelected,
        scapAutoIncluded = counts.ScapAutoIncluded,
        ruleCount = counts.RuleCount
      }
    };

    var payload = System.Text.Json.JsonSerializer.Serialize(canonical);
    using var sha = System.Security.Cryptography.SHA256.Create();
    var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
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

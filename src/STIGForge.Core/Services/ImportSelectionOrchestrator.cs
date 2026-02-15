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
}

public sealed class ImportSelectionOrchestrator
{
  public IReadOnlyList<ImportSelectionCandidate> BuildPlan(IEnumerable<ImportSelectionCandidate> candidates)
  {
    if (candidates == null)
      throw new ArgumentNullException(nameof(candidates));

    return candidates
      .Where(c => c != null)
      .OrderBy(c => GetPriority(c.ArtifactType))
      .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
      .ToList();
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

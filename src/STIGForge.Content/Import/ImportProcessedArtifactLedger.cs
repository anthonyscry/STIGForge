namespace STIGForge.Content.Import;

public class ImportProcessedArtifactLedger
{
  private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

  public bool TryBegin(string sha256, ContentImportRoute route)
  {
    var normalized = sha256.Trim().ToLowerInvariant();
    var key = route + ":" + normalized;
    return _keys.Add(key);
  }
}

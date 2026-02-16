namespace STIGForge.Content.Import;

public sealed class ImportProcessedArtifactLedger
{
  private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

  public bool TryBegin(string sha256, ContentImportRoute route)
  {
    if (string.IsNullOrWhiteSpace(sha256))
    {
      throw new ArgumentException("SHA-256 is required.", nameof(sha256));
    }

    var normalized = sha256.Trim();
    var key = route + ":" + normalized;
    return _keys.Add(key);
  }
}

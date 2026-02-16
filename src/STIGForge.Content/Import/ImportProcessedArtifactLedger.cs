namespace STIGForge.Content.Import;

public sealed class ImportProcessedArtifactLedger
{
  private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

  public bool IsProcessed(string sha256, ContentImportRoute route)
  {
    var key = BuildKey(sha256, route);
    return _keys.Contains(key);
  }

  public bool MarkProcessed(string sha256, ContentImportRoute route)
  {
    var key = BuildKey(sha256, route);
    return _keys.Add(key);
  }

  public IReadOnlyList<string> Snapshot()
  {
    return _keys
      .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
      .ToArray();
  }

  public void Load(IEnumerable<string> keys)
  {
    if (keys == null)
      throw new ArgumentNullException(nameof(keys));

    _keys.Clear();

    foreach (var key in keys)
    {
      if (string.IsNullOrWhiteSpace(key))
        continue;

      if (TryNormalizeKey(key, out var normalized))
        _keys.Add(normalized);
    }
  }

  private static bool TryNormalizeKey(string key, out string normalized)
  {
    normalized = string.Empty;

    var trimmed = key.Trim();
    var colonIndex = trimmed.IndexOf(':');
    if (colonIndex <= 0 || colonIndex != trimmed.LastIndexOf(':'))
      return false;

    var routeToken = trimmed.Substring(0, colonIndex).Trim();
    var hashToken = trimmed.Substring(colonIndex + 1).Trim();
    if (string.IsNullOrWhiteSpace(routeToken) || string.IsNullOrWhiteSpace(hashToken))
      return false;

    if (!Enum.TryParse<ContentImportRoute>(routeToken, true, out var route))
      return false;

    normalized = route + ":" + hashToken;
    return true;
  }

  private static string BuildKey(string sha256, ContentImportRoute route)
  {
    if (string.IsNullOrWhiteSpace(sha256))
      throw new ArgumentException("SHA-256 is required.", nameof(sha256));

    var normalized = sha256.Trim();
    return route + ":" + normalized;
  }
}

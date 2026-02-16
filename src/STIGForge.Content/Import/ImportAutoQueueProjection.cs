namespace STIGForge.Content.Import;

public sealed class ImportAutoProjectionRow
{
  public PlannedContentImport Planned { get; set; } = new();

  public string StateLabel { get; set; } = string.Empty;
}

public sealed class ImportAutoProjectionResult
{
  public IReadOnlyList<ImportAutoProjectionRow> AutoCommitted { get; set; } = Array.Empty<ImportAutoProjectionRow>();

  public IReadOnlyList<ImportAutoProjectionRow> Exceptions { get; set; } = Array.Empty<ImportAutoProjectionRow>();
}

public static class ImportAutoQueueProjection
{
  private const string AutoCommittedLabel = "AutoCommitted";
  private const string FailedLabel = "Failed";

  public static ImportAutoProjectionResult Project(IReadOnlyList<PlannedContentImport> planned, IReadOnlyList<string> failures)
  {
    if (planned == null)
      throw new ArgumentNullException(nameof(planned));

    if (failures == null)
      throw new ArgumentNullException(nameof(failures));

    var failedRowIndexes = ResolveFailedRowIndexes(planned, failures);

    var autoCommitted = new List<ImportAutoProjectionRow>();
    var exceptions = new List<ImportAutoProjectionRow>();

    for (var index = 0; index < planned.Count; index++)
    {
      var row = planned[index];
      if (row == null)
        continue;

      var projectionRow = new ImportAutoProjectionRow
      {
        Planned = row,
        StateLabel = failedRowIndexes.Contains(index) ? FailedLabel : AutoCommittedLabel
      };

      if (string.Equals(projectionRow.StateLabel, FailedLabel, StringComparison.Ordinal))
        exceptions.Add(projectionRow);
      else
        autoCommitted.Add(projectionRow);
    }

    return new ImportAutoProjectionResult
    {
      AutoCommitted = autoCommitted,
      Exceptions = exceptions
    };
  }

  private static HashSet<int> ResolveFailedRowIndexes(IReadOnlyList<PlannedContentImport> planned, IReadOnlyList<string> failures)
  {
    var byPathRoute = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
    var byFileRoute = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
    var byFileName = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < planned.Count; index++)
    {
      var row = planned[index];
      if (row == null)
        continue;

      var route = row.Route.ToString();
      var fileName = row.FileName?.Trim() ?? string.Empty;
      var zipPath = row.ZipPath?.Trim() ?? string.Empty;

      if (!string.IsNullOrWhiteSpace(zipPath))
        Enqueue(byPathRoute, BuildRouteKey(zipPath, route), index);

      if (!string.IsNullOrWhiteSpace(fileName))
      {
        Enqueue(byFileRoute, BuildRouteKey(fileName, route), index);
        Enqueue(byFileName, fileName, index);
      }
    }

    var failedIndexes = new HashSet<int>();
    foreach (var failure in failures)
    {
      var parsed = ParseFailure(failure);
      if (string.IsNullOrWhiteSpace(parsed.Identifier))
        continue;

      if (parsed.Route.HasValue)
      {
        var routeName = parsed.Route.Value.ToString();
        if (TryConsume(byPathRoute, BuildRouteKey(parsed.Identifier, routeName), failedIndexes))
          continue;

        var fileName = ExtractFileName(parsed.Identifier);
        if (TryConsume(byFileRoute, BuildRouteKey(fileName, routeName), failedIndexes))
          continue;
      }

      if (TryConsume(byFileName, parsed.Identifier, failedIndexes))
        continue;

      TryConsume(byFileName, ExtractFileName(parsed.Identifier), failedIndexes);
    }

    return failedIndexes;
  }

  private static bool TryConsume(Dictionary<string, Queue<int>> index, string key, ISet<int> target)
  {
    if (string.IsNullOrWhiteSpace(key))
      return false;

    if (!index.TryGetValue(key, out var queue))
      return false;

    while (queue.Count > 0)
    {
      var candidate = queue.Dequeue();
      if (target.Add(candidate))
        return true;
    }

    return false;
  }

  private static void Enqueue(Dictionary<string, Queue<int>> index, string key, int rowIndex)
  {
    if (string.IsNullOrWhiteSpace(key))
      return;

    if (!index.TryGetValue(key, out var queue))
    {
      queue = new Queue<int>();
      index[key] = queue;
    }

    queue.Enqueue(rowIndex);
  }

  private static FailureParseResult ParseFailure(string? failure)
  {
    if (string.IsNullOrWhiteSpace(failure))
      return new FailureParseResult(string.Empty, null);

    var trimmed = (failure ?? string.Empty).Trim();

    var closeRouteAndColonIndex = trimmed.IndexOf("):", StringComparison.Ordinal);
    if (closeRouteAndColonIndex > 0)
    {
      var routeCloseParenIndex = closeRouteAndColonIndex;
      var routeOpenParenIndex = trimmed.LastIndexOf('(', routeCloseParenIndex);
      if (routeOpenParenIndex > 0)
      {
        var routeName = trimmed.Substring(routeOpenParenIndex + 1, routeCloseParenIndex - routeOpenParenIndex - 1).Trim();
        if (Enum.TryParse<ContentImportRoute>(routeName, true, out var route))
        {
          var identifier = trimmed.Substring(0, routeOpenParenIndex).TrimEnd();
          return new FailureParseResult(identifier, route);
        }
      }
    }

    var detailsOpenParenIndex = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
    if (detailsOpenParenIndex > 0)
      return new FailureParseResult(trimmed.Substring(0, detailsOpenParenIndex).TrimEnd(), null);

    return new FailureParseResult(trimmed, null);
  }

  private static string BuildRouteKey(string identifier, string routeName)
  {
    return identifier.Trim() + "|" + routeName.Trim();
  }

  private static string ExtractFileName(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return string.Empty;

    var trimmed = value.Trim();
    var slashIndex = trimmed.LastIndexOfAny(new[] { '\\', '/' });
    if (slashIndex < 0 || slashIndex >= trimmed.Length - 1)
      return trimmed;

    return trimmed.Substring(slashIndex + 1);
  }

  private readonly struct FailureParseResult
  {
    public FailureParseResult(string identifier, ContentImportRoute? route)
    {
      Identifier = identifier;
      Route = route;
    }

    public string Identifier { get; }

    public ContentImportRoute? Route { get; }
  }
}

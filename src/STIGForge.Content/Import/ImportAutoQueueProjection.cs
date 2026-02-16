namespace STIGForge.Content.Import;

public sealed class ImportAutoProjectionRow
{
  public PlannedContentImport Planned { get; init; } = new();

  public string StateLabel { get; init; } = string.Empty;
}

public sealed class ImportAutoProjectionResult
{
  public IReadOnlyList<ImportAutoProjectionRow> AutoCommitted { get; init; } = Array.Empty<ImportAutoProjectionRow>();

  public IReadOnlyList<ImportAutoProjectionRow> Exceptions { get; init; } = Array.Empty<ImportAutoProjectionRow>();
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

    var failedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var failure in failures)
    {
      var fileName = ParseFailureFileName(failure);
      if (!string.IsNullOrWhiteSpace(fileName))
        failedFileNames.Add(fileName);
    }

    var autoCommitted = new List<ImportAutoProjectionRow>();
    var exceptions = new List<ImportAutoProjectionRow>();

    foreach (var row in planned)
    {
      if (row == null)
        continue;

      var fileName = row.FileName?.Trim() ?? string.Empty;
      var projectionRow = new ImportAutoProjectionRow
      {
        Planned = row,
        StateLabel = failedFileNames.Contains(fileName) ? FailedLabel : AutoCommittedLabel
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

  private static string ParseFailureFileName(string? failure)
  {
    if (string.IsNullOrWhiteSpace(failure))
      return string.Empty;

    var trimmed = failure.Trim();
    var parenIndex = trimmed.IndexOf('(');
    if (parenIndex >= 0)
      return trimmed[..parenIndex].Trim();

    return trimmed;
  }
}

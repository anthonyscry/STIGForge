namespace STIGForge.Content.Import;

public sealed class ImportScanSummary
{
  public string ImportFolder { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public int ArchiveCount { get; set; }
  public int CandidateCount { get; set; }
  public int WinnerCount { get; set; }
  public int SuppressedCount { get; set; }
  public int ImportedPackCount { get; set; }
  public int ImportedToolCount { get; set; }
  public Dictionary<string, int> ImportedByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> Failures { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> ProcessedArtifactLedgerSnapshot { get; set; } = Array.Empty<string>();
}

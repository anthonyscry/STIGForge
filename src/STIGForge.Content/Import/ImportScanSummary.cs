namespace STIGForge.Content.Import;

/// <summary>
/// Per-operation staged outcome row emitted in the import scan summary artifacts.
/// Records the deterministic staged transition result for each planned content import operation.
/// </summary>
public sealed class StagedOperationOutcome
{
  /// <summary>File name of the source ZIP archive for this operation.</summary>
  public string FileName { get; set; } = string.Empty;

  /// <summary>The artifact kind detected and planned for this operation.</summary>
  public string ArtifactKind { get; set; } = string.Empty;

  /// <summary>The import route selected by the planner.</summary>
  public string Route { get; set; } = string.Empty;

  /// <summary>The source label used during import.</summary>
  public string SourceLabel { get; set; } = string.Empty;

  /// <summary>
  /// Final staged transition state after execution.
  /// One of: Planned, Staged, Committed, Failed.
  /// </summary>
  public string State { get; set; } = string.Empty;

  /// <summary>
  /// Failure reason captured if <see cref="State"/> is <c>Failed</c>.
  /// Null for all other states.
  /// </summary>
  public string? FailureReason { get; set; }

  /// <summary>Number of content packs committed for this operation. Zero on failure.</summary>
  public int CommittedPackCount { get; set; }
}

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

  /// <summary>
  /// Per-operation staged lifecycle outcomes.
  /// Each planned content import operation emits one row showing its
  /// deterministic staged transition result (Committed or Failed).
  /// </summary>
  public IReadOnlyList<StagedOperationOutcome> StagedOutcomes { get; set; } = Array.Empty<StagedOperationOutcome>();

  /// <summary>Count of operations that transitioned to Committed state.</summary>
  public int StagedCommittedCount { get; set; }

  /// <summary>Count of operations that transitioned to Failed state.</summary>
  public int StagedFailedCount { get; set; }
}

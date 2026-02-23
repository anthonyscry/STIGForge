namespace STIGForge.Evidence;

/// <summary>
/// Top-level evidence index manifest for a bundle.
/// Written to Evidence/evidence_index.json.
/// </summary>
public sealed class EvidenceIndex
{
  public string BundleRoot { get; set; } = string.Empty;
  public DateTimeOffset IndexedAt { get; set; }
  public int TotalEntries { get; set; }
  public List<EvidenceIndexEntry> Entries { get; set; } = new();
}

/// <summary>
/// Flattened evidence entry in the index, derived from EvidenceMetadata files.
/// </summary>
public sealed class EvidenceIndexEntry
{
  public string EvidenceId { get; set; } = string.Empty;
  public string ControlKey { get; set; } = string.Empty;
  public string? RuleId { get; set; }
  public string? ControlId { get; set; }
  public string? Title { get; set; }
  public string Type { get; set; } = string.Empty;
  public string Source { get; set; } = string.Empty;
  public string TimestampUtc { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;
  public string RelativePath { get; set; } = string.Empty;
  public string? RunId { get; set; }
  public string? StepName { get; set; }
  public string? SupersedesEvidenceId { get; set; }
  public Dictionary<string, string>? Tags { get; set; }
}

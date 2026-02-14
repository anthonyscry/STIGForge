namespace STIGForge.Content.Import;

public enum ImportArtifactKind
{
  Unknown,
  Stig,
  Scap,
  Gpo,
  Admx,
  Tool
}

public enum ToolArtifactKind
{
  Unknown,
  EvaluateStig,
  Scc,
  PowerStig
}

public sealed class ImportInboxCandidate
{
  public string ZipPath { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;
  public ImportArtifactKind ArtifactKind { get; set; }
  public ToolArtifactKind ToolKind { get; set; }
  public DetectionConfidence Confidence { get; set; } = DetectionConfidence.Low;
  public string ContentKey { get; set; } = string.Empty;
  public string VersionTag { get; set; } = string.Empty;
  public DateTimeOffset? BenchmarkDate { get; set; }
  public List<string> Reasons { get; set; } = new();
}

public sealed class ImportInboxScanResult
{
  public IReadOnlyList<ImportInboxCandidate> Candidates { get; set; } = Array.Empty<ImportInboxCandidate>();
  public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class ImportDedupOutcome
{
  public IReadOnlyList<ImportInboxCandidate> Winners { get; set; } = Array.Empty<ImportInboxCandidate>();
  public IReadOnlyList<ImportInboxCandidate> Suppressed { get; set; } = Array.Empty<ImportInboxCandidate>();
}

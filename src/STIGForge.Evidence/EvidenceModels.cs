namespace STIGForge.Evidence;

public enum EvidenceArtifactType
{
  Command,
  File,
  Registry,
  PolicyExport,
  Screenshot,
  Other
}

public sealed class EvidenceWriteRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? ControlId { get; set; }
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public EvidenceArtifactType Type { get; set; } = EvidenceArtifactType.Other;
  public string Source { get; set; } = "EvidenceAutopilot";
  public string? Command { get; set; }
  public string? ContentText { get; set; }
  public string? SourceFilePath { get; set; }
  public string? FileExtension { get; set; }
  public Dictionary<string, string>? Tags { get; set; }
}

public sealed class EvidenceMetadata
{
  public string? ControlId { get; set; }
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public string Type { get; set; } = string.Empty;
  public string Source { get; set; } = string.Empty;
  public string? Command { get; set; }
  public string? OriginalPath { get; set; }
  public string TimestampUtc { get; set; } = string.Empty;
  public string Host { get; set; } = string.Empty;
  public string User { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;
  public Dictionary<string, string>? Tags { get; set; }
}

public sealed class EvidenceWriteResult
{
  public string EvidenceDir { get; set; } = string.Empty;
  public string EvidencePath { get; set; } = string.Empty;
  public string MetadataPath { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;
}

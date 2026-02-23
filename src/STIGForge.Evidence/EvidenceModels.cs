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

  /// <summary>Run ID for apply-run provenance linkage (optional for non-apply evidence).</summary>
  public string? RunId { get; set; }

  /// <summary>Step name within the apply run that produced this evidence (e.g., powerstig_compile, apply_script, apply_dsc).</summary>
  public string? StepName { get; set; }

  /// <summary>
  /// Evidence ID from a prior run that this record supersedes or is retained from.
  /// Enables downstream proof/export to trace lineage across reruns.
  /// </summary>
  public string? SupersedesEvidenceId { get; set; }
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

  /// <summary>Run ID for apply-run provenance. Null for manually collected evidence.</summary>
  public string? RunId { get; set; }

  /// <summary>Apply step name that produced this evidence (e.g., powerstig_compile, apply_script, apply_dsc).</summary>
  public string? StepName { get; set; }

  /// <summary>
  /// Evidence ID from a prior run that this record supersedes or is retained from.
  /// Null when no prior run comparison is available.
  /// </summary>
  public string? SupersedesEvidenceId { get; set; }
}

public sealed class EvidenceWriteResult
{
  public string EvidenceDir { get; set; } = string.Empty;
  public string EvidencePath { get; set; } = string.Empty;
  public string MetadataPath { get; set; } = string.Empty;
  public string Sha256 { get; set; } = string.Empty;

  /// <summary>Stable identifier for this evidence record (basename without extension), usable for lineage references.</summary>
  public string EvidenceId { get; set; } = string.Empty;
}

namespace STIGForge.Core.Models;

public sealed class ManualAnswer
{
  public string? RuleId { get; set; }
  public string? VulnId { get; set; }
  public string Status { get; set; } = "Open";
  public string? Reason { get; set; }
  public string? Comment { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AnswerFile
{
  public string? ProfileId { get; set; }
  public string? PackId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public List<ManualAnswer> Answers { get; set; } = new();
}

/// <summary>
/// Portable answer file format for cross-bundle and cross-system export/import.
/// Wraps AnswerFile with export-specific metadata for traceability.
/// </summary>
public sealed class AnswerFileExport
{
  public string? StigId { get; set; }
  public string? ExportedAt { get; set; }
  public string? ExportedBy { get; set; }
  public AnswerFile Answers { get; set; } = new();
}

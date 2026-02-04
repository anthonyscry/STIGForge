namespace STIGForge.Verify;

public sealed class ControlResult
{
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public string? Title { get; set; }
  public string? Severity { get; set; }
  public string? Status { get; set; }
  public string? FindingDetails { get; set; }
  public string? Comments { get; set; }
  public string Tool { get; set; } = string.Empty;
  public string SourceFile { get; set; } = string.Empty;
  public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class VerifyRunResult
{
  public int ExitCode { get; set; }
  public string Output { get; set; } = string.Empty;
  public string Error { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
}

public sealed class VerifyReport
{
  public string Tool { get; set; } = string.Empty;
  public string ToolVersion { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public string OutputRoot { get; set; } = string.Empty;
  public IReadOnlyList<ControlResult> Results { get; set; } = Array.Empty<ControlResult>();
}

public sealed class CoverageSummary
{
  public string Tool { get; set; } = string.Empty;
  public int ClosedCount { get; set; }
  public int OpenCount { get; set; }
  public int TotalCount { get; set; }
  public double ClosedPercent { get; set; }
}

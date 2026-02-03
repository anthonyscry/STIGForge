namespace STIGForge.Core.Models;

public sealed class ExternalIds
{
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public string? SrgId { get; set; }
  public string? BenchmarkId { get; set; }
}

public sealed class RevisionInfo
{
  public string PackName { get; set; } = string.Empty;
  public string? BenchmarkVersion { get; set; }
  public string? BenchmarkRelease { get; set; }
  public DateTimeOffset? BenchmarkDate { get; set; }
}

public sealed class Applicability
{
  public OsTarget OsTarget { get; set; }
  public IReadOnlyCollection<RoleTemplate> RoleTags { get; set; } = Array.Empty<RoleTemplate>();
  public ScopeTag ClassificationScope { get; set; }
  public Confidence Confidence { get; set; }
}

public sealed class ControlRecord
{
  public string ControlId { get; set; } = string.Empty;
  public ExternalIds ExternalIds { get; set; } = new();
  public string Title { get; set; } = string.Empty;
  public string Severity { get; set; } = "unknown";
  public string? Discussion { get; set; }
  public string? CheckText { get; set; }
  public string? FixText { get; set; }
  public bool IsManual { get; set; }
  public string? WizardPrompt { get; set; }
  public Applicability Applicability { get; set; } = new();
  public RevisionInfo Revision { get; set; } = new();
}

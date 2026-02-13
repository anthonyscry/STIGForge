namespace STIGForge.Core.Models;

public sealed class ControlAnnotation
{
  public string RuleId { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string Notes { get; set; } = string.Empty;
  public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AnnotationFile
{
  public List<ControlAnnotation> Annotations { get; set; } = new();
}

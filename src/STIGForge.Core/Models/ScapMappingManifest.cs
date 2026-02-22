using System.Text.Json.Serialization;

namespace STIGForge.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScapMappingMethod
{
  BenchmarkOverlap,
  StrictTagMatch,
  Unmapped
}

public sealed class ScapControlMapping
{
  public string VulnId { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string? BenchmarkId { get; set; }
  public string? BenchmarkRuleRef { get; set; }
  public ScapMappingMethod Method { get; set; }
  public double Confidence { get; set; }
  public string? Reason { get; set; }
}

public sealed class ScapMappingManifest
{
  public string StigPackId { get; set; } = string.Empty;
  public string StigName { get; set; } = string.Empty;
  public string? SelectedBenchmarkPackId { get; set; }
  public string? SelectedBenchmarkName { get; set; }
  public IReadOnlyList<string> SelectionReasons { get; set; } = Array.Empty<string>();
  public IReadOnlyList<ScapControlMapping> ControlMappings { get; set; } = Array.Empty<ScapControlMapping>();
  public DateTimeOffset GeneratedAt { get; set; }

  [JsonIgnore]
  public int UnmappedCount => ControlMappings.Count(m => m.Method == ScapMappingMethod.Unmapped);
}

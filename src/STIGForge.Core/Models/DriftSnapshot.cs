namespace STIGForge.Core.Models;

public static class DriftChangeTypes
{
  public const string BaselineEstablished = "BaselineEstablished";
  public const string StateChanged = "StateChanged";
  public const string MissingInCurrentScan = "MissingInCurrentScan";
}

public sealed class DriftSnapshot
{
  public string SnapshotId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string? PreviousState { get; set; }
  public string CurrentState { get; set; } = string.Empty;
  public string ChangeType { get; set; } = string.Empty;
  public DateTimeOffset DetectedAt { get; set; }
}

public sealed class DriftCheckResult
{
  public string BundleRoot { get; set; } = string.Empty;
  public DateTimeOffset CheckedAt { get; set; }
  public int CurrentRuleCount { get; set; }
  public int BaselineRuleCount { get; set; }
  public IReadOnlyList<DriftSnapshot> DriftEvents { get; set; } = Array.Empty<DriftSnapshot>();
  public IReadOnlyList<string> AutoRemediatedRuleIds { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> RemediationErrors { get; set; } = Array.Empty<string>();
}

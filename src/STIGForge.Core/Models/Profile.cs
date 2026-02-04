namespace STIGForge.Core.Models;

public sealed class NaPolicy
{
  public bool AutoNaOutOfScope { get; set; }
  public Confidence ConfidenceThreshold { get; set; }
  public string DefaultNaCommentTemplate { get; set; } = string.Empty;
}

public enum AutomationMode
{
  Conservative,
  Standard,
  Aggressive
}

public enum ReleaseDateSource
{
  ContentPack,
  RuleRevision,
  ManualOverride
}

public sealed class AutomationPolicy
{
  public AutomationMode Mode { get; set; } = AutomationMode.Standard;
  public int NewRuleGraceDays { get; set; } = 30;
  public bool AutoApplyRequiresMapping { get; set; } = true;
  public ReleaseDateSource ReleaseDateSource { get; set; } = ReleaseDateSource.ContentPack;
}

public sealed class Profile
{
  public string ProfileId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public OsTarget OsTarget { get; set; }
  public RoleTemplate RoleTemplate { get; set; }
  public HardeningMode HardeningMode { get; set; }
  public ClassificationMode ClassificationMode { get; set; }
  public NaPolicy NaPolicy { get; set; } = new();
  public AutomationPolicy AutomationPolicy { get; set; } = new();
  public IReadOnlyList<string> OverlayIds { get; set; } = Array.Empty<string>();
}

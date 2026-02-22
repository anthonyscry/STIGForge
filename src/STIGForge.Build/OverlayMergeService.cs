using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Build;

/// <summary>
/// Service for merging overlay decisions into compiled controls with deterministic ordering.
/// </summary>
public sealed class OverlayMergeService
{
  /// <summary>
  /// Merge overlay decisions into compiled controls using last-wins precedence.
  /// </summary>
  public OverlayMergeResult Merge(IReadOnlyList<CompiledControl> compiledControls, IReadOnlyList<Overlay> overlays)
  {
    ArgumentNullException.ThrowIfNull(compiledControls);
    ArgumentNullException.ThrowIfNull(overlays);

    // Clone controls to avoid mutating input
    var mergedControls = compiledControls
      .Select(Clone)
      .ToList();

    if (overlays.Count == 0)
    {
      return new OverlayMergeResult
      {
        MergedControls = mergedControls,
        AppliedDecisions = Array.Empty<OverlayAppliedDecision>(),
        Conflicts = Array.Empty<OverlayConflict>()
      };
    }

    var controlsByKey = BuildControlKeyIndex(mergedControls);
    var appliedDecisions = new List<OverlayAppliedDecision>();
    var winningByKey = new Dictionary<string, OverlayAppliedDecision>(StringComparer.OrdinalIgnoreCase);
    var conflicts = new List<OverlayConflict>();

    // Process overlays in order (last wins)
    for (var overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
    {
      var overlay = overlays[overlayIndex];

      // Order overrides deterministically within overlay
      var orderedOverrides = (overlay.Overrides ?? Array.Empty<ControlOverride>())
        .Select((value, index) => new { Override = value, OriginalIndex = index })
        .Select(x => new
        {
          x.Override,
          x.OriginalIndex,
          Key = ResolveOverrideKey(x.Override)
        })
        .Where(x => x.Key != null)
        .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.OriginalIndex)
        .ToList();

      for (var overrideOrder = 0; overrideOrder < orderedOverrides.Count; overrideOrder++)
      {
        var entry = orderedOverrides[overrideOrder];
        var key = entry.Key!;

        if (!controlsByKey.TryGetValue(key, out var controlIndices))
          continue;

        var outcome = new OverlayDecisionOutcome
        {
          StatusOverride = entry.Override.StatusOverride,
          NaReason = entry.Override.NaReason,
          Notes = entry.Override.Notes
        };

        var decision = new OverlayAppliedDecision
        {
          Key = key,
          OverlayId = overlay.OverlayId,
          OverlayName = overlay.Name,
          OverlayOrder = overlayIndex,
          OverrideOrder = overrideOrder,
          Outcome = outcome
        };

        // Track conflicts when same key has different outcomes
        if (winningByKey.TryGetValue(key, out var priorDecision)
          && !OverlayDecisionOutcomeEquals(priorDecision.Outcome, outcome))
        {
          conflicts.Add(new OverlayConflict
          {
            Key = key,
            Previous = priorDecision,
            Current = decision
          });
        }

        winningByKey[key] = decision;
        appliedDecisions.Add(decision);

        // Apply override to all matching controls
        foreach (var idx in controlIndices)
          mergedControls[idx] = ApplyOverride(mergedControls[idx], entry.Override);
      }
    }

    // Deterministic ordering for conflicts
    var orderedConflicts = conflicts
      .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.Current.OverlayOrder)
      .ThenBy(c => c.Current.OverrideOrder)
      .ToList();

    return new OverlayMergeResult
    {
      MergedControls = mergedControls,
      AppliedDecisions = appliedDecisions,
      Conflicts = orderedConflicts
    };
  }

  /// <summary>
  /// Build index of control keys for efficient override lookup.
  /// </summary>
  private static Dictionary<string, List<int>> BuildControlKeyIndex(IReadOnlyList<CompiledControl> controls)
  {
    var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < controls.Count; i++)
    {
      var control = controls[i];
      TryAddIndex(index, BuildRuleKey(control.Control.ExternalIds.RuleId), i);
      TryAddIndex(index, BuildVulnKey(control.Control.ExternalIds.VulnId), i);
    }

    return index;
  }

  /// <summary>
  /// Add control index to key lookup.
  /// </summary>
  private static void TryAddIndex(Dictionary<string, List<int>> index, string? key, int controlIndex)
  {
    if (key == null)
      return;

    if (!index.TryGetValue(key, out var controls))
    {
      controls = new List<int>();
      index[key] = controls;
    }

    controls.Add(controlIndex);
  }

  /// <summary>
  /// Apply override decision to a compiled control.
  /// </summary>
  private static CompiledControl ApplyOverride(CompiledControl control, ControlOverride controlOverride)
  {
    var status = controlOverride.StatusOverride ?? control.Status;
    var comment = string.IsNullOrWhiteSpace(controlOverride.NaReason)
      ? control.Comment
      : controlOverride.NaReason;

    return new CompiledControl(control.Control, status, comment, control.NeedsReview, control.ReviewReason);
  }

  /// <summary>
  /// Create a clone of a compiled control.
  /// </summary>
  private static CompiledControl Clone(CompiledControl control)
  {
    return new CompiledControl(control.Control, control.Status, control.Comment, control.NeedsReview, control.ReviewReason);
  }

  /// <summary>
  /// Compare two overlay decision outcomes for equality.
  /// </summary>
  private static bool OverlayDecisionOutcomeEquals(OverlayDecisionOutcome left, OverlayDecisionOutcome right)
  {
    return left.StatusOverride == right.StatusOverride
      && string.Equals(left.NaReason, right.NaReason, StringComparison.OrdinalIgnoreCase)
      && string.Equals(left.Notes, right.Notes, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Resolve override key from control override.
  /// </summary>
  private static string? ResolveOverrideKey(ControlOverride controlOverride)
  {
    return BuildRuleKey(controlOverride.RuleId) ?? BuildVulnKey(controlOverride.VulnId);
  }

  /// <summary>
  /// Build rule-based key for override lookup.
  /// </summary>
  private static string? BuildRuleKey(string? ruleId)
  {
    return string.IsNullOrWhiteSpace(ruleId) ? null : "RULE:" + ruleId.Trim();
  }

  /// <summary>
  /// Build vulnerability-based key for override lookup.
  /// </summary>
  private static string? BuildVulnKey(string? vulnId)
  {
    return string.IsNullOrWhiteSpace(vulnId) ? null : "VULN:" + vulnId.Trim();
  }
}

/// <summary>
/// Result of overlay merge operation.
/// </summary>
public sealed class OverlayMergeResult
{
  public IReadOnlyList<CompiledControl> MergedControls { get; init; } = Array.Empty<CompiledControl>();
  public IReadOnlyList<OverlayAppliedDecision> AppliedDecisions { get; init; } = Array.Empty<OverlayAppliedDecision>();
  public IReadOnlyList<OverlayConflict> Conflicts { get; init; } = Array.Empty<OverlayConflict>();
}

/// <summary>
/// Represents a conflict between overlay decisions.
/// </summary>
public sealed class OverlayConflict
{
  public string Key { get; init; } = string.Empty;
  public OverlayAppliedDecision Previous { get; init; } = new();
  public OverlayAppliedDecision Current { get; init; } = new();
}

/// <summary>
/// Represents an applied overlay decision.
/// </summary>
public sealed class OverlayAppliedDecision
{
  public string Key { get; init; } = string.Empty;
  public string OverlayId { get; init; } = string.Empty;
  public string OverlayName { get; init; } = string.Empty;
  public int OverlayOrder { get; init; }
  public int OverrideOrder { get; init; }
  public OverlayDecisionOutcome Outcome { get; init; } = new();
}

/// <summary>
/// Outcome of an overlay decision.
/// </summary>
public sealed class OverlayDecisionOutcome
{
  public ControlStatus? StatusOverride { get; init; }
  public string? NaReason { get; init; }
  public string? Notes { get; init; }
}

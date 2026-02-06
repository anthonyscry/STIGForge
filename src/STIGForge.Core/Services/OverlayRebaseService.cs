using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Rebases overlays when STIG baselines change.
/// Handles control ID changes, removed controls, and modified checks.
/// </summary>
public sealed class OverlayRebaseService
{
  private readonly IOverlayRepository _overlays;
  private readonly BaselineDiffService _diffService;

  public OverlayRebaseService(IOverlayRepository overlays, BaselineDiffService diffService)
  {
    _overlays = overlays;
    _diffService = diffService;
  }

  /// <summary>
  /// Rebase an overlay from baseline pack to new pack.
  /// Returns rebase report with actions taken and confidence scores.
  /// </summary>
  public async Task<RebaseReport> RebaseOverlayAsync(
    string overlayId,
    string baselinePackId,
    string newPackId,
    CancellationToken cancellationToken = default)
  {
    var report = new RebaseReport
    {
      OverlayId = overlayId,
      BaselinePackId = baselinePackId,
      NewPackId = newPackId,
      RebasedAt = DateTimeOffset.Now
    };

    // Get diff between baseline and new pack
    var diff = await _diffService.ComparePacksAsync(baselinePackId, newPackId, cancellationToken);

    // Load overlay
    var overlay = await _overlays.GetAsync(overlayId, cancellationToken);
    if (overlay == null)
    {
      report.Success = false;
      report.ErrorMessage = $"Overlay {overlayId} not found";
      return report;
    }

    // Analyze each overlay override
    foreach (var ovr in overlay.Overrides)
    {
      var action = DetermineRebaseAction(ovr, diff);
      report.Actions.Add(action);

      // Update confidence score
      if (action.Confidence < report.OverallConfidence)
        report.OverallConfidence = action.Confidence;
    }

    // Categorize actions
    report.SafeActions = report.Actions.Count(a => a.Confidence >= 0.9);
    report.ReviewNeeded = report.Actions.Count(a => a.Confidence < 0.9 && a.Confidence >= 0.5);
    report.HighRisk = report.Actions.Count(a => a.Confidence < 0.5);

    report.Success = true;
    return report;
  }

  /// <summary>
  /// Apply rebase actions to an overlay (creates new version).
  /// </summary>
  public async Task<Overlay> ApplyRebaseAsync(
    string overlayId,
    RebaseReport report,
    CancellationToken cancellationToken = default)
  {
    var overlay = await _overlays.GetAsync(overlayId, cancellationToken);
    if (overlay == null)
      throw new InvalidOperationException($"Overlay {overlayId} not found");

    var rebasedOverlay = new Overlay
    {
      OverlayId = Guid.NewGuid().ToString(),
      Name = overlay.Name + " (Rebased)",
      UpdatedAt = DateTimeOffset.Now,
      Overrides = new List<ControlOverride>(),
      PowerStigOverrides = overlay.PowerStigOverrides.ToList()
    };

    var rebasedOverrides = new List<ControlOverride>();

    // Apply each action
    foreach (var action in report.Actions.Where(a => a.ActionType != RebaseActionType.Remove))
    {
      var existingOverride = overlay.Overrides.FirstOrDefault(o => GetOverrideKey(o) == action.OriginalControlKey);
      if (existingOverride == null) continue;

      var rebasedOverride = new ControlOverride
      {
        RuleId = existingOverride.RuleId,
        VulnId = existingOverride.VulnId,
        StatusOverride = existingOverride.StatusOverride,
        NaReason = existingOverride.NaReason,
        Notes = action.Confidence < 1.0 
          ? $"[REBASE: {action.ActionType}, Confidence: {action.Confidence:P0}] {existingOverride.Notes}" 
          : existingOverride.Notes
      };

      rebasedOverrides.Add(rebasedOverride);
    }

    // Can't use 'with' on regular class - create new instance
    var finalOverlay = new Overlay
    {
      OverlayId = rebasedOverlay.OverlayId,
      Name = rebasedOverlay.Name,
      UpdatedAt = rebasedOverlay.UpdatedAt,
      Overrides = rebasedOverrides,
      PowerStigOverrides = rebasedOverlay.PowerStigOverrides
    };

    await _overlays.SaveAsync(finalOverlay, cancellationToken);
    return finalOverlay;
  }

  /// <summary>
  /// Determine what action to take for an overlay override during rebase.
  /// </summary>
  private RebaseAction DetermineRebaseAction(ControlOverride ovr, BaselineDiff diff)
  {
    var overrideKey = GetOverrideKey(ovr);

    // Check if control was removed
    var removed = diff.RemovedControls.FirstOrDefault(c => c.ControlKey == overrideKey);
    if (removed != null)
    {
      return new RebaseAction
      {
        OriginalControlKey = overrideKey,
        ActionType = RebaseActionType.Remove,
        Reason = "Control removed from new STIG baseline",
        Confidence = 1.0, // High confidence - control is gone
        RequiresReview = true
      };
    }

    // Check if control was modified
    var modified = diff.ModifiedControls.FirstOrDefault(c => c.ControlKey == overrideKey);
    if (modified != null)
    {
      var highImpactChanges = modified.Changes.Where(c => c.Impact == FieldChangeImpact.High).ToList();
      
      if (highImpactChanges.Count > 0)
      {
        return new RebaseAction
        {
          OriginalControlKey = overrideKey,
          NewControlKey = overrideKey,
          ActionType = RebaseActionType.ReviewRequired,
          Reason = $"High-impact changes: {string.Join(", ", highImpactChanges.Select(c => c.FieldName))}",
          Confidence = 0.5, // Medium confidence - needs review
          RequiresReview = true,
          FieldChanges = highImpactChanges
        };
      }

      return new RebaseAction
      {
        OriginalControlKey = overrideKey,
        NewControlKey = overrideKey,
        ActionType = RebaseActionType.KeepWithWarning,
        Reason = $"Low-impact changes: {string.Join(", ", modified.Changes.Select(c => c.FieldName))}",
        Confidence = 0.8, // Good confidence - minor changes
        RequiresReview = false,
        FieldChanges = modified.Changes
      };
    }

    // Control unchanged - safe to keep
    return new RebaseAction
    {
      OriginalControlKey = overrideKey,
      NewControlKey = overrideKey,
      ActionType = RebaseActionType.Keep,
      Reason = "Control unchanged in new baseline",
      Confidence = 1.0, // Perfect confidence
      RequiresReview = false
    };
  }

  private static string GetOverrideKey(ControlOverride ovr)
  {
    // Prefer RuleId, fall back to VulnId
    if (!string.IsNullOrWhiteSpace(ovr.RuleId))
      return "RULE:" + ovr.RuleId;
    if (!string.IsNullOrWhiteSpace(ovr.VulnId))
      return "VULN:" + ovr.VulnId;
    return "UNKNOWN";
  }
}

/// <summary>
/// Report of rebase operation.
/// </summary>
public sealed class RebaseReport
{
  public string OverlayId { get; set; } = string.Empty;
  public string BaselinePackId { get; set; } = string.Empty;
  public string NewPackId { get; set; } = string.Empty;
  public DateTimeOffset RebasedAt { get; set; }
  public bool Success { get; set; }
  public string? ErrorMessage { get; set; }

  public List<RebaseAction> Actions { get; set; } = new();
  public double OverallConfidence { get; set; } = 1.0;

  public int SafeActions { get; set; }
  public int ReviewNeeded { get; set; }
  public int HighRisk { get; set; }
}

/// <summary>
/// Action to take for a single overlay entry during rebase.
/// </summary>
public sealed class RebaseAction
{
  public string OriginalControlKey { get; set; } = string.Empty;
  public string? NewControlKey { get; set; }
  public RebaseActionType ActionType { get; set; }
  public string Reason { get; set; } = string.Empty;
  public double Confidence { get; set; }
  public bool RequiresReview { get; set; }
  public List<ControlFieldChange> FieldChanges { get; set; } = new();
}

/// <summary>
/// Type of rebase action.
/// </summary>
public enum RebaseActionType
{
  Keep,                // Control unchanged, keep entry as-is
  KeepWithWarning,     // Control changed (low impact), keep with warning
  ReviewRequired,      // Control changed (high impact), needs manual review
  Remove,              // Control removed from baseline
  Remap                // Control ID changed, remap to new ID (future)
}

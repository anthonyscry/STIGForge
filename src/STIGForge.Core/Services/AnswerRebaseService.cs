using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Rebases manual answers when STIG baselines change.
/// Mirrors OverlayRebaseService pattern with confidence-scored actions.
/// </summary>
public sealed class AnswerRebaseService
{
  private readonly ManualAnswerService _answerService;
  private readonly BaselineDiffService _diffService;
  private readonly IAuditTrailService? _audit;

  public AnswerRebaseService(ManualAnswerService answerService, BaselineDiffService diffService, IAuditTrailService? audit = null)
  {
    _answerService = answerService;
    _diffService = diffService;
    _audit = audit;
  }

  /// <summary>
  /// Rebase answers from baseline pack to new pack.
  /// Returns rebase report with actions taken and confidence scores.
  /// </summary>
  public async Task<AnswerRebaseReport> RebaseAnswersAsync(
    string bundleRoot,
    string baselinePackId,
    string newPackId,
    CancellationToken cancellationToken = default)
  {
    var report = new AnswerRebaseReport
    {
      BundleRoot = bundleRoot,
      BaselinePackId = baselinePackId,
      NewPackId = newPackId,
      RebasedAt = DateTimeOffset.UtcNow
    };

    // Load answer file
    var answerFile = _answerService.LoadAnswerFile(bundleRoot);
    if (answerFile.Answers.Count == 0)
    {
      report.Success = true;
      return report;
    }

    // Get diff between baseline and new pack
    var diff = await _diffService.ComparePacksAsync(baselinePackId, newPackId, cancellationToken).ConfigureAwait(false);

    // Analyze each answer
    foreach (var answer in answerFile.Answers)
    {
      var action = DetermineRebaseAction(answer, diff);
      report.Actions.Add(action);

      // Update overall confidence
      if (action.Confidence < report.OverallConfidence)
        report.OverallConfidence = action.Confidence;
    }

    report.Actions = report.Actions
      .OrderBy(a => a.ControlKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(a => a.ActionType)
      .ToList();

    // Categorize actions
    report.SafeActions = report.Actions.Count(a => !a.RequiresReview && !a.IsBlockingConflict && a.Confidence >= 0.8);
    report.ReviewNeeded = report.Actions.Count(a => a.RequiresReview);
    report.HighRisk = report.Actions.Count(a => a.IsBlockingConflict || a.Confidence < 0.5);
    report.BlockingConflicts = report.Actions.Count(a => a.IsBlockingConflict);

    report.Success = true;

    if (_audit != null)
    {
      _ = Task.Run(async () =>
      {
        try
        {
          await _audit.RecordAsync(new AuditEntry
          {
            Action = "rebase-answers",
            Target = bundleRoot,
            Result = "success",
            Detail = $"Baseline={baselinePackId}, New={newPackId}, Safe={report.SafeActions}, Review={report.ReviewNeeded}, Blocking={report.BlockingConflicts}, HighRisk={report.HighRisk}",
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Timestamp = DateTimeOffset.UtcNow
          }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Trace.TraceError("Answer rebase audit logging failed: " + ex.Message);
        }
      });
    }

    return report;
  }

  /// <summary>
  /// Apply rebase actions to produce a rebased AnswerFile.
  /// Throws if blocking conflicts exist.
  /// </summary>
  public AnswerFile ApplyAnswerRebase(AnswerRebaseReport report, AnswerFile sourceAnswers)
  {
    var blockingActions = report.Actions
      .Where(a => a.IsBlockingConflict)
      .OrderBy(a => a.ControlKey, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (blockingActions.Count > 0)
    {
      var controls = string.Join(", ", blockingActions.Select(a => a.ControlKey));
      throw new InvalidOperationException($"Cannot apply answer rebase while blocking conflicts remain unresolved. Controls: {controls}");
    }

    var rebasedFile = new AnswerFile
    {
      ProfileId = sourceAnswers.ProfileId,
      PackId = sourceAnswers.PackId,
      CreatedAt = sourceAnswers.CreatedAt,
      Answers = new List<ManualAnswer>()
    };

    // Build answer lookup
    var answerLookup = new Dictionary<string, ManualAnswer>(StringComparer.OrdinalIgnoreCase);
    foreach (var answer in sourceAnswers.Answers)
    {
      var key = GetAnswerKey(answer);
      if (!answerLookup.ContainsKey(key))
        answerLookup[key] = answer;
    }

    // Apply each action
    foreach (var action in report.Actions.Where(a => !a.IsBlockingConflict && a.ActionType != AnswerRebaseActionType.Remove))
    {
      if (action.OriginalAnswer == null) continue;

      var rebasedAnswer = new ManualAnswer
      {
        RuleId = action.OriginalAnswer.RuleId,
        VulnId = action.OriginalAnswer.VulnId,
        Status = action.OriginalAnswer.Status,
        Reason = action.OriginalAnswer.Reason,
        UpdatedAt = DateTimeOffset.Now
      };

      // Add rebase metadata to Comment
      if (action.ActionType == AnswerRebaseActionType.CarryWithWarning)
      {
        var originalComment = action.OriginalAnswer.Comment ?? string.Empty;
        rebasedAnswer.Comment = $"[REBASED: {action.Confidence:P0}] {originalComment}".Trim();
      }
      else if (action.ActionType == AnswerRebaseActionType.Carry)
      {
        var originalComment = action.OriginalAnswer.Comment ?? string.Empty;
        var prefix = action.Confidence < 1.0
          ? $"[REBASED: {action.Confidence:P0}] "
          : string.Empty;
        rebasedAnswer.Comment = (prefix + originalComment).Trim();
        if (string.IsNullOrWhiteSpace(rebasedAnswer.Comment))
          rebasedAnswer.Comment = null;
      }
      else
      {
        rebasedAnswer.Comment = action.OriginalAnswer.Comment;
      }

      rebasedFile.Answers.Add(rebasedAnswer);
    }

    return rebasedFile;
  }

  /// <summary>
  /// Determine what action to take for a manual answer during rebase.
  /// </summary>
  private static AnswerRebaseAction DetermineRebaseAction(ManualAnswer answer, BaselineDiff diff)
  {
    var answerKey = GetAnswerKey(answer);

    // Check if control was removed
    var removed = diff.RemovedControls.FirstOrDefault(c =>
      string.Equals(c.ControlKey, answerKey, StringComparison.OrdinalIgnoreCase));
    if (removed != null)
    {
      return new AnswerRebaseAction
      {
        ControlKey = answerKey,
        ActionType = AnswerRebaseActionType.Remove,
        Reason = "Control removed from new STIG baseline",
        RecommendedAction = "Review this answer and decide whether to remove it or remap to a new control before applying rebase.",
        Confidence = 1.0,
        RequiresReview = true,
        IsBlockingConflict = true,
        OriginalAnswer = answer,
        FieldChanges = new List<ControlFieldChange>()
      };
    }

    // Check if control was modified
    var modified = diff.ModifiedControls.FirstOrDefault(c =>
      string.Equals(c.ControlKey, answerKey, StringComparison.OrdinalIgnoreCase));
    if (modified != null)
    {
      var highImpactChanges = modified.Changes.Where(c => c.Impact == FieldChangeImpact.High).ToList();
      var mediumImpactChanges = modified.Changes.Where(c => c.Impact == FieldChangeImpact.Medium).ToList();

      if (highImpactChanges.Count > 0)
      {
        return new AnswerRebaseAction
        {
          ControlKey = answerKey,
          ActionType = AnswerRebaseActionType.ReviewRequired,
          Reason = $"High-impact changes: {string.Join(", ", highImpactChanges.Select(c => c.FieldName))}",
          RecommendedAction = "Review the high-impact field changes and re-evaluate the manual answer before applying rebase.",
          Confidence = 0.4,
          RequiresReview = true,
          IsBlockingConflict = false,
          OriginalAnswer = answer,
          FieldChanges = modified.Changes
        };
      }

      if (mediumImpactChanges.Count > 0)
      {
        return new AnswerRebaseAction
        {
          ControlKey = answerKey,
          ActionType = AnswerRebaseActionType.CarryWithWarning,
          Reason = $"Medium-impact changes: {string.Join(", ", mediumImpactChanges.Select(c => c.FieldName))}",
          RecommendedAction = "Proceed with apply and validate this answer during post-rebase verification.",
          Confidence = 0.7,
          RequiresReview = false,
          IsBlockingConflict = false,
          OriginalAnswer = answer,
          FieldChanges = modified.Changes
        };
      }

      // Low-impact only
      return new AnswerRebaseAction
      {
        ControlKey = answerKey,
        ActionType = AnswerRebaseActionType.Carry,
        Reason = $"Low-impact changes: {string.Join(", ", modified.Changes.Select(c => c.FieldName))}",
        RecommendedAction = "No manual action required.",
        Confidence = 0.9,
        RequiresReview = false,
        IsBlockingConflict = false,
        OriginalAnswer = answer,
        FieldChanges = modified.Changes
      };
    }

    // Control unchanged — safe to carry
    return new AnswerRebaseAction
    {
      ControlKey = answerKey,
      ActionType = AnswerRebaseActionType.Carry,
      Reason = "Control unchanged in new baseline",
      RecommendedAction = "No manual action required.",
      Confidence = 1.0,
      RequiresReview = false,
      IsBlockingConflict = false,
      OriginalAnswer = answer,
      FieldChanges = new List<ControlFieldChange>()
    };
  }

  private static string GetAnswerKey(ManualAnswer answer)
  {
    if (!string.IsNullOrWhiteSpace(answer.RuleId))
      return "RULE:" + answer.RuleId;
    if (!string.IsNullOrWhiteSpace(answer.VulnId))
      return "VULN:" + answer.VulnId;
    return "UNKNOWN";
  }
}

/// <summary>
/// Report of answer rebase operation.
/// </summary>
public sealed class AnswerRebaseReport
{
  public string BundleRoot { get; set; } = string.Empty;
  public string BaselinePackId { get; set; } = string.Empty;
  public string NewPackId { get; set; } = string.Empty;
  public DateTimeOffset RebasedAt { get; set; }
  public bool Success { get; set; }
  public string? ErrorMessage { get; set; }

  public List<AnswerRebaseAction> Actions { get; set; } = new();
  public double OverallConfidence { get; set; } = 1.0;

  public int SafeActions { get; set; }
  public int ReviewNeeded { get; set; }
  public int HighRisk { get; set; }
  public int BlockingConflicts { get; set; }
  public bool HasBlockingConflicts => BlockingConflicts > 0;
}

/// <summary>
/// Action to take for a single answer during rebase.
/// </summary>
public sealed class AnswerRebaseAction
{
  public string ControlKey { get; set; } = string.Empty;
  public AnswerRebaseActionType ActionType { get; set; }
  public string Reason { get; set; } = string.Empty;
  public string RecommendedAction { get; set; } = string.Empty;
  public double Confidence { get; set; }
  public bool RequiresReview { get; set; }
  public bool IsBlockingConflict { get; set; }
  public ManualAnswer? OriginalAnswer { get; set; }
  public List<ControlFieldChange> FieldChanges { get; set; } = new();
}

/// <summary>
/// Type of answer rebase action.
/// </summary>
public enum AnswerRebaseActionType
{
  Carry,              // Control unchanged, carry answer as-is (confidence >= 0.8)
  CarryWithWarning,   // Control changed (medium impact), carry with warning (0.5-0.8)
  ReviewRequired,     // Control changed (high impact), needs manual review (< 0.5)
  Remove,             // Control removed from baseline
  Remap               // Control ID changed, remap to new ID (future)
}

using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ClassificationScopeService : IClassificationScopeService
{
  public CompiledControls Compile(Profile profile, IReadOnlyList<ControlRecord> controls)
  {
    var compiled = new List<CompiledControl>(controls.Count);
    var review = new List<CompiledControl>();

    foreach (var c in controls)
    {
      var result = Evaluate(profile, c);
      var cc = new CompiledControl(c, result.Status, result.Comment, result.NeedsReview, result.ReviewReason);
      compiled.Add(cc);
      if (cc.NeedsReview) review.Add(cc);
    }

    return new CompiledControls(compiled, review);
  }

  private static (ControlStatus Status, string? Comment, bool NeedsReview, string? ReviewReason) Evaluate(Profile profile, ControlRecord c)
  {
    var status = ControlStatus.Open;
    string? comment = null;

    if (!profile.NaPolicy.AutoNaOutOfScope)
      return (status, comment, false, null);

    if (profile.ClassificationMode == ClassificationMode.Classified)
    {
      if (c.Applicability.ClassificationScope == ScopeTag.UnclassifiedOnly)
      {
        if (MeetsThreshold(c.Applicability.Confidence, profile.NaPolicy.ConfidenceThreshold))
        {
          status = ControlStatus.NotApplicable;
          comment = profile.NaPolicy.DefaultNaCommentTemplate;
          return (status, comment, false, null);
        }

        return (status, comment, true, "Low confidence scope match: UnclassifiedOnly");
      }

      if (c.Applicability.ClassificationScope == ScopeTag.Unknown)
        return (status, comment, true, "Unknown classification scope");
    }

    return (status, comment, false, null);
  }

  private static bool MeetsThreshold(Confidence actual, Confidence threshold)
  {
    int a = actual == Confidence.High ? 3 : actual == Confidence.Medium ? 2 : 1;
    int t = threshold == Confidence.High ? 3 : threshold == Confidence.Medium ? 2 : 1;
    return a >= t;
  }

  /// <summary>
  /// Filters controls based on classification scope.
  /// Returns only controls that match the target classification mode.
  /// </summary>
  /// <param name="controls">Controls to filter</param>
  /// <param name="mode">Target classification mode</param>
  /// <returns>Filtered list of controls</returns>
  public static IEnumerable<ControlRecord> FilterControls(
    IEnumerable<ControlRecord> controls,
    ClassificationMode mode)
  {
    return mode switch
    {
      ClassificationMode.Classified =>
        // Include only controls that are NOT UnclassifiedOnly
        controls.Where(c => c.Applicability.ClassificationScope != ScopeTag.UnclassifiedOnly),

      ClassificationMode.Unclassified =>
        // Include only controls that ARE UnclassifiedOnly
        controls.Where(c => c.Applicability.ClassificationScope == ScopeTag.UnclassifiedOnly),

      ClassificationMode.Mixed =>
        // Include all controls in mixed mode
        controls,

      _ => controls
    };
  }
}

using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ReleaseAgeGate
{
  private readonly IClock _clock;

  public ReleaseAgeGate(IClock clock)
  {
    _clock = clock;
  }

  public bool ShouldAutoApply(Profile profile, ContentPack pack)
  {
    if (pack.ReleaseDate == null) return false;
    if (profile.AutomationPolicy.NewRuleGraceDays <= 0) return true;
    
    var cutoff = pack.ReleaseDate.Value.AddDays(profile.AutomationPolicy.NewRuleGraceDays);
    return _clock.Now >= cutoff;
  }

  /// <summary>
  /// Filters controls based on release age (maturity) of their revisions.
  /// Excludes controls with recent benchmark dates (within grace period).
  /// </summary>
  /// <param name="controls">Controls to filter</param>
  /// <param name="gracePeriodDays">Grace period in days</param>
  /// <returns>Filtered list of mature controls</returns>
  public IEnumerable<ControlRecord> FilterControls(
    IEnumerable<ControlRecord> controls,
    int gracePeriodDays)
  {
    if (gracePeriodDays <= 0)
    {
      // No grace period - include all controls
      return controls;
    }

    var cutoff = _clock.Now.AddDays(-gracePeriodDays);

    return controls.Where(c =>
      c.Revision == null ||        // No revision info - include
      c.Revision.BenchmarkDate == null ||  // No benchmark date - include
      c.Revision.BenchmarkDate < cutoff    // Mature revision - include
    ).ToList();
  }
}

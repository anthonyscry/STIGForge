using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class OverlayConflict
{
  public string ControlKey { get; init; } = string.Empty;
  public string WinningOverlayId { get; init; } = string.Empty;
  public string OverriddenOverlayId { get; init; } = string.Empty;
  public string WinningValue { get; init; } = string.Empty;
  public string OverriddenValue { get; init; } = string.Empty;
  public string Reason { get; init; } = string.Empty;
  public bool IsBlockingConflict { get; init; }
}

public sealed class OverlayConflictReport
{
  public IReadOnlyList<OverlayConflict> Conflicts { get; init; } = Array.Empty<OverlayConflict>();
  public bool HasBlockingConflicts => BlockingConflictCount > 0;
  public int BlockingConflictCount => Conflicts.Count(c => c.IsBlockingConflict);
}

public sealed class OverlayConflictDetector
{
  /// <summary>
  /// Detect conflicts across overlays ordered by positional precedence (index 0 = lowest, last = highest / winner).
  /// </summary>
  public OverlayConflictReport DetectConflicts(IReadOnlyList<Overlay> overlays)
  {
    if (overlays is null || overlays.Count < 2)
      return new OverlayConflictReport();

    // Build map: controlKey -> list of (overlayIndex, overlayId, override)
    var overrideMap = new Dictionary<string, List<(int Index, string OverlayId, ControlOverride Override)>>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < overlays.Count; i++)
    {
      var overlay = overlays[i];
      foreach (var ovr in overlay.Overrides)
      {
        var key = ControlKey(ovr);
        if (string.IsNullOrWhiteSpace(key)) continue;

        if (!overrideMap.TryGetValue(key, out var list))
        {
          list = new List<(int, string, ControlOverride)>();
          overrideMap[key] = list;
        }

        list.Add((i, overlay.OverlayId, ovr));
      }
    }

    var conflicts = new List<OverlayConflict>();

    foreach (var kvp in overrideMap)
    {
      if (kvp.Value.Count < 2) continue;

      // The entry with the highest index wins (last-wins positional precedence)
      var sorted = kvp.Value.OrderBy(x => x.Index).ToList();
      var winner = sorted[^1];

      for (int i = 0; i < sorted.Count - 1; i++)
      {
        var loser = sorted[i];
        var isBlocking = IsBlockingConflict(winner.Override, loser.Override);
        var reason = isBlocking
          ? "Blocking: different StatusOverride values"
          : "Non-blocking: same StatusOverride, different details";

        conflicts.Add(new OverlayConflict
        {
          ControlKey = kvp.Key,
          WinningOverlayId = winner.OverlayId,
          OverriddenOverlayId = loser.OverlayId,
          WinningValue = FormatValue(winner.Override),
          OverriddenValue = FormatValue(loser.Override),
          Reason = reason,
          IsBlockingConflict = isBlocking
        });
      }
    }

    // Deterministic ordering: ControlKey then OverriddenOverlayId
    conflicts = conflicts
      .OrderBy(c => c.ControlKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.OverriddenOverlayId, StringComparer.OrdinalIgnoreCase)
      .ToList();

    return new OverlayConflictReport { Conflicts = conflicts };
  }

  private static string ControlKey(ControlOverride ovr)
  {
    // Prefer RuleId, fallback to VulnId
    if (!string.IsNullOrWhiteSpace(ovr.RuleId)) return ovr.RuleId;
    if (!string.IsNullOrWhiteSpace(ovr.VulnId)) return ovr.VulnId;
    return string.Empty;
  }

  private static bool IsBlockingConflict(ControlOverride winner, ControlOverride loser)
  {
    // Blocking when StatusOverride values differ (genuine disagreement on compliance status)
    return winner.StatusOverride != loser.StatusOverride;
  }

  private static string FormatValue(ControlOverride ovr)
  {
    var status = ovr.StatusOverride?.ToString() ?? "Open";
    var reason = ovr.NaReason ?? "";
    return $"Status={status}, NaReason={reason}";
  }
}

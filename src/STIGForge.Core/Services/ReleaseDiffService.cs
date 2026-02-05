using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ReleaseDiffService
{
  public ReleaseDiff Diff(string fromPackId, string toPackId, IReadOnlyList<ControlRecord> fromControls, IReadOnlyList<ControlRecord> toControls)
  {
    var fromMap = IndexByKey(fromControls);
    var toMap = IndexByKey(toControls);
    var keys = new HashSet<string>(fromMap.Keys, StringComparer.OrdinalIgnoreCase);
    keys.UnionWith(toMap.Keys);

    var items = new List<ControlDiff>(keys.Count);
    foreach (var key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
      var hasFrom = fromMap.TryGetValue(key, out var from);
      var hasTo = toMap.TryGetValue(key, out var to);

      if (hasFrom && !hasTo)
      {
        items.Add(ToDiff(key, from!, null, DiffKind.Removed));
        continue;
      }
      if (!hasFrom && hasTo)
      {
        items.Add(ToDiff(key, null, to!, DiffKind.Added));
        continue;
      }

      var fromHash = ControlFingerprint.Compute(from!);
      var toHash = ControlFingerprint.Compute(to!);
      var kind = fromHash == toHash ? DiffKind.Unchanged : DiffKind.Changed;
      items.Add(ToDiff(key, from, to, kind, fromHash, toHash));
    }

    return new ReleaseDiff { FromPackId = fromPackId, ToPackId = toPackId, Items = items };
  }

  private static Dictionary<string, ControlRecord> IndexByKey(IEnumerable<ControlRecord> controls)
  {
    var map = new Dictionary<string, ControlRecord>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in controls)
    {
      var key = GetKey(c);
      if (!map.ContainsKey(key)) map[key] = c;
    }
    return map;
  }

  private static string GetKey(ControlRecord c)
  {
    if (!string.IsNullOrWhiteSpace(c.ExternalIds.RuleId)) return "RULE:" + c.ExternalIds.RuleId!.Trim();
    if (!string.IsNullOrWhiteSpace(c.ExternalIds.VulnId)) return "VULN:" + c.ExternalIds.VulnId!.Trim();
    return "TITLE:" + (c.Title ?? string.Empty).Trim();
  }

  private static ControlDiff ToDiff(string key, ControlRecord? from, ControlRecord? to, DiffKind kind, string? fromHash = null, string? toHash = null)
  {
    var source = to ?? from!;
    var changed = new List<string>();
    if (from != null && to != null && kind == DiffKind.Changed)
    {
      if (!string.Equals(from.Title, to.Title, StringComparison.Ordinal)) changed.Add("Title");
      if (!string.Equals(from.Severity, to.Severity, StringComparison.Ordinal)) changed.Add("Severity");
      if (!string.Equals(from.Discussion, to.Discussion, StringComparison.Ordinal)) changed.Add("Discussion");
      if (!string.Equals(from.CheckText, to.CheckText, StringComparison.Ordinal)) changed.Add("CheckText");
      if (!string.Equals(from.FixText, to.FixText, StringComparison.Ordinal)) changed.Add("FixText");
      if (from.IsManual != to.IsManual) changed.Add("IsManual");
    }

    return new ControlDiff
    {
      Key = key,
      RuleId = source.ExternalIds.RuleId,
      VulnId = source.ExternalIds.VulnId,
      Title = source.Title,
      Kind = kind,
      IsManual = source.IsManual,
      ManualChanged = changed.Contains("IsManual") || source.IsManual,
      ChangedFields = changed,
      FromHash = fromHash,
      ToHash = toHash
    };
  }
}

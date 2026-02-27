using System;
using System.Collections.Generic;
using System.Linq;

namespace STIGForge.Verify;

public static class SnapshotMergeService
{
  private static readonly string[] ToolPrecedence = { "Manual", "SCC", "Evaluate-STIG" };

  public static IReadOnlyList<ControlResult> Merge(IEnumerable<ControlResult> results, string assetId)
  {
    if (results is null)
      return Array.Empty<ControlResult>();

    var normalizedResults = results
      .Select(result => new ResultWithAsset(result, NormalizeAssetId(result, assetId)))
      .ToArray();

    var benchmarkGroups = normalizedResults
      .GroupBy(r => (
        AssetId: r.AssetId ?? string.Empty,
        BenchmarkId: r.Result.BenchmarkId ?? string.Empty))
      .OrderBy(g => g.Key.AssetId, StringComparer.Ordinal)
      .ThenBy(g => g.Key.BenchmarkId, StringComparer.Ordinal);

    var merged = new List<ControlResult>();

    foreach (var benchmarkGroup in benchmarkGroups)
    {
      var controlGroups = BuildControlGroups(benchmarkGroup.Select(r => r.Result));
      var assetKey = string.IsNullOrWhiteSpace(benchmarkGroup.Key.AssetId) ? null : benchmarkGroup.Key.AssetId;
      var benchmarkKey = string.IsNullOrWhiteSpace(benchmarkGroup.Key.BenchmarkId) ? null : benchmarkGroup.Key.BenchmarkId;

      foreach (var controlGroup in controlGroups)
      {
        merged.Add(MergeControlGroup(assetKey, benchmarkKey, controlGroup.Results));
      }
    }

    return merged
      .OrderBy(r => r.AssetId, StringComparer.Ordinal)
      .ThenBy(r => r.BenchmarkId, StringComparer.Ordinal)
      .ThenBy(r => BuildControlKey(r), StringComparer.Ordinal)
      .ToArray();
  }

  private static IReadOnlyList<ControlGroup> BuildControlGroups(IEnumerable<ControlResult> orderedResults)
  {
    var groups = new List<ControlGroup>();
    foreach (var result in orderedResults
      .OrderBy(r => GetToolRank(r.Tool))
      .ThenBy(r => r.Tool, StringComparer.Ordinal)
      .ThenByDescending(r => r.VerifiedAt ?? DateTimeOffset.MinValue)
      .ThenBy(r => r.VulnId, StringComparer.Ordinal)
      .ThenBy(r => r.RuleId, StringComparer.Ordinal)
      .ThenBy(r => r.Title, StringComparer.Ordinal)
      .ThenBy(r => r.SourceFile, StringComparer.Ordinal))
    {
      var match = groups.FirstOrDefault(g => g.Matches(result));
      if (match is null)
      {
        var group = new ControlGroup(result);
        groups.Add(group);
      }
      else
      {
        match.Add(result);
      }
    }

    return groups;
  }

  private static ControlResult MergeControlGroup(string? assetId, string? benchmarkId, IReadOnlyList<ControlResult> candidates)
  {
    var ordered = candidates
      .OrderBy(r => GetToolRank(r.Tool))
      .ThenBy(r => r.Tool, StringComparer.Ordinal)
      .ThenByDescending(r => r.VerifiedAt ?? DateTimeOffset.MinValue)
      .ThenBy(r => r.VulnId, StringComparer.Ordinal)
      .ThenBy(r => r.RuleId, StringComparer.Ordinal)
      .ThenBy(r => r.Title, StringComparer.Ordinal)
      .ThenBy(r => r.SourceFile, StringComparer.Ordinal)
      .ToArray();

    var winner = ordered.First();
    var evidence = ordered
      .Where(r => !string.IsNullOrWhiteSpace(r.FindingDetails))
      .Select(r => FormatProvenance(r.Tool, r.FindingDetails!));
    var comments = ordered
      .Where(r => !string.IsNullOrWhiteSpace(r.Comments))
      .Select(r => FormatProvenance(r.Tool, r.Comments!));

    return new ControlResult
    {
      AssetId = assetId,
      BenchmarkId = benchmarkId,
      VulnId = GetBestIdentifier(ordered, r => r.VulnId),
      RuleId = GetBestIdentifier(ordered, r => r.RuleId),
      Title = GetBestIdentifier(ordered, r => r.Title),
      Severity = winner.Severity,
      Status = winner.Status,
      Tool = winner.Tool,
      SourceFile = winner.SourceFile,
      VerifiedAt = winner.VerifiedAt,
      FindingDetails = string.Join("\n", evidence),
      Comments = string.Join("\n", comments)
    };
  }

  private static string BuildControlKey(ControlResult result)
  {
    return result.VulnId ?? result.RuleId ?? result.Title ?? string.Empty;
  }

  private static string? GetBestIdentifier(IEnumerable<ControlResult> ordered, Func<ControlResult, string?> selector)
  {
    foreach (var candidate in ordered)
    {
      var value = selector(candidate);
      if (!string.IsNullOrWhiteSpace(value))
        return value;
    }

    return null;
  }

  private static string FormatProvenance(string tool, string text)
  {
    var cleanTool = string.IsNullOrWhiteSpace(tool) ? "Unknown" : tool.Trim();
    return $"[{cleanTool}] {text.Trim()}";
  }

  private static string? NormalizeAssetId(ControlResult result, string assetId)
  {
    if (!string.IsNullOrWhiteSpace(result.AssetId))
      return result.AssetId;
    if (!string.IsNullOrWhiteSpace(assetId))
      return assetId;

    return null;
  }

  private static int GetToolRank(string tool)
  {
    if (string.IsNullOrWhiteSpace(tool))
      return ToolPrecedence.Length;

    for (var i = 0; i < ToolPrecedence.Length; i++)
    {
      if (tool.StartsWith(ToolPrecedence[i], StringComparison.OrdinalIgnoreCase))
        return i;
    }

    return ToolPrecedence.Length;
  }

  private sealed record ResultWithAsset(ControlResult Result, string? AssetId);

  private sealed class ControlGroup
  {
    public ControlGroup(ControlResult first)
    {
      VulnId = first.VulnId;
      RuleId = first.RuleId;
      Title = first.Title;
      Results.Add(first);
    }

    public string? VulnId { get; private set; }
    public string? RuleId { get; private set; }
    public string? Title { get; private set; }
    public List<ControlResult> Results { get; } = new();

    public bool Matches(ControlResult candidate)
    {
      if (IsSameIdentifier(VulnId, candidate.VulnId))
        return true;
      if (IsSameIdentifier(RuleId, candidate.RuleId))
        return true;
      if (IsSameIdentifier(Title, candidate.Title) && !HasConflictingIdentifier(candidate))
        return true;

      return false;
    }

    public void Add(ControlResult next)
    {
      if (string.IsNullOrWhiteSpace(VulnId))
        VulnId = next.VulnId;
      if (string.IsNullOrWhiteSpace(RuleId))
        RuleId = next.RuleId;
      if (string.IsNullOrWhiteSpace(Title))
        Title = next.Title;

      Results.Add(next);
    }

    private static bool IsSameIdentifier(string? left, string? right)
    {
      return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.Ordinal);
    }

    private bool HasConflictingIdentifier(ControlResult candidate)
    {
      if (!string.IsNullOrWhiteSpace(VulnId) && !string.IsNullOrWhiteSpace(candidate.VulnId) && !string.Equals(VulnId, candidate.VulnId, StringComparison.Ordinal))
        return true;
      if (!string.IsNullOrWhiteSpace(RuleId) && !string.IsNullOrWhiteSpace(candidate.RuleId) && !string.Equals(RuleId, candidate.RuleId, StringComparison.Ordinal))
        return true;

      return false;
    }
  }
}

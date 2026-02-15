using System.Text.RegularExpressions;

namespace STIGForge.Core.Services;

public sealed class CanonicalScapSelectionInput
{
  public string StigPackId { get; set; } = string.Empty;
  public string StigName { get; set; } = string.Empty;
  public DateTimeOffset StigImportedAt { get; set; }
  public IReadOnlyCollection<string> StigBenchmarkIds { get; set; } = Array.Empty<string>();
  public IReadOnlyList<CanonicalScapCandidate> Candidates { get; set; } = Array.Empty<CanonicalScapCandidate>();
}

public sealed class CanonicalScapCandidate
{
  public string PackId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string SourceLabel { get; set; } = string.Empty;
  public DateTimeOffset ImportedAt { get; set; }
  public DateTimeOffset? ReleaseDate { get; set; }
  public IReadOnlyCollection<string> BenchmarkIds { get; set; } = Array.Empty<string>();
}

public sealed class CanonicalScapSelectionResult
{
  public CanonicalScapCandidate? Winner { get; set; }
  public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
  public bool HasConflict { get; set; }
}

public sealed class CanonicalScapSelector
{
  private static readonly Regex DisaVersionRegex = new(@"V\s*(\d+)\s*R\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public CanonicalScapSelectionResult Select(CanonicalScapSelectionInput input)
  {
    if (input == null)
      throw new ArgumentNullException(nameof(input));

    var candidates = (input.Candidates ?? Array.Empty<CanonicalScapCandidate>())
      .Where(c => c != null)
      .ToList();

    var reasons = new List<string>();
    if (candidates.Count == 0)
    {
      return new CanonicalScapSelectionResult
      {
        Winner = null,
        HasConflict = false,
        Reasons = new[] { "No SCAP candidate found for this STIG." }
      };
    }

    var hasConflict = candidates.Count > 1;
    var remaining = candidates.ToList();

    var stigVersionRank = ParseVersionRank(input.StigName);
    if (stigVersionRank > 0)
    {
      var versionAligned = remaining
        .Where(c => ParseVersionRank(c.Name) == stigVersionRank)
        .ToList();

      if (versionAligned.Count > 0)
      {
        if (versionAligned.Count != remaining.Count)
          reasons.Add("Version-aligned SCAP candidates preferred for selected STIG version.");
        remaining = versionAligned;
      }
    }

    if (remaining.Count > 1)
    {
      var normalizedStigIds = NormalizeIds(input.StigBenchmarkIds);
      if (normalizedStigIds.Count > 0)
      {
        var scored = remaining
          .Select(c => new
          {
            Candidate = c,
            Score = CountOverlap(normalizedStigIds, NormalizeIds(c.BenchmarkIds))
          })
          .ToList();

        var bestScore = scored.Max(x => x.Score);
        if (bestScore > 0)
        {
          var overlapBest = scored
            .Where(x => x.Score == bestScore)
            .Select(x => x.Candidate)
            .ToList();

          if (overlapBest.Count != remaining.Count)
            reasons.Add("Benchmark ID overlap used to reduce SCAP candidates.");

          remaining = overlapBest;
        }
      }
    }

    if (remaining.Count > 1)
    {
      var niwcEnhanced = remaining
        .Where(IsNiwcEnhanced)
        .ToList();
      if (niwcEnhanced.Count > 0)
      {
        reasons.Add("NIWC Enhanced (Consolidated Bundle) candidate preferred for tied version candidates.");
        remaining = niwcEnhanced;
      }
    }

    if (remaining.Count > 1)
    {
      remaining = remaining
        .OrderByDescending(c => c.ReleaseDate ?? DateTimeOffset.MinValue)
        .ThenByDescending(c => c.ImportedAt)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.PackId, StringComparer.OrdinalIgnoreCase)
        .ToList();

      reasons.Add("Deterministic fallback applied (release/import date then lexical ordering).");
    }

    return new CanonicalScapSelectionResult
    {
      Winner = remaining[0],
      HasConflict = hasConflict,
      Reasons = reasons
    };
  }

  private static bool IsNiwcEnhanced(CanonicalScapCandidate candidate)
  {
    var text = ((candidate.Name ?? string.Empty) + " " + (candidate.SourceLabel ?? string.Empty)).ToLowerInvariant();
    var hasNiwc = text.IndexOf("niwc", StringComparison.Ordinal) >= 0;
    var hasEnhancedOrBundle = text.IndexOf("enhanced", StringComparison.Ordinal) >= 0
      || text.IndexOf("consolidated", StringComparison.Ordinal) >= 0
      || text.IndexOf("bundle", StringComparison.Ordinal) >= 0;
    var isScap = text.IndexOf("scap", StringComparison.Ordinal) >= 0;

    return hasNiwc && hasEnhancedOrBundle && isScap;
  }

  private static long ParseVersionRank(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return 0;

    var match = DisaVersionRegex.Match(text);
    if (!match.Success)
      return 0;

    if (!int.TryParse(match.Groups[1].Value, out var version))
      return 0;
    if (!int.TryParse(match.Groups[2].Value, out var release))
      return 0;

    return (version * 1000L) + release;
  }

  private static HashSet<string> NormalizeIds(IEnumerable<string>? ids)
  {
    var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (ids == null)
      return normalized;

    foreach (var id in ids)
    {
      if (string.IsNullOrWhiteSpace(id))
        continue;

      var source = id.Trim().ToLowerInvariant();
      var sb = new System.Text.StringBuilder(source.Length);
      foreach (var ch in source)
      {
        if (char.IsLetterOrDigit(ch))
          sb.Append(ch);
      }

      if (sb.Length > 0)
        normalized.Add(sb.ToString());
    }

    return normalized;
  }

  private static int CountOverlap(ISet<string> left, ISet<string> right)
  {
    var count = 0;
    foreach (var item in left)
    {
      if (right.Contains(item))
        count++;
    }

    return count;
  }
}

using System.Text.RegularExpressions;

namespace STIGForge.Content.Import;

public sealed class ImportDedupService
{
  private static readonly Regex DisaVersionRegex = new(@"V\s*(\d+)\s*R\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public ImportDedupOutcome Resolve(IReadOnlyList<ImportInboxCandidate> candidates)
  {
    if (candidates.Count == 0)
      return new ImportDedupOutcome();

    var decisions = new List<string>();

    var byLogicalContent = candidates
      .GroupBy(BuildLogicalKey, StringComparer.OrdinalIgnoreCase)
      .Select(group => SelectPreferred(group.ToList(), forStig: group.Any(x => x.ArtifactKind == ImportArtifactKind.Stig), decisions))
      .ToList();

    var winners = byLogicalContent.Select(x => x.Winner).ToList();
    var suppressed = byLogicalContent.SelectMany(x => x.Suppressed).ToList();

    return new ImportDedupOutcome
    {
      Winners = winners
        .GroupBy(BuildPhysicalKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.ArtifactKind.ToString(), StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.ContentKey, StringComparer.OrdinalIgnoreCase)
        .ToList(),
      Suppressed = suppressed
        .GroupBy(BuildPhysicalKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.ArtifactKind.ToString(), StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.ContentKey, StringComparer.OrdinalIgnoreCase)
        .ToList(),
      Decisions = decisions
    };
  }

  private static string BuildLogicalKey(ImportInboxCandidate candidate)
  {
    var contentKey = candidate.ContentKey ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(contentKey))
      return candidate.ArtifactKind + "|" + contentKey.Trim().ToLowerInvariant();

    if (candidate.ArtifactKind == ImportArtifactKind.Tool)
      return "tool|" + candidate.ToolKind;

    var sha = candidate.Sha256 ?? string.Empty;
    return candidate.ArtifactKind + "|" + sha.Trim().ToLowerInvariant();
  }

  private static string BuildPhysicalKey(ImportInboxCandidate candidate)
  {
    return (candidate.ZipPath ?? string.Empty).Trim().ToLowerInvariant()
      + "|" + candidate.ArtifactKind
      + "|" + candidate.ToolKind
      + "|" + (candidate.ContentKey ?? string.Empty).Trim().ToLowerInvariant();
  }

  private static (ImportInboxCandidate Winner, List<ImportInboxCandidate> Suppressed) SelectPreferred(List<ImportInboxCandidate> group, bool forStig, ICollection<string> decisions)
  {
    if (group.Count == 0)
      throw new InvalidOperationException("Group must contain at least one candidate.");

    if (group.All(c => c.ArtifactKind == ImportArtifactKind.Scap)
      && HasDifferentHashes(group)
      && TrySelectNiwcEnhanced(group, out var niwcWinner))
    {
      var suppressedByNiwc = group
        .Where(c => !ReferenceEquals(c, niwcWinner))
        .ToList();

      decisions.Add("SCAP conflict: same logical content, different hash -> selected NIWC Enhanced (Consolidated Bundle): "
        + niwcWinner.FileName + " [" + niwcWinner.ContentKey + "]");

      return (niwcWinner, suppressedByNiwc);
    }

    var ordered = group
      .OrderByDescending(c => c.Confidence)
      .ThenByDescending(c => GetDateRank(c))
      .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (forStig)
    {
      ordered = group
        .OrderByDescending(c => GetVersionRank(c))
        .ThenByDescending(c => GetDateRank(c))
        .ThenByDescending(c => c.Confidence)
        .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    var winner = ordered[0];
    var suppressed = ordered.Skip(1).ToList();

    if (group.All(c => c.ArtifactKind == ImportArtifactKind.Scap)
      && HasDifferentHashes(group)
      && suppressed.Count > 0)
    {
      decisions.Add("SCAP conflict: same logical content, different hash -> NIWC Enhanced not found; selected deterministic fallback: "
        + winner.FileName + " [" + winner.ContentKey + "]");
    }

    return (winner, suppressed);
  }

  private static bool HasDifferentHashes(IReadOnlyList<ImportInboxCandidate> group)
  {
    var hashes = group
      .Select(c => (c.Sha256 ?? string.Empty).Trim())
      .Where(h => h.Length > 0)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    return hashes.Count > 1;
  }

  private static bool TrySelectNiwcEnhanced(IReadOnlyList<ImportInboxCandidate> group, out ImportInboxCandidate winner)
  {
    var niwcCandidates = group
      .Where(c => c.ImportedFrom == ImportProvenance.ConsolidatedBundle && c.IsNiwcEnhanced)
      .OrderByDescending(GetDateRank)
      .ThenByDescending(c => c.Confidence)
      .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (niwcCandidates.Count == 0)
    {
      winner = null!;
      return false;
    }

    winner = niwcCandidates[0];
    return true;
  }

  private static long GetDateRank(ImportInboxCandidate candidate)
  {
    return candidate.BenchmarkDate?.UtcTicks ?? 0;
  }

  private static long GetVersionRank(ImportInboxCandidate candidate)
  {
    if (string.IsNullOrWhiteSpace(candidate.VersionTag))
      return 0;

    var match = DisaVersionRegex.Match(candidate.VersionTag);
    if (!match.Success)
      return 0;

    if (!int.TryParse(match.Groups[1].Value, out var version))
      return 0;
    if (!int.TryParse(match.Groups[2].Value, out var release))
      return 0;

    return (version * 1000L) + release;
  }
}

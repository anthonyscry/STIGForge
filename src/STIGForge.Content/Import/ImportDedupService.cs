using System.Text.RegularExpressions;

namespace STIGForge.Content.Import;

public sealed class ImportDedupService
{
  private static readonly Regex DisaVersionRegex = new(@"V\s*(\d+)\s*R\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public ImportDedupOutcome Resolve(IReadOnlyList<ImportInboxCandidate> candidates)
  {
    if (candidates.Count == 0)
      return new ImportDedupOutcome();

    var bySha = candidates
      .GroupBy(c => c.Sha256, StringComparer.OrdinalIgnoreCase)
      .Select(group => SelectPreferred(group.ToList(), forStig: group.Any(x => x.ArtifactKind == ImportArtifactKind.Stig)))
      .ToList();

    var shaWinners = bySha.Select(x => x.Winner).ToList();
    var shaSuppressed = bySha.SelectMany(x => x.Suppressed).ToList();

    var finalWinners = new List<ImportInboxCandidate>();
    var finalSuppressed = new List<ImportInboxCandidate>(shaSuppressed);

    var withKey = shaWinners
      .Where(c => !string.IsNullOrWhiteSpace(c.ContentKey))
      .GroupBy(c => c.ContentKey, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var withoutKey = shaWinners
      .Where(c => string.IsNullOrWhiteSpace(c.ContentKey))
      .ToList();

    foreach (var group in withKey)
    {
      var selection = SelectPreferred(group.ToList(), forStig: group.Any(x => x.ArtifactKind == ImportArtifactKind.Stig));
      finalWinners.Add(selection.Winner);
      finalSuppressed.AddRange(selection.Suppressed);
    }

    finalWinners.AddRange(withoutKey);

    return new ImportDedupOutcome
    {
      Winners = finalWinners
        .DistinctBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .ToList(),
      Suppressed = finalSuppressed
        .DistinctBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .ToList()
    };
  }

  private static (ImportInboxCandidate Winner, List<ImportInboxCandidate> Suppressed) SelectPreferred(List<ImportInboxCandidate> group, bool forStig)
  {
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
    return (winner, suppressed);
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

using STIGForge.Core;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class CklMergeService
{
  public Task<CklMergeResult> MergeAsync(
    CklChecklist importedChecklist,
    IReadOnlyList<ControlResult> existingResults,
    CklConflictResolutionStrategy strategy,
    CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(importedChecklist);
    ArgumentNullException.ThrowIfNull(existingResults);
    ct.ThrowIfCancellationRequested();

    var existingByKey = existingResults
      .Where(result => !string.IsNullOrWhiteSpace(BuildControlKey(result.VulnId, result.RuleId)))
      .GroupBy(result => BuildControlKey(result.VulnId, result.RuleId), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    var mergedFindings = new List<CklFinding>(importedChecklist.Findings.Count + existingResults.Count);
    var conflicts = new List<CklConflict>();
    var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var finding in importedChecklist.Findings)
    {
      ct.ThrowIfCancellationRequested();

      var key = BuildControlKey(finding.VulnId, finding.RuleId);
      _ = processedKeys.Add(key);

      ControlResult? existing;
      if (!existingByKey.TryGetValue(key, out existing))
      {
        mergedFindings.Add(CloneFinding(finding));
        continue;
      }

      var cklStatus = NormalizeCklStatus(finding.Status);
      var stigForgeStatus = NormalizeStigForgeStatus(existing.Status);
      var hasConflict = !string.Equals(cklStatus, stigForgeStatus, StringComparison.Ordinal);

      var mergedFinding = CloneFinding(finding);
      mergedFinding.Comments = PreferText(mergedFinding.Comments, existing.Comments);
      mergedFinding.FindingDetails = PreferText(mergedFinding.FindingDetails, existing.FindingDetails);

      if (hasConflict)
      {
        var resolvedStatus = ResolveStatus(strategy, finding.Status, existing, importedChecklist.ImportedAt);
        var conflict = new CklConflict
        {
          VulnId = finding.VulnId,
          RuleId = finding.RuleId,
          CklStatus = finding.Status,
          StigForgeStatus = MapToCklStatus(existing.Status),
          ResolvedStatus = resolvedStatus,
          RequiresManualResolution = strategy == CklConflictResolutionStrategy.Manual,
          CklImportedAt = importedChecklist.ImportedAt,
          StigForgeEvaluatedAt = ResolveStigForgeTimestamp(existing),
          StrategyApplied = strategy
        };

        conflicts.Add(conflict);
        mergedFinding.Status = resolvedStatus;

        if (strategy == CklConflictResolutionStrategy.StigForgeWins)
        {
          mergedFinding.Comments = PreferText(existing.Comments, mergedFinding.Comments);
          mergedFinding.FindingDetails = PreferText(existing.FindingDetails, mergedFinding.FindingDetails);
        }
      }
      else
      {
        mergedFinding.Status = MapToCklStatus(existing.Status);
      }

      mergedFindings.Add(mergedFinding);
    }

    foreach (var existing in existingResults)
    {
      ct.ThrowIfCancellationRequested();

      var key = BuildControlKey(existing.VulnId, existing.RuleId);
      if (processedKeys.Contains(key))
        continue;

      mergedFindings.Add(new CklFinding
      {
        VulnId = existing.VulnId ?? string.Empty,
        RuleId = existing.RuleId,
        RuleTitle = existing.Title ?? existing.RuleId,
        Severity = existing.Severity ?? "unknown",
        Status = MapToCklStatus(existing.Status),
        Comments = existing.Comments,
        FindingDetails = existing.FindingDetails
      });
    }

    var orderedFindings = mergedFindings
      .OrderBy(f => f.VulnId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var mergedChecklist = new CklChecklist
    {
      FilePath = importedChecklist.FilePath,
      ImportedAt = importedChecklist.ImportedAt,
      AssetName = importedChecklist.AssetName,
      HostName = importedChecklist.HostName,
      HostIp = importedChecklist.HostIp,
      HostMac = importedChecklist.HostMac,
      HostFqdn = importedChecklist.HostFqdn,
      StigTitle = importedChecklist.StigTitle,
      StigVersion = importedChecklist.StigVersion,
      StigRelease = importedChecklist.StigRelease,
      Findings = orderedFindings
    };

    return Task.FromResult(new CklMergeResult
    {
      Strategy = strategy,
      MergedFindings = orderedFindings,
      Conflicts = conflicts,
      MergedChecklist = mergedChecklist
    });
  }

  private static string ResolveStatus(
    CklConflictResolutionStrategy strategy,
    string cklStatus,
    ControlResult existing,
    DateTimeOffset cklImportedAt)
  {
    return strategy switch
    {
      CklConflictResolutionStrategy.CklWins => NormalizeToKnownCklStatus(cklStatus),
      CklConflictResolutionStrategy.StigForgeWins => MapToCklStatus(existing.Status),
      CklConflictResolutionStrategy.MostRecent => ResolveMostRecentStatus(cklStatus, existing, cklImportedAt),
      CklConflictResolutionStrategy.Manual => NormalizeToKnownCklStatus(cklStatus),
      _ => NormalizeToKnownCklStatus(cklStatus)
    };
  }

  private static string ResolveMostRecentStatus(string cklStatus, ControlResult existing, DateTimeOffset cklImportedAt)
  {
    var cklTimestamp = cklImportedAt;
    var stigForgeTimestamp = ResolveStigForgeTimestamp(existing);
    if (stigForgeTimestamp.HasValue && stigForgeTimestamp.Value > cklTimestamp)
      return MapToCklStatus(existing.Status);

    return NormalizeToKnownCklStatus(cklStatus);
  }

  private static DateTimeOffset? ResolveStigForgeTimestamp(ControlResult existing)
  {
    if (string.IsNullOrWhiteSpace(existing.SourceFile))
      return null;

    if (!File.Exists(existing.SourceFile))
      return null;

    return new DateTimeOffset(File.GetLastWriteTimeUtc(existing.SourceFile), TimeSpan.Zero);
  }

  private static CklFinding CloneFinding(CklFinding finding)
  {
    return new CklFinding
    {
      VulnId = finding.VulnId,
      RuleId = finding.RuleId,
      RuleTitle = finding.RuleTitle,
      Severity = finding.Severity,
      Status = NormalizeToKnownCklStatus(finding.Status),
      Comments = finding.Comments,
      FindingDetails = finding.FindingDetails,
      CheckContent = finding.CheckContent,
      FixText = finding.FixText
    };
  }

  private static string PreferText(string? primary, string? fallback)
  {
    if (!string.IsNullOrWhiteSpace(primary))
      return primary;

    return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;
  }

  private static string BuildControlKey(string? vulnId, string? ruleId)
  {
    if (!string.IsNullOrWhiteSpace(vulnId))
      return "VULN:" + vulnId.Trim();

    if (!string.IsNullOrWhiteSpace(ruleId))
      return "RULE:" + ruleId.Trim();

    return string.Empty;
  }

  private static string NormalizeCklStatus(string? status)
  {
    return NormalizeToken(status) switch
    {
      "notafinding" or "notafind" or "pass" => "NotAFinding",
      "open" or "fail" or "error" => "Open",
      "notapplicable" or "notapp" or "na" => "Not_Applicable",
      "notreviewed" or "nr" or "notchecked" or "informational" => "Not_Reviewed",
      _ => "Not_Reviewed"
    };
  }

  private static string NormalizeStigForgeStatus(string? status)
  {
    return NormalizeToken(status) switch
    {
      "pass" or "compliant" or "notafinding" => "NotAFinding",
      "fail" or "noncompliant" or "open" or "error" => "Open",
      "notapplicable" or "na" => "Not_Applicable",
      "notreviewed" or "notchecked" or "informational" => "Not_Reviewed",
      _ => "Not_Reviewed"
    };
  }

  private static string NormalizeToKnownCklStatus(string? status)
  {
    return NormalizeCklStatus(status);
  }

  private static string MapToCklStatus(string? stigForgeStatus)
  {
    return NormalizeStigForgeStatus(stigForgeStatus);
  }

  private static string NormalizeToken(string? value)
  {
    return StatusNormalizer.Normalize(value);
  }
}

public enum CklConflictResolutionStrategy
{
  CklWins,
  StigForgeWins,
  MostRecent,
  Manual
}

public sealed class CklMergeResult
{
  public CklConflictResolutionStrategy Strategy { get; set; }
  public IReadOnlyList<CklFinding> MergedFindings { get; set; } = [];
  public IReadOnlyList<CklConflict> Conflicts { get; set; } = [];
  public CklChecklist MergedChecklist { get; set; } = new();
}

public sealed class CklConflict
{
  public string VulnId { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string CklStatus { get; set; } = string.Empty;
  public string StigForgeStatus { get; set; } = string.Empty;
  public string ResolvedStatus { get; set; } = string.Empty;
  public bool RequiresManualResolution { get; set; }
  public DateTimeOffset CklImportedAt { get; set; }
  public DateTimeOffset? StigForgeEvaluatedAt { get; set; }
  public CklConflictResolutionStrategy StrategyApplied { get; set; }
}
